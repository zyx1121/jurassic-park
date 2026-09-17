# Jurassic Park art direction

## Active direction: readable RTS jungle (2026-09-17)

Decided with the player on 2026-09-17. This is the only active direction; earlier directions live on the `legacy` branch.

**Keep the pipeline, change the target.** Every sprite and terrain plate still
comes from a real photo, scan or realistic textured model, rendered in Blender,
reduced to 64 px/m and mapped to the shared master palette. The visual target is
no longer a cinematic diorama; it is a Warcraft III style camp-defense map that
stays beautiful at a glance and legible at a distance.

| Decision | Value |
|---|---|
| Camera pitch | **60 degrees**, fixed yaw, pan and zoom only. **Orthographic** (decided from the M1 comparison captures, 2026-09-17; one checkbox on `RtsCamera` switches back): the fixed pixels-per-metre grid below only exists without perspective, and at this pitch a perspective lens leans upright objects 34 to 48 degrees at the screen edge. Measured on the M1 scene (2026-09-17): vertical FOV 25 at 75 m default distance, zoom 32 to 140 m. FOV 40 at this pitch made upright objects near the screen edge lean about 45 degrees |
| Depth of field | Off in gameplay. Only the fixed 640 x 360 pixel grid and palette carry the retro read |
| Palette | **Full green jungle** first. Keep the 26/32-color ramp structure but re-weight toward the humid-vegetation rows; sand and bone tones are reserved for paths, clearings and bones so approaches read as light ground on dark foliage. Evaluate on the first M1 map before producing assets in bulk |
| Buildings | Same digitized pipeline as props: render realistic tents, fences, electric walls and turrets in Blender, then reduce. No hand-drawn pixel buildings and no flat-color blockers; flat-color blockers are acceptable only as M1 and M2 placeholders |
| Readability | Walls, doors, resource nodes and dinosaurs must be identifiable from the 60-degree view without hover. Selection rings and health bars are UI, not baked into sprites |

References, trimmed to what we actually borrow:

