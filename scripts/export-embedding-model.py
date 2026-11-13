#!/usr/bin/env python3
"""
의미 검색에 쓸 임베딩 모델을 ONNX로 바꿔 data/models/{이름}/에 둔다. 모델마다 한 번만 하면 된다.

    python3 -m venv .build/venv
    .build/venv/bin/pip install torch transformers onnx onnxruntime onnxscript huggingface_hub
    .build/venv/bin/python scripts/export-embedding-model.py                     # 기본 모델
    .build/venv/bin/python scripts/export-embedding-model.py nlpai-lab/KURE-v1   # 비교할 다른 모델
    dotnet run --project src/HnPaper.Web -- semantic-check

사이트(TextEmbedder)는 tokenizer.json을 Microsoft.ML.Tokenizers의 Unigram 토크나이저로 읽으므로
XLM-RoBERTa 계열(arctic-embed-l-v2.0, bge-m3, KURE 등)만 쓸 수 있다.
"""

import argparse
import json
import sys
import tempfile
import unicodedata
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
import torch
from huggingface_hub import HfApi, snapshot_download
from onnx import TensorProto, helper, numpy_helper
from onnxruntime.quantization.matmul_nbits_quantizer import DefaultWeightOnlyQuantConfig, MatMulNBitsQuantizer
from transformers import AutoModel, AutoTokenizer

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_REPO = "dragonkue/snowflake-arctic-embed-l-v2.0-ko"

# arctic-ko: 기사 300개로 시험했을 때 관계없는 검색어("김치찌개 레시피", "축구 경기 결과")의 1위가 0.18~0.22,
# 키워드 없이 찾은 관련 기사("개인정보 보호" → 테크 기업의 개인 정보 수집 글)가 0.25~0.30이었다.
# embedding.json에서 고쳐도 벡터를 다시 만들 필요는 없다.
MIN_SCORE = {
    "dragonkue/snowflake-arctic-embed-l-v2.0-ko": 0.25,
}

# 토크나이저끼리 다르게 나누기 쉬운 글을 섞었다.
SAMPLES = [
    "query: 쿠버네티스 비용 줄이기",
    "query: AI 코딩 도구",
    "query: rust memory safety",
    "애플이 새 맥북 프로를 공개했습니다. M5 칩은 이전 세대보다 GPU 성능이 크게 올랐습니다.",
    "오픈소스 데이터베이스 PostgreSQL 18이 나왔습니다. 비동기 I/O로 읽기 성능이 좋아졌습니다.",
    "**한 줄 요약: 사용량만큼 요금을 받는 서비스는 한도에 닿으면 멈추는 상한을 기본으로 켜 둬야 합니다.**",
    "HN 반응\n- 클라우드 업체의 대응이 너무 늦었다는 지적과, 과금 집계가 늦어 즉시 차단은 어렵다는 반론이 맞섰습니다.",
    "ＦＵＬＬ　ＷＩＤＴＨ　ｔｅｘｔ，　①②③　ﬁ ligature　™ ½ ㎏ ㈜ ℃",
    "\u1112\u1161\u11ab\u1100\u1173\u11af 자모로 풀어 쓴 한글",
    "이모지 🚀🔥👨‍👩‍👧 와 기호 → ← ≠ … “따옴표” ‘작은따옴표’ · 가운뎃점",
    "fn main() {\n    println!(\"Hello, world!\");\n}\t탭과  두 칸   공백",
    "日本語のテキストと中文文本 그리고 English mixed together. Ελληνικά, русский, العربية, हिन्दी.",
    "Show HN: I built a tiny SQLite-backed queue in 300 lines of Go",
    "C++23과 C#, .NET 10, Node.js v24.1.0을 비교했습니다. 처리량은 37.5% 늘고 p99 지연은 120ms → 85ms로 줄었습니다.",
    "자세한 내용은 https://example.com/path?q=1&lang=ko#section 에서 볼 수 있습니다. 문의: someone@example.org",
    "개발자 사이먼 윌리슨의 주장입니다. 코딩 에이전트로 누구나 유료 API를 쓰는 앱을 쉽게 만들게 되면서, "
    "밤새 폭주한 서비스가 거액을 청구하는 사고도 늘어날 것이라고 봤습니다. AWS(9월)와 Google Cloud(7월)가 "
    "지출 한도를 잇달아 내놓아 업계도 같은 방향으로 움직이고 있습니다. 상한 해제는 사용자가 위험을 확인하고 "
    "직접 고르게(옵트인) 하자고 했습니다.",
    "가" * 2000,
]


