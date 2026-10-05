# /// script
# requires-python = ">=3.11"
# dependencies = ["numpy", "pillow", "scipy"]
# ///
"""대표 이미지가 없는 기사에 쓰는 패널 이미지를 만든다.

꽃잎이나 비단 자락을 아주 가까이서 초점을 완전히 흐려 찍은 필름 사진처럼 보이게 그린다.
한 점에서 뻗어 나온 큰 꽃잎 여러 장을 뒤에서 앞으로 겹치고, 장마다 깊이에 따라 가장자리를 크게 흐린다(또렷한 곳은 없다).
겹친 곳에는 그림자를, 가장자리에는 번지는 빛을, 꽃잎을 따라 흐르는 넓은 빛줄기를 넣고,
밝은 곳은 번지게 하고 필름 같은 입자감을 더한다. 색은 한 계열로 고르고 가끔 다른 색 꽃잎 하나를 섞는다.

사이트(GradientLibrary)는 출력 폴더의 이미지를 모두 읽어 기사 id로 하나를 고르므로,
이미지를 더 만들거나 지우면 다음 실행부터 바로 반영된다.

    uv run scripts/generate-gradients.py                 # 32장, 시드 1
    uv run scripts/generate-gradients.py --count 40 --seed 7
"""

import argparse
import pathlib

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, shift, zoom

WIDTH = 1600  # 높이는 16:9(지면의 썸네일 비율)로 정한다
WORK = 4  # 꽃잎은 1/WORK 크기로 그린다
MARGIN = 0.35  # 화면 밖으로 더 그리는 여백(단위: 이미지 높이)

# 색 계열마다 바탕색, 꽃잎 색(어두운 것부터), 가끔 섞는 다른 색
PALETTES = [
    dict(bg=["#1a8cff", "#3fb6f5", "#9fe6f0"], petals=["#0f6fff", "#2a9cff", "#4cc7f0", "#8fe3ee", "#dff8fb"], accent=[]),  # 하늘·물빛
    dict(bg=["#ff4f7a", "#ff8a4c", "#ffd27a"], petals=["#e8336d", "#ff5f6d", "#ff8c4a", "#ffb85c", "#ffe3a3"], accent=["#7a5cff"]),  # 노을
    dict(bg=["#c9d81f", "#e4ef4a", "#f4fbb0"], petals=["#b5c900", "#d4e41c", "#eef55a", "#c8f0e0", "#fbffe0"], accent=["#5fd6e8"]),  # 연두·노랑
    dict(bg=["#7a3cf0", "#b46cff", "#f3c6ff"], petals=["#6a2cf0", "#9b4dff", "#cf7bff", "#f0a6ff", "#ffe2fb"], accent=["#5aa0ff"]),  # 난초
    dict(bg=["#0b8f6a", "#1fb38a", "#8fe0c0"], petals=["#067a5a", "#14a07a", "#36c79b", "#7fe3c4", "#d8fbef"], accent=[]),  # 에메랄드
    dict(bg=["#ff5c8f", "#ff9bb8", "#ffe0ea"], petals=["#f0346f", "#ff6f99", "#ffa1bd", "#ffcadb", "#fff1f5"], accent=["#ffb37a"]),  # 장미
    dict(bg=["#ff7a1f", "#ff9f3a", "#ffc46b"], petals=["#f05a14", "#ff7f26", "#ffa340", "#ffc36e", "#ffe6b8"], accent=["#ff5a5a"]),  # 호박색
    dict(bg=["#5a62f5", "#8f95ff", "#d9d6ff"], petals=["#4b4ff0", "#6f74ff", "#9c9cff", "#c4bdff", "#ece8ff"], accent=["#ff9bd2"]),  # 페리윙클
    dict(bg=["#58c21a", "#8fdc1f", "#d6f56a"], petals=["#3fae10", "#6fcf1a", "#9fe62a", "#d4f55a", "#f5ffd0"], accent=["#ffe14a"]),  # 라임
    dict(bg=["#3e9dff", "#86c8ff", "#e0f2ff"], petals=["#2f8dff", "#6cb8ff", "#ffbf1f", "#ffd95a", "#fff1b0"], accent=[]),  # 금잔화·하늘
    dict(bg=["#6f9cff", "#e9e4d4", "#ff9d4a"], petals=["#5b8cff", "#9db8ff", "#f3ecdc", "#ffc47a", "#ff8a3a"], accent=[]),  # 하늘·살구
    dict(bg=["#00a3c4", "#3fd0e0", "#c8f5f0"], petals=["#008fb3", "#13b8d1", "#52dbe3", "#a6f0ea", "#e6fffb"], accent=["#3ccf73"]),  # 석호
    dict(bg=["#bfe6ff", "#e8dcff", "#fff0f5"], petals=["#9ed4ff", "#c6b8ff", "#ffc2dc", "#b8f0d8", "#fff3c4"], accent=[]),  # 오팔
    dict(bg=["#ff5a6e", "#ff8d8a", "#ffd0c4"], petals=["#f03a5a", "#ff6470", "#ff9688", "#ffc2b0", "#fff0ea"], accent=["#ffb340"]),  # 산호
    dict(bg=["#9fd0ff", "#d4ecff", "#f7fbff"], petals=["#6fb6ff", "#a4d2ff", "#d2e9ff", "#f2f9ff", "#c9c3ff"], accent=[]),  # 빙하
    dict(bg=["#00a9a0", "#2a7fff", "#5236ff"], petals=["#00b3a6", "#1e9bd7", "#2a6bff", "#5a4cff", "#a9c3ff"], accent=["#b8f05a"]),  # 청록·남색
]


