#!/usr/bin/env zsh
# Single-frame prop sprite: fixed-size cell, bottom aligned, RGB quantized with alpha kept.
# usage: pixelize_prop.sh <in.png> <out.png> <cell_px> [colors=12]
# Note: quantize and composite must be separate magick calls; doing both in one command
# blacks out the palette image (ImageMagick 7.1.2 Q16-HDRI).
set -euo pipefail
in=$1; out=$2; px=$3; colors=${4:-12}
tmp=$(mktemp -d)
magick "$in" -alpha on -trim +repage -filter Box -resize "${px}x${px}" -sigmoidal-contrast 3x45% "$tmp/s.png"
magick "$tmp/s.png" -alpha extract -threshold 50% "$tmp/a.png"
magick "$tmp/s.png" -alpha off -dither None -colors "$colors" "$tmp/q.png"
magick "$tmp/q.png" "$tmp/a.png" -compose CopyOpacity -composite "$tmp/c.png"
magick "$tmp/c.png" -background none -gravity south -extent "${px}x${px}" "$out"
rm -rf "$tmp"
echo "$out $(magick identify -format '%wx%h %k colors' "$out")"