def main():
    parser = argparse.ArgumentParser(description="임베딩 모델을 ONNX로 바꿔 data/models/에 둔다.")
    parser.add_argument("repo", nargs="?", default=DEFAULT_REPO, help=f"Hugging Face 모델 저장소 (기본값: {DEFAULT_REPO})")
    parser.add_argument("--name", help="data/models/ 아래 폴더 이름 (기본값: 저장소 이름)")
    parser.add_argument("--out", type=Path, help="모델 폴더 위치 (기본값: data/models/{이름})")
    parser.add_argument("--max-tokens", type=int, default=512, help="글 하나에서 쓰는 최대 토큰 수 (기본값: 512)")
    args = parser.parse_args()

    name = args.name or args.repo.split("/")[-1]
    out = (args.out or ROOT / "data" / "models" / name).resolve()

    print(f"받는 중: {args.repo}")
    revision = HfApi().model_info(args.repo).sha
    src = Path(snapshot_download(args.repo, revision=revision, allow_patterns=["*.json", "model.safetensors"]))

    pooling = read_pooling(src)
    query_prefix = read_query_prefix(src)
    print(f"벡터 고르기: {pooling}, 검색어 접두어: {query_prefix!r}")

    tokenizer = AutoTokenizer.from_pretrained(src)
    model = AutoModel.from_pretrained(src).float().eval()
    dimensions = model.config.hidden_size

    texts = [nfc(t) for t in SAMPLES]
    ids = [tokenizer(t, truncation=True, max_length=args.max_tokens)["input_ids"] for t in texts]
    reference = embed_torch(model, pooling, ids)

    out.mkdir(parents=True, exist_ok=True)
    target = out / "model.onnx"
    with tempfile.TemporaryDirectory() as tmp:
        fp32_path = Path(tmp) / "model.onnx"
        export(model, pooling, fp32_path)
        print("크기를 줄이는 중")
        compact = quantize(onnx.load(str(fp32_path)))
        partial = out / "model.onnx.tmp"
        onnx.save_model(compact, str(partial), save_as_external_data=False)

    vectors = embed_onnx(partial, ids)
    cosines = (reference * vectors).sum(axis=1)
    print(f"원래 모델과 벡터의 코사인 유사도: 최소 {cosines.min():.4f}, 평균 {cosines.mean():.4f}")
    if cosines.min() < 0.995:
        partial.unlink()
        sys.exit("줄인 모델의 벡터가 원래 모델과 너무 다릅니다.")
    partial.replace(target)

    (out / "tokenizer.json").write_bytes((src / "tokenizer.json").read_bytes())
    config = {
        "source": args.repo,
        "revision": revision,
        "pooling": pooling,
        "quantization": "MatMulNBits 8bit block 32 (accuracy level 4), word embeddings fp16",
        "dimensions": dimensions,
        "maxTokens": args.max_tokens,
        "queryPrefix": query_prefix,
        "documentPrefix": "",
        "minScore": MIN_SCORE.get(args.repo, 0.25),
        "bosId": tokenizer.cls_token_id,
        "eosId": tokenizer.sep_token_id,
        "padId": tokenizer.pad_token_id,
    }
    (out / "embedding.json").write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    # text를 NFC로 맞추기 전의 글로 남겨, .NET이 NFC로 맞추는지까지 semantic-check가 보게 한다.
    check = [{"text": t, "ids": i, "embedding": [round(float(x), 6) for x in v]} for t, i, v in zip(SAMPLES, ids, vectors)]
    (out / "check.json").write_text(json.dumps(check, ensure_ascii=False) + "\n", encoding="utf-8")

    size = target.stat().st_size / 1024 / 1024
    print(f"완료: {out} (model.onnx {size:.0f}MB, {dimensions}차원)")
    print("확인: dotnet run --project src/HnPaper.Web -- semantic-check" + (f" --model {name}" if args.repo != DEFAULT_REPO else ""))


def nfc(text: str) -> str:
    """Hugging Face 토크나이저는 자모로 풀어 쓴 한글(NFD)을 완성형과 다르게 나누므로, 사이트(TextEmbedder)처럼 먼저 맞춘다."""
    return unicodedata.normalize("NFC", text)


def read_pooling(src: Path) -> str:
    path = src / "1_Pooling" / "config.json"
    if not path.exists():
        return "cls"
    config = json.loads(path.read_text(encoding="utf-8"))
    if config.get("pooling_mode_cls_token"):
        return "cls"
    if config.get("pooling_mode_mean_tokens"):
        return "mean"
    sys.exit(f"지원하지 않는 벡터 고르기 방식입니다: {config}")