def linear(hex_color: str) -> np.ndarray:
    """sRGB 색을 선형 RGB로 바꾼다. 빛이 섞이는 계산은 선형 공간에서 해야 사진처럼 번진다."""
    c = np.array([int(hex_color[i:i + 2], 16) for i in (1, 3, 5)], dtype=np.float32) / 255
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def srgb(c: np.ndarray) -> np.ndarray:
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def ramp(colors: list[np.ndarray], t: np.ndarray) -> np.ndarray:
    """0..1 값을 색 목록을 따라 이어 칠한다."""
    t = np.clip(t, 0, 1) * (len(colors) - 1)
    i = np.minimum(t.astype(int), len(colors) - 2)
    f = (t - i)[..., None]
    stack = np.stack(colors)
    return stack[i] * (1 - f) + stack[i + 1] * f


def wiggle(rng: np.random.Generator, t: np.ndarray, count: int, freq: tuple[float, float]) -> np.ndarray:
    """부드러운 1차원 잡음(사인파 몇 개의 합). 대략 -1..1."""
    out = np.zeros_like(t)
    for _ in range(count):
        out += np.sin(t * rng.uniform(*freq) + rng.uniform(0, 2 * np.pi))
    return out / np.sqrt(count)


def petal_shape(u: np.ndarray, waist: float) -> np.ndarray:
    """꽃잎의 폭(0..1). 밑동(u=0)은 가늘고 waist에서 가장 넓고 끝(u=1)은 둥글게 닫힌다."""
    u = np.clip(u, 0, 1)
    base = np.sin(np.pi / 2 * np.minimum(u / waist, 1)) ** 0.7
    tip = np.sqrt(np.clip(1 - ((u - waist) / (1 - waist)) ** 2, 0, 1))
    return np.where(u < waist, base, tip)


def blur(a: np.ndarray, sigma: float) -> np.ndarray:
    """가로세로로만 흐린다(색 채널끼리는 섞지 않는다)."""
    return gaussian_filter(a, sigma=(sigma, sigma) + (0,) * (a.ndim - 2), mode="nearest")


