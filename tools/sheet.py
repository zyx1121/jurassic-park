#!/usr/bin/env python3
"""Pack rendered frames into a 4-direction sprite sheet with fixed framing and one shared palette.

usage: sheet.py <render_dir/<Anim>> <out.png> [--px 128] [--colors 16] [--dirs front,left,right,back]
Rows are directions in the given order (default Down, Left, Right, Up), columns are frames.
"""
import argparse
import glob
import os
import subprocess
import tempfile

ap = argparse.ArgumentParser()
ap.add_argument("src"); ap.add_argument("out")
ap.add_argument("--px", type=int, default=128)
ap.add_argument("--colors", type=int, default=16)
ap.add_argument("--dirs", default="front,left,right,back")
ap.add_argument("--contrast", default="3x45%")
ap.add_argument("--palette", default="", help="master palette image; when given, frames are graded and remapped to it instead of a per-sheet quantize")
ap.add_argument("--modulate", default="96,84,100", help="brightness,saturation,hue used with --palette")
a = ap.parse_args()

dirs = a.dirs.split(",")
frames = sorted(glob.glob(os.path.join(a.src, f"{dirs[0]}_*.png")))
n = len(frames)
assert n, f"no frames in {a.src}"
px = a.px
with tempfile.TemporaryDirectory() as tmp:
    # 1) downscale every frame without trimming so the character stays put between frames
    small = []
    for d in dirs:
        for k in range(n):
            src = os.path.join(a.src, f"{d}_{k:02d}.png")
            dst = os.path.join(tmp, f"{d}_{k:02d}.png")
            grade = ["-channel", "RGB", "-sigmoidal-contrast", a.contrast] + (["-modulate", a.modulate] if a.palette else []) + ["+channel"]
            subprocess.run(["magick", src, "-alpha", "on", "-filter", "Box", "-resize", f"{px}x{px}", *grade, dst], check=True)
            small.append(dst)
    # 2) one palette from all frames so colors do not flicker across frames or directions
    palette = os.path.join(tmp, "palette.png")
    if a.palette:
        palette = a.palette
    else:
        subprocess.run(["magick", *small, "+append", "-alpha", "off", "-dither", "None", "-colors", str(a.colors),
                        "-unique-colors", palette], check=True)
    # 3) remap each frame to the palette and pack rows (direction) x columns (frame)
    rows = []
    for d in dirs:
        row = os.path.join(tmp, f"row_{d}.png")
        cells = []
        for k in range(n):
            cell = os.path.join(tmp, f"{d}_{k:02d}.png")
            mapped = os.path.join(tmp, f"m_{d}_{k:02d}.png")
            # remap RGB only, then put the original alpha back and clear fully transparent pixels
            subprocess.run(["magick", cell, "-alpha", "off", "-dither", "None", "-remap", palette,
                            "(", cell, "-alpha", "extract", "-threshold", "50%", ")",
                            "-compose", "CopyOpacity", "-composite",
                            "-background", "none", "-alpha", "background", mapped], check=True)
            cells.append(mapped)
        subprocess.run(["magick", *cells, "+append", row], check=True)
        rows.append(row)
    subprocess.run(["magick", *rows, "-background", "none", "-append", "-define", "png:color-type=6", a.out], check=True)
info = subprocess.run(["magick", "identify", "-format", "%wx%h %k", a.out], capture_output=True, text=True).stdout
print(f"{a.out} {info} frames={n} rows={len(dirs)} cell={px}")