- [Donkey Kong Country](https://en.wikipedia.org/wiki/Donkey_Kong_Country): model, render, then compress into sprite-scale color. This is the pipeline.
- [Octopath Traveler](https://www.jp.square-enix.com/octopathtraveler/about/): modern lighting on visibly pixel-built subjects. We borrow the light, not the depth of field or the storybook palette.
- [Crow Country](https://blog.playstation.com/2024/05/02/crow-country-retro-original-playstation-era-gameplay-stylings-meet-modern-horror/): restraint and readable low-resolution shape language.
- Warcraft III Jurassic Park 6.3: layout reference only, for how a defended camp, its approaches and its console read on screen. Nothing is imported from the map.

Mortal Kombat and Resident Evil are dropped as references; their hard digitized
edge and uncertainty beyond the focal plane pull against RTS readability.

Rules carried over unchanged: no blanket outline pass, no Kenney or flat low-poly
art, no texture filtering, mipmaps, TAA or MSAA, key light always from screen
upper-left, shadows shift toward blue-purple never black.

## Carried-over production rules

The sections below come from the 2026-09-14 diorama specification and still
apply. Its camera, depth of field, sand-and-bone grade, facility kits and sea
pass are superseded; they remain readable on the `legacy` branch. The master
palette keeps its ramp structure but is being re-weighted toward green; treat the
hex values as the starting point, not the final jungle grade.

## Scale and pixel grammar

- World density: **64 source pixels per Unity unit/metre** for every sprite and terrain material.
- Terrain: **128 × 128 px** represents **2 × 2 m**. The 128 px choice retains recognizable leaf, stone and grit structure while preserving the required 64 px/m; it also halves the visible repetition frequency of the old 1 m tiles.
- Character cell: **128 × 128 px**. A 1.75 m adult occupies about **112 vertical pixels**, leaving 8 px of headroom and 8 px for the grounded foot/contact region.
- Prop cells: tree/palm **192 px**, rock/log/stump **128 px**, grass/fern/bush **96 px**.
- Intended visible-height ratio in world: broadleaf tree : adult : common rock = **2.45 : 1 : 0.52**. Palm : adult = **2.7 : 1**. These are achieved with the Unity scale multipliers in `out/REPORT.md`; cell size is not physical size.
- Screen pixel grid: compose the 3D scene at **640 × 360**, then point-scale to the display. At 1920 × 1080 each composition pixel becomes exactly 3 × 3 display pixels.
- Edges are hard pixel steps. No bilinear texture filtering, sprite smoothing, texture compression, mipmaps, TAA or MSAA.

## Master palette (32 colors)

The rows are intentional ramps: blue-purple shadow, humid vegetation, sand/bone light, bark/rust, and neutral stone.

```text
#17141F #242136 #302A48 #413958
#17323A #21474A #2D5A50 #3C6B55
#506F4D #697E50 #84945B #A4AA6A
#C4BD7A #D8C58B #E6D2A1 #F0DFC0
#3A2928 #50352D #684431 #81583A
#9D7049 #B98D60 #CEA978 #DFC397
#34383F #484D52 #5E6262 #77786E
#929081 #AAA491 #C2B8A1 #D6CAB4
```

Shadows shift toward `#242136` / `#17323A`, never toward plain black. Warm key-facing bark, skin and sand may climb the ochre/bone ramps; vegetation highlights stop at `#A4AA6A` except for rare wet glints. `#17141F` is the deepest occlusion and UI-safe background, not a universal outline color.

## Source and downsample pipeline

1. Start with a genuine photo plate or realistic textured model. Preferred external source is [Poly Haven](https://polyhaven.com/) because its asset library is [CC0](https://polyhaven.com/license). Flat-color model kits are rejected even if their license is permissive.
2. Texture plates: crop square; cyclically offset variants; harmonize 12% opposing edge bands in the high-resolution plate; downsample once with Lanczos to 128 px; grade at contrast 1.16–1.30, saturation 0.58–0.92, cool-shadow shift 0.30–0.50; map in OKLab to the 32-color master palette; lock final opposing pixels; emit a 4 × 4 preview. `tools/pixelize_texture.py` implements this deterministically.
3. Models: orthographic three-quarter render, camera elevation 30°, transparent film, 512 px source, warm upper-left sun/key, cold weak fill, ambient 0.18–0.25, exposure around -0.7 EV. Render four directions for production props.
4. Props/characters: alpha-trim, Box downsample to their fixed cell, sigmoidal contrast `3.8 × 44%`, saturation 84%, no dither, nearest remap to `tools/master_palette.ppm`, alpha threshold 50%, bottom-center pad. `tools/pixelize_prop.sh` implements this.
5. Import at 64 PPU, point/no-filter, no compression and no mipmaps. Let URP lighting/post affect the scene composition, then downsample the whole camera through the 640 × 360 render texture.

## Outline rule

There is **no blanket outline pass**. A uniform black contour makes foliage and rocks read as stickers and erases the digitized/pre-rendered cue. Silhouettes rely on the natural alpha edge, baked cold rim light, and `#17141F` only where the source already contains deep occlusion. Accordingly, `tools/outline.py` is intentionally not created. If a character disappears at night, add a selective one-pixel shadow-side rim by hand or in its material—not a morphological outline around every asset.

## Do / do not

Do preserve bark cracks, individual frond groupings, chipped rock planes, mud shine and cloth seams through the downsample. Do use asymmetry, damage, wet/dry variation and cold crevice color. Do judge assets at native size and in a 640 × 360 scene, not only enlarged.

Do not use Kenney Nature Kit art, flat material recolors, smooth gradients inside final sprites, black universal outlines, neon grass, pastel beaches, uniform random tints, per-prop light directions, or 1 m single-pattern grass tiles. Do not allow bloom/DoF/texture filtering to melt the pixel grid. Do not call a low-poly mesh “retro” and treat that as the target.

