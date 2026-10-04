# /// script
# requires-python = ">=3.11"
# dependencies = ["numpy", "pillow"]
# ///
"""대표 이미지가 없는 기사에 쓰는 그라데이션 패널 이미지를 만든다.

파스텔 색 덩어리를 번지게 겹치고, 흐릿한 사선 빛줄기와 옅은 입자감을 더한다.
사이트(GradientLibrary)는 출력 폴더의 이미지를 모두 읽어 기사 id로 하나를 고르므로,
이미지를 더 만들거나 지우면 다음 실행부터 바로 반영된다.

    uv run scripts/generate-gradients.py                 # 24장, 시드 1
    uv run scripts/generate-gradients.py --count 40 --seed 7
"""

import argparse
import pathlib

import numpy as np
from PIL import Image, ImageFilter

WIDTH, HEIGHT = 1200, 675  # 16:9, 지면의 썸네일 비율
WORK_W, WORK_H = 480, 270  # 저해상도로 그린 뒤 키워서 부드럽게 만든다

PALETTES = [
    ["#1aa6c4", "#4cc3f0", "#3ee0c8", "#c4f27c", "#e8fbf6"],  # 청록·라임
    ["#6f8dff", "#a7bcff", "#8f8cff", "#d9b8ff", "#eef0ff"],  # 페리윙클·라벤더
    ["#5fc9ea", "#e3f8f3", "#c3f58a", "#9ae6f5", "#ffffff"],  # 라임·아쿠아
    ["#8fa0ff", "#c9b8fd", "#b9b2ff", "#7ee0f5", "#f3f0ff"],  # 라벤더·시안
    ["#ff86b3", "#ffc4dc", "#ff9ec7", "#ffe680", "#fff6e0"],  # 분홍·노랑
    ["#1f6fff", "#7cc4ff", "#5aa9ff", "#b0e4ff", "#eaf6ff"],  # 하늘·파랑
    ["#ff8a7a", "#ffdcae", "#ffb38a", "#ffa3b8", "#fff1e6"],  # 복숭아·코랄
    ["#22d3ee", "#34d399", "#5eead4", "#8b93f8", "#e9fffa"],  # 에메랄드·인디고
    ["#c79bff", "#ffc6d2", "#f5b3ff", "#9aa5ff", "#fff0fb"],  # 노을 보라
    ["#93c5fd", "#e6f4fe", "#a5f3fc", "#c7d2fe", "#ffffff"],  # 얼음
]


def rgb(hex_color: str) -> np.ndarray:
    return np.array([int(hex_color[i:i + 2], 16) for i in (1, 3, 5)], dtype=float) / 255


def render(rng: np.random.Generator, palette: list[str]) -> Image.Image:
    colors = [rgb(c) for c in palette]
    y, x = np.mgrid[0:WORK_H, 0:WORK_W]
    x = x / WORK_W
    y = y / WORK_H * (WORK_H / WORK_W) * (16 / 9)  # 화면 비율대로 좌표를 맞춘다

    # 바탕: 임의 각도의 선형 그라데이션
    angle = rng.uniform(0, 2 * np.pi)
    t = x * np.cos(angle) + y * np.sin(angle)
    t = (t - t.min()) / (t.max() - t.min())
    img = colors[0] * (1 - t[..., None]) + colors[1] * t[..., None]

    # 색 덩어리: 회전한 타원형 가우시안을 여러 개 겹친다
    for color in colors[2:] + [colors[rng.integers(len(colors))]]:
        cx, cy = rng.uniform(-0.1, 1.1), rng.uniform(-0.1, 1.1)
        sx, sy = rng.uniform(0.18, 0.5), rng.uniform(0.15, 0.45)
        rot = rng.uniform(0, np.pi)
        dx, dy = x - cx, y - cy
        u = dx * np.cos(rot) + dy * np.sin(rot)
        v = -dx * np.sin(rot) + dy * np.cos(rot)
        alpha = np.exp(-(u ** 2 / (2 * sx ** 2) + v ** 2 / (2 * sy ** 2))) * rng.uniform(0.55, 0.9)
        img = img * (1 - alpha[..., None]) + color * alpha[..., None]

    # 빛줄기: 살짝 휜 사선 띠를 밝게 겹친다(스크린 합성)
    for _ in range(rng.integers(1, 3)):
        theta = rng.uniform(0, np.pi)
        offset = rng.uniform(-0.3, 0.3)
        width = rng.uniform(0.05, 0.13)
        curve = rng.uniform(-0.5, 0.5)
        along = (x - 0.5) * np.sin(theta) - (y - 0.5) * np.cos(theta)
        across = (x - 0.5) * np.cos(theta) + (y - 0.5) * np.sin(theta) + offset + curve * along ** 2
        light = np.exp(-(across ** 2) / (2 * width ** 2)) * rng.uniform(0.25, 0.5)
        img = img + (1 - img) * light[..., None]

    image = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
    image = image.resize((WIDTH, HEIGHT), Image.BICUBIC).filter(ImageFilter.GaussianBlur(16))

    # 옅은 입자감: 큰 화면에서 띠 무늬(밴딩)가 보이지 않게 한다
    pixels = np.asarray(image).astype(float) + rng.normal(0, 2.0, (HEIGHT, WIDTH, 1))
    return Image.fromarray(np.clip(pixels, 0, 255).astype(np.uint8))


def main() -> None:
    root = pathlib.Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--count", type=int, default=24, help="만들 이미지 수 (기본 24)")
    parser.add_argument("--seed", type=int, default=1, help="같은 시드면 같은 이미지가 나온다 (기본 1)")
    parser.add_argument("--out", type=pathlib.Path, default=root / "src/HnPaper.Web/wwwroot/img/gradients")
    args = parser.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(args.seed)
    for i in range(args.count):
        palette = PALETTES[i % len(PALETTES)]
        path = args.out / f"g{i + 1:02d}.webp"
        render(rng, palette).save(path, "WEBP", quality=84, method=6)
        print(path.relative_to(root) if path.is_relative_to(root) else path)


if __name__ == "__main__":
    main()