def read_query_prefix(src: Path) -> str:
    path = src / "config_sentence_transformers.json"
    if not path.exists():
        return ""
    return (json.loads(path.read_text(encoding="utf-8")).get("prompts") or {}).get("query", "")


class Embedder(torch.nn.Module):
    def __init__(self, model, pooling: str):
        super().__init__()
        self.model = model
        self.pooling = pooling

    def forward(self, input_ids, attention_mask):
        hidden = self.model(input_ids=input_ids, attention_mask=attention_mask).last_hidden_state
        if self.pooling == "cls":
            pooled = hidden[:, 0]
        else:
            mask = attention_mask.unsqueeze(-1).to(hidden.dtype)
            pooled = (hidden * mask).sum(dim=1) / mask.sum(dim=1).clamp(min=1e-9)
        return torch.nn.functional.normalize(pooled, p=2, dim=-1)


def export(model, pooling: str, path: Path):
    print("ONNX로 바꾸는 중")
    ids = torch.tensor([[0, 6, 2], [0, 7, 2]], dtype=torch.long)
    mask = torch.ones_like(ids)
    kwargs = dict(
        input_names=["input_ids", "attention_mask"],
        output_names=["embedding"],
        dynamic_axes={"input_ids": {0: "batch", 1: "sequence"}, "attention_mask": {0: "batch", 1: "sequence"},
                      "embedding": {0: "batch"}},
        opset_version=17,
        external_data=True,
    )
    with torch.no_grad():
        try:
            torch.onnx.export(Embedder(model, pooling), (ids, mask), str(path), dynamo=False, **kwargs)
        except TypeError:  # dynamo 인자가 없는 옛 PyTorch
            torch.onnx.export(Embedder(model, pooling), (ids, mask), str(path), **kwargs)


def quantize(model: onnx.ModelProto) -> onnx.ModelProto:
    # 행렬곱 가중치는 32개씩 묶어 8비트로, 단어 임베딩 표(25만×1024, 모델의 절반 가까이)는 fp16으로 두면
    # 0.9GB에 fp32와 코사인 0.998 이상이다. 같은 시험에서 채널별 동적 int8은 0.97~0.98, 임베딩 표까지 int8로 하면 0.89~0.93이었다.
    # 8비트 가중치를 fp32로 계산하면(accuracy_level 0) x64 PC에서 검색어 하나에 160ms가 걸려서(fp32 모델은 22ms) int8로 계산한다(4).
    initializers = {i.name: i for i in model.graph.initializer}
    for node in list(model.graph.node):
        table = initializers.get(node.input[0]) if node.op_type == "Gather" else None
        if table is None or not table.dims or table.dims[0] < 100_000:
            continue
        half = numpy_helper.from_array(numpy_helper.to_array(table).astype(np.float16), table.name + "_fp16")
        model.graph.initializer.remove(table)
        model.graph.initializer.append(half)
        output = node.output[0]
        node.input[0] = half.name
        node.output[0] = output + "_fp16"
        cast = helper.make_node("Cast", [output + "_fp16"], [output], to=TensorProto.FLOAT, name=node.name + "_to_fp32")
        model.graph.node.insert(list(model.graph.node).index(node) + 1, cast)

    config = DefaultWeightOnlyQuantConfig(block_size=32, is_symmetric=True, bits=8, accuracy_level=4)
    quantizer = MatMulNBitsQuantizer(model, block_size=32, is_symmetric=True, accuracy_level=4, algo_config=config)
    quantizer.process()
    return quantizer.model.model


def embed_torch(model, pooling: str, ids_list: list[list[int]]) -> np.ndarray:
    embedder = Embedder(model, pooling)
    rows = []
    with torch.no_grad():
        for ids in ids_list:
            tensor = torch.tensor([ids], dtype=torch.long)
            rows.append(embedder(tensor, torch.ones_like(tensor))[0].numpy())
    return np.stack(rows)


def embed_onnx(path: Path, ids_list: list[list[int]]) -> np.ndarray:
    session = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])
    rows = []
    for ids in ids_list:
        array = np.array([ids], dtype=np.int64)
        rows.append(session.run(["embedding"], {"input_ids": array, "attention_mask": np.ones_like(array)})[0][0])
    return np.stack(rows)


if __name__ == "__main__":
    main()
