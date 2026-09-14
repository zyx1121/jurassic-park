# /// script
# requires-python = ">=3.11"
# dependencies = ["numpy", "pillow"]
# ///
"""Tileable pixel-art ground texture: periodic value-noise fBm, color ramp, quantized.

usage: uv run tools/gen_tile.py <out.png> <dark hex> <light hex> [--size 64] [--colors 7] [--scale 6] [--seed 1] [--speckle 0.0]
"""
import argparse
import numpy as np
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument("out"); ap.add_argument("dark"); ap.add_argument("light")
ap.add_argument("--size", type=int, default=64)
ap.add_argument("--colors", type=int, default=7)
ap.add_argument("--scale", type=int, default=6, help="lattice cells across the tile at octave 0")
ap.add_argument("--seed", type=int, default=1)
ap.add_argument("--speckle", type=float, default=0.0, help="fraction of pixels bumped one shade")
a = ap.parse_args()

rng = np.random.default_rng(a.seed)
n = a.size

def value_noise(cells):
    lattice = rng.random((cells, cells))
    ys, xs = np.mgrid[0:n, 0:n] / n * cells
    x0 = np.floor(xs).astype(int); y0 = np.floor(ys).astype(int)
    fx = xs - x0; fy = ys - y0
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
    x1 = (x0 + 1) % cells; y1 = (y0 + 1) % cells
    x0 %= cells; y0 %= cells
    v = (lattice[y0, x0] * (1 - fx) + lattice[y0, x1] * fx) * (1 - fy) + (lattice[y1, x0] * (1 - fx) + lattice[y1, x1] * fx) * fy
    return v

h = np.zeros((n, n)); amp = 1.0; total = 0.0; cells = a.scale
for _ in range(4):
    h += value_noise(cells) * amp; total += amp; amp *= 0.5; cells *= 2
h = (h / total); h = (h - h.min()) / (h.max() - h.min() + 1e-9)
if a.speckle > 0:
    mask = rng.random((n, n)) < a.speckle
    h[mask] = np.clip(h[mask] + rng.choice([-0.25, 0.25], size=mask.sum()), 0, 1)

def hex2rgb(s): s = s.lstrip("#"); return np.array([int(s[i:i+2], 16) for i in (0, 2, 4)], dtype=float)
d, l = hex2rgb(a.dark), hex2rgb(a.light)
levels = np.round(h * (a.colors - 1)) / (a.colors - 1)
rgb = (d[None, None, :] * (1 - levels[..., None]) + l[None, None, :] * levels[..., None]).astype(np.uint8)
Image.fromarray(rgb, "RGB").save(a.out)
print(a.out, n, "x", n, len(np.unique(rgb.reshape(-1, 3), axis=0)), "colors")
