# /// script
# requires-python = ">=3.11"
# dependencies = ["numpy", "pillow"]
# ///
"""Convert a photographic surface plate into a seamless, palette-locked tile.

The output is deliberately small and must be imported with point filtering.
Edges are harmonized in the high-resolution source before resampling, so the
first/last pixel pairs agree without painting a blurry seam through the tile.

usage:
  uv run tools/pixelize_texture.py INPUT OUTPUT [options]

Example:
  uv run tools/pixelize_texture.py textures/real/grass/source.png \
      out/terrain/grass.png --size 64 --preview out/terrain/preview_grass.png
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path

import numpy as np
from PIL import Image


# Shared world palette. Transparent sprites use #17141F for the optional rim;
# terrain uses all 32 opaque entries. Keep this synchronized with
# ART_DIRECTION.md.
MASTER_PALETTE = [
    "#17141F", "#242136", "#302A48", "#413958",
    "#17323A", "#21474A", "#2D5A50", "#3C6B55",
    "#506F4D", "#697E50", "#84945B", "#A4AA6A",
    "#C4BD7A", "#D8C58B", "#E6D2A1", "#F0DFC0",
    "#3A2928", "#50352D", "#684431", "#81583A",
    "#9D7049", "#B98D60", "#CEA978", "#DFC397",
    "#34383F", "#484D52", "#5E6262", "#77786E",
    "#929081", "#AAA491", "#C2B8A1", "#D6CAB4",
]


def _hex_rgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def _linear(rgb: np.ndarray) -> np.ndarray:
    rgb = rgb / 255.0
    return np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)


def _oklab(rgb: np.ndarray) -> np.ndarray:
    """sRGB uint/float 0..255 to perceptual OKLab."""
    c = _linear(rgb.astype(np.float32))
    l = 0.4122214708 * c[..., 0] + 0.5363325363 * c[..., 1] + 0.0514459929 * c[..., 2]
    m = 0.2119034982 * c[..., 0] + 0.6806995451 * c[..., 1] + 0.1073969566 * c[..., 2]
    s = 0.0883024619 * c[..., 0] + 0.2817188376 * c[..., 1] + 0.6299787005 * c[..., 2]
    l, m, s = np.cbrt(l), np.cbrt(m), np.cbrt(s)
    return np.stack((
        0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
        1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
        0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
    ), axis=-1)


def harmonize_edges(rgb: np.ndarray, fraction: float) -> np.ndarray:
    """Make opposing edge bands converge while preserving the center detail."""
    out = rgb.astype(np.float32).copy()
    h, w = out.shape[:2]
    bx = max(2, min(w // 3, int(round(w * fraction))))
    by = max(2, min(h // 3, int(round(h * fraction))))

    # Pair pixels equidistant from opposing edges. At the outermost pair both
    # become their mean; the correction falls away with a cosine taper.
    for x in range(bx):
        mate = w - 1 - x
        strength = 0.5 * (1.0 + math.cos(math.pi * x / bx))
        mean = (out[:, x] + out[:, mate]) * 0.5
        out[:, x] = out[:, x] * (1.0 - strength) + mean * strength
        out[:, mate] = out[:, mate] * (1.0 - strength) + mean * strength
    for y in range(by):
        mate = h - 1 - y
        strength = 0.5 * (1.0 + math.cos(math.pi * y / by))
        mean = (out[y] + out[mate]) * 0.5
        out[y] = out[y] * (1.0 - strength) + mean * strength
        out[mate] = out[mate] * (1.0 - strength) + mean * strength

    # Guarantee exact wrap pairs after both passes.
    lr = (out[:, 0] + out[:, -1]) * 0.5
    out[:, 0] = out[:, -1] = lr
    tb = (out[0] + out[-1]) * 0.5
    out[0] = out[-1] = tb
    return np.clip(out, 0, 255).astype(np.uint8)


def color_grade(image: Image.Image, exposure: float, contrast: float,
                saturation: float, shadow_shift: float) -> Image.Image:
    arr = np.asarray(image.convert("RGB"), dtype=np.float32) / 255.0
    arr *= 2.0 ** exposure
    # Contrast around a photographic middle gray, then saturation around luma.
    arr = (arr - 0.42) * contrast + 0.42
    lum = arr[..., 0] * 0.2126 + arr[..., 1] * 0.7152 + arr[..., 2] * 0.0722
    arr = lum[..., None] + (arr - lum[..., None]) * saturation
    # Uncanny cool shadows; highlights remain materially truthful.
    mask = np.clip((0.52 - lum) / 0.52, 0.0, 1.0)[..., None] * shadow_shift
    shadow_color = np.array([0.13, 0.12, 0.24], dtype=np.float32)
    arr = arr * (1.0 - mask * 0.34) + shadow_color * (mask * 0.34)
    return Image.fromarray(np.clip(arr * 255.0, 0, 255).astype(np.uint8), "RGB")


def quantize_master(image: Image.Image) -> Image.Image:
    arr = np.asarray(image.convert("RGB"), dtype=np.uint8)
    pal = np.asarray([_hex_rgb(x) for x in MASTER_PALETTE], dtype=np.uint8)
    px_lab = _oklab(arr).reshape(-1, 3)
    pal_lab = _oklab(pal).reshape(-1, 3)
    # Chunk to avoid a large temporary if the requested tile size is raised.
    result = np.empty(len(px_lab), dtype=np.uint8)
    for start in range(0, len(px_lab), 16384):
        block = px_lab[start:start + 16384]
        dist = np.sum((block[:, None, :] - pal_lab[None, :, :]) ** 2, axis=2)
        result[start:start + len(block)] = np.argmin(dist, axis=1)
    return Image.fromarray(pal[result].reshape(arr.shape), "RGB")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("input", type=Path)
    ap.add_argument("output", type=Path)
    ap.add_argument("--size", type=int, default=64, choices=(32, 64, 128, 256))
    ap.add_argument("--preview", type=Path, help="write a 4x4 nearest-neighbor tiled preview")
    ap.add_argument("--edge-blend", type=float, default=0.12,
                    help="fraction of source width/height harmonized at each edge")
    ap.add_argument("--offset-x", type=float, default=0.0,
                    help="cyclic source offset as fraction of width")
    ap.add_argument("--offset-y", type=float, default=0.0,
                    help="cyclic source offset as fraction of height")
    ap.add_argument("--exposure", type=float, default=-0.15)
    ap.add_argument("--contrast", type=float, default=1.16)
    ap.add_argument("--saturation", type=float, default=0.82)
    ap.add_argument("--shadow-shift", type=float, default=0.38)
    args = ap.parse_args()

    image = Image.open(args.input).convert("RGB")
    side = min(image.size)
    left = (image.width - side) // 2
    top = (image.height - side) // 2
    image = image.crop((left, top, left + side, top + side))
    arr = np.asarray(image)
    arr = np.roll(arr, (round(side * args.offset_y), round(side * args.offset_x)), axis=(0, 1))
    arr = harmonize_edges(arr, args.edge_blend)
    image = Image.fromarray(arr, "RGB").resize((args.size, args.size), Image.Resampling.LANCZOS)
    image = color_grade(image, args.exposure, args.contrast, args.saturation, args.shadow_shift)
    image = quantize_master(image)
    # Lanczos can sample just beyond the harmonized border and the palette snap
    # can amplify that tiny difference. Lock the final opposing pixel pairs.
    final = np.asarray(image).copy()
    final[:, -1] = final[:, 0]
    final[-1] = final[0]
    image = Image.fromarray(final, "RGB")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    image.save(args.output, optimize=True)
    if args.preview:
        args.preview.parent.mkdir(parents=True, exist_ok=True)
        tiled = Image.new("RGB", (args.size * 4, args.size * 4))
        for y in range(4):
            for x in range(4):
                tiled.paste(image, (x * args.size, y * args.size))
        tiled.save(args.preview, optimize=True)

    rgb = np.asarray(image)
    edge_error = max(
        int(np.abs(rgb[:, 0].astype(int) - rgb[:, -1].astype(int)).max()),
        int(np.abs(rgb[0].astype(int) - rgb[-1].astype(int)).max()),
    )
    print(f"{args.output}: {args.size}x{args.size}, "
          f"{len(np.unique(rgb.reshape(-1, 3), axis=0))} palette colors, "
          f"wrap edge max delta {edge_error}")


if __name__ == "__main__":
    main()