def render(rng: np.random.Generator, palette: dict, width: int) -> Image.Image:
    height = width * 9 // 16
    aspect = width / height
    # 모두 초점이 나가 잔세부가 없으므로 1/WORK 크기로 그려 흐린 뒤 키운다.
    # 화면 밖 여백까지 그려, 가장자리 근처도 화면 밖 꽃잎과 함께 흐려지게 한다.
    rows, cols = height // WORK, width // WORK
    pad = int(MARGIN * rows)
    px = 1 / rows  # 작업 픽셀 하나의 크기(단위: 이미지 높이)
    y, x = np.mgrid[-pad:rows + pad, -pad:cols + pad].astype(np.float32) * px
    petals = [linear(c) for c in palette["petals"]]
    accents = [linear(c) for c in palette["accent"]]

    # 바탕: 임의 방향의 그라데이션 위에 아주 흐린 색 덩어리(멀리 있는 꽃잎)를 얹는다
    angle = rng.uniform(0, 2 * np.pi)
    t = (x - aspect / 2) * np.cos(angle) + (y - 0.5) * np.sin(angle)
    canvas = ramp([linear(c) for c in palette["bg"]], (t - t.min()) / (t.max() - t.min()))
    for _ in range(rng.integers(2, 4)):
        color = petals[rng.integers(len(petals))]
        cx, cy, radius = rng.uniform(0, aspect), rng.uniform(0, 1), rng.uniform(0.25, 0.6)
        blob = np.exp(-((x - cx) ** 2 + (y - cy) ** 2) / (2 * radius ** 2))[..., None] * rng.uniform(0.3, 0.7)
        canvas = canvas * (1 - blob) + color * blob

    # 꽃잎이 뻗어 나오는 중심. 화면 가까이면 꽃의 한쪽을, 멀리 밖이면 화면을 가로지르는 비단 자락처럼 보인다.
    sweep = rng.random() < 0.6
    if sweep:
        direction = rng.uniform(0, 2 * np.pi)
        distance = rng.uniform(1.0, 2.2)
        cx = aspect / 2 + np.cos(direction) * distance * aspect / 1.4
        cy = 0.5 + np.sin(direction) * distance
        toward = np.arctan2(0.5 - cy, aspect / 2 - cx)
        count = rng.integers(6, 10)
        reach = np.hypot(aspect / 2 - cx, 0.5 - cy)
        angles = toward + rng.uniform(-0.6, 0.6, count) / reach
        lengths = reach + rng.uniform(0.4, 1.4, count)
        widths = rng.uniform(0.2, 0.5, count)
    else:
        # 화면 바로 바깥에 두어 꽃의 한쪽을 크게 당겨 찍은 것처럼 보이게 한다(꽃잎이 모이는 한가운데는 보이지 않는다)
        edge_x, edge_y = rng.uniform(-0.5, -0.05), rng.uniform(-0.5, 0.3)
        cx = edge_x if rng.random() < 0.5 else aspect - edge_x
        cy = edge_y if rng.random() < 0.5 else 1 - edge_y
        count = rng.integers(8, 13)
        angles = rng.uniform(0, 2 * np.pi) + np.sort(rng.uniform(0, 2 * np.pi, count))
        lengths = rng.uniform(1.1, 2.4, count)
        widths = rng.uniform(0.2, 0.5, count)

    dx, dy = x - cx, y - cy
    r = np.sqrt(dx ** 2 + dy ** 2) + 1e-4
    theta = np.arctan2(dy, dx)
    light = rng.uniform(0, 2 * np.pi)  # 빛이 오는 쪽
    depths = (np.arange(count) + rng.uniform(0, 1, count)) / count  # 먼저 그리는 꽃잎이 뒤에 있다(0 맨 뒤, 1 맨 앞)
    # 앞쪽 꽃잎 하나를 가장 덜 흐리게 하고 화면을 지나가게 해, 어느 이미지에나 알아볼 만한 형태가 하나는 있게 한다
    hero = count - 1 - rng.integers(0, 3)
    focus = depths[hero]
    accent_at = rng.integers(count) if accents and rng.random() < 0.8 else -1

    for order, i in enumerate(rng.permutation(count)):
        # 모두 흐리고, 기준 깊이에서 멀수록 더 흐리다(가우스 흐림의 표준편차, 단위: 이미지 높이)
        defocus = rng.uniform(0.035, 0.06) + 0.2 * abs(depths[order] - focus) ** 1.1
        sigma = defocus / px
        twist = rng.uniform(-0.7, 0.7)
        bend = rng.uniform(-0.25, 0.25)
        if order == hero:
            hx, hy = rng.uniform(0.3, aspect - 0.3), rng.uniform(0.25, 0.75)
            reach = np.hypot(hx - cx, hy - cy)
            angles[i] = np.arctan2(hy - cy, hx - cx) - twist * reach - bend * reach ** 2
            # 끝이 화면 밖에 있게 길고 넓게 그린다. 끝이 다 보이면 꽃잎이 아니라 물건처럼 보인다.
            lengths[i] = reach + rng.uniform(1.2, 2.0)
            widths[i] = rng.uniform(0.35, 0.7)
        spine = angles[i] + twist * r + bend * r ** 2
        delta = (theta - spine + np.pi) % (2 * np.pi) - np.pi
        u = r / lengths[i]
        half = widths[i] * petal_shape(u, rng.uniform(0.35, 0.65)) * (1 + 0.15 * wiggle(rng, u, 3, (2, 6)))
        stretch = np.sqrt(1 + (r * (twist + 2 * bend * r)) ** 2)
        dist = np.maximum((np.abs(delta) * r - half) / stretch, r - lengths[i])  # 가장자리까지 거리(안쪽이 음수)
        across = np.clip(delta * r / np.maximum(half, 1e-3), -1.3, 1.3)  # 꽃잎 폭 방향 -1..1
        shape = np.clip(0.5 - dist / px, 0, 1) * rng.uniform(0.82, 0.97)  # 흐리기 전의 또렷한 꽃잎

        # 아래 꽃잎에 지는 그림자: 빛 반대쪽으로 조금 밀린 꽃잎 모양을 더 크게 흐린다
        offset = 0.05 / px
        cast = shift(shape, (-np.sin(light) * offset, -np.cos(light) * offset), order=1, mode="nearest")
        canvas *= 1 - blur(cast, sigma * 1.5 + 0.04 / px)[..., None] * rng.uniform(0.15, 0.35)

        # 색: 밑동은 짙고 끝은 밝다. 빛을 받는 쪽 반은 밝고 반대쪽은 어둡다.
        if i == accent_at:
            dark = accents[rng.integers(len(accents))]
            bright = dark * 0.35 + 0.65 * np.minimum(dark * 2.2, 1)
        else:
            k = rng.integers(len(petals) - 1)
            dark, bright = petals[k], petals[min(len(petals) - 1, k + rng.integers(1, 3))]
        along = np.clip(u * rng.uniform(0.9, 1.6) + rng.uniform(-0.3, 0.1), 0, 1)
        color = dark * (1 - along[..., None]) + bright * along[..., None]
        side = np.cos(angles[i] + np.pi / 2 - light)
        color = color * (1 + 0.5 * side * across)[..., None]

        # 비단 같은 광택: 꽃잎 폭의 한 곳을 따라 흐르는 넓고 밝은 띠
        sheen = np.exp(-((across - rng.uniform(-0.6, 0.6)) / rng.uniform(0.2, 0.45)) ** 2) * rng.uniform(0.1, 0.35)
        color = color + (np.minimum(bright * 1.4, 1) - color) * sheen[..., None]

        # 가장자리 안쪽의 밝은 띠. 흐리면 꽃잎 둘레로 번지는 빛이 된다.
        rim = np.exp(-((dist + 0.03) / 0.03) ** 2) * rng.uniform(0.1, 0.3)
        color = color + (np.minimum(bright * 1.6, 1) - color) * rim[..., None]

        # 빛줄기: 꽃잎을 따라 흐르는 넓고 부드러운 밝은 띠 몇 줄(흐린 풀잎·꽃잎 사이로 든 빛처럼).
        # 꽃잎보다 덜 흐려 형태보다 조금 또렷하게 남긴다.
        bands = wiggle(rng, across * rng.uniform(1.5, 3.5) + 0.3 * wiggle(rng, u * 2, 2, (1, 3)), 3, (2, 5))
        streak = np.maximum(bands, 0) ** 1.5 * rng.uniform(0.1, 0.25)
        lit = (np.minimum(bright * 1.5, 1) - color) * streak[..., None]

        alpha = blur(shape, sigma)
        layer = blur(color * shape[..., None], sigma) + blur(lit * shape[..., None], sigma * 0.5)
        canvas = canvas * (1 - alpha[..., None]) + layer

    canvas = canvas[pad:pad + rows, pad:pad + cols]

    # 밝은 곳이 번지게 한다
    canvas = canvas + blur(np.maximum(canvas - 0.55, 0), 12) * 0.9
    # 하이라이트가 하얗게 날아가지 않도록 부드럽게 누른다
    canvas = canvas / (1 + 0.25 * np.maximum(canvas - 0.6, 0))
    canvas = zoom(canvas, (height / rows, width / cols, 1), order=3)[:height, :width]

    # 필름 같은 입자감: 2~3픽셀 크기로 뭉친 잡음과 고운 잡음을 겹친다. 큰 화면의 띠 무늬(밴딩)도 없앤다.
    clumps = zoom(rng.normal(0, 1, (height // 3 + 1, width // 3 + 1)), 3, order=3)[:height, :width]
    clumps = clumps / clumps.std() * 4.0
    pixels = srgb(canvas) * 255 + (clumps + rng.normal(0, 2.5, (height, width)))[..., None] + rng.normal(0, 1.0, (height, width, 3))
    return Image.fromarray(np.clip(pixels, 0, 255).astype(np.uint8))


def main() -> None:
    root = pathlib.Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--count", type=int, default=32, help="만들 이미지 수 (기본 32)")
    parser.add_argument("--seed", type=int, default=1, help="같은 시드면 같은 이미지가 나온다 (기본 1)")
    parser.add_argument("--width", type=int, default=WIDTH, help=f"이미지 폭 (기본 {WIDTH}, 높이는 16:9)")
    parser.add_argument("--out", type=pathlib.Path, default=root / "src/HnPaper.Web/wwwroot/img/gradients")
    args = parser.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    for i in range(args.count):
        rng = np.random.default_rng([args.seed, i])  # 장마다 시드를 따로 두어, 한 장만 다시 만들어도 나머지가 바뀌지 않는다
        path = args.out / f"bloom-{i + 1:02d}.webp"
        render(rng, PALETTES[i % len(PALETTES)], args.width).save(path, "WEBP", quality=80, method=6)
        print(path.relative_to(root) if path.is_relative_to(root) else path)


if __name__ == "__main__":
    main()
