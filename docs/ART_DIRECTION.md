# Jurassic Park art direction — “humid CD-ROM uncanny”

## North star

The world is built from believable material evidence—grit, bark fissures, leaf translucency, mineral stains, damp cloth and scaled skin—then deliberately damaged by a late-1990s production pipeline. High-detail photo/scan/model sources are rendered with a hard warm key and cold blue-purple fill, reduced to a shared 32-color world palette, and shown at a fixed 64 pixels per world metre. The result should feel like a modern engine reconstructing a half-remembered year-2000 PC/PlayStation game: tactile, darkly playful and a little wrong. It must never look like intentionally simple flat-color low-poly art.

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

### Current offline substitution

The Poly Haven pages and CC0 metadata were verified, but this runner could not resolve `api.polyhaven.com`/`dl.polyhaven.org`, and its in-app browser was unavailable. The actual source plates and prop reference renders in this package were therefore produced specifically for the repository and dedicated under CC0 1.0; editable OBJ/MTL models were generated locally. Each `SOURCES.md` names the exact production source and the verified Poly Haven swap target. `tools/build_real_models.py` is the preferred native-Blender source generator; `tools/build_cc0_obj_models.py` is its Blender-independent fallback.

The bundled Blender 5.2.1 build also crashes during Metal device detection before Python executes. `tools/render_sprites.py` was extended to accept `.blend` sources, but the supplied prop proofs use the saved high-detail `source_render.png` plates. Re-render the documented Poly Haven candidates on a normal Blender machine when binary download/render access is restored; do not replace these with Kenney assets.

## Outline rule

There is **no blanket outline pass**. A uniform black contour makes foliage and rocks read as stickers and erases the digitized/pre-rendered cue. Silhouettes rely on the natural alpha edge, baked cold rim light, and `#17141F` only where the source already contains deep occlusion. Accordingly, `tools/outline.py` is intentionally not created. If a character disappears at night, add a selective one-pixel shadow-side rim by hand or in its material—not a morphological outline around every asset.

## Lighting, camera and atmosphere

- Camera: perspective, pitch **38° down**, yaw **0°**, follow distance **22 m**, vertical FOV **35°**, near/far **0.1 / 120 m**. Preserve camera-relative billboarding; do not pitch billboards fully toward the lens—keep their vertical axis upright.
- Key light: rotation **(50°, -35°, 0°)**; shadows soft but definite, Strength 0.72. The key always comes from screen upper-left so baked prop shading and world shadows agree.
- Gaussian DoF: Start **24 m**, End **42 m**, Max Radius **0.55**, High Quality Sampling **On**. The player and nearby threats must remain pixel-sharp.
- Bloom: Threshold **1.15**, Intensity **0.18**, Scatter **0.55**, Clamp **6**, Tint `#FFD6A0`. Bloom is for fire, wet glints and electrics, not a full-screen haze.
- Vignette: `#17141F`, Intensity **0.23**, Smoothness **0.42**, Rounded **Off**.
- Color Adjustments: Post Exposure **-0.15**, Contrast **+22**, Saturation **-18**, Color Filter `#E2D4C2`.

| State | Directional color / intensity | Ambient color | Linear fog color | Fog start / end |
|---|---:|---:|---:|---:|
| Day | `#FFE0A8` / 1.25 | `#46565D` | `#637278` | 28 / 65 m |
| Dusk | `#FF9E67` / 0.85 | `#302A48` | `#373149` | 22 / 55 m |
| Night | `#A8B7FF` / 0.35 | `#171A31` | `#16192B` | 16 / 44 m |

## References and what is borrowed

- [Octopath Traveler / Square Enix](https://www.jp.square-enix.com/octopathtraveler/about/): perspective stagecraft, selective depth of field, modern light on visibly pixel-built subjects. We borrow the compositing logic, not its bright storybook palette.
- [Donkey Kong Country](https://en.wikipedia.org/wiki/Donkey_Kong_Country): high-detail 3D forms pre-rendered and compressed into sprite-scale color information. We borrow the model → render → constrained sprite workflow.
- [Mortal Kombat (1992)](https://en.wikipedia.org/wiki/Mortal_Kombat_%281992_video_game%29): real subjects reduced to assertive digitized silhouettes. We borrow the uneasy photographic read and hard edge, not fighting-game saturation.
- [Resident Evil history / Capcom](https://game.capcom.com/residentevil/uk/news_topics-201603281111.html): survival-horror staging, pools of visibility and detailed pre-rendered-feeling environments. We borrow the uncertainty beyond the focal plane.
- [Crow Country](https://blog.playstation.com/2024/05/02/crow-country-retro-original-playstation-era-gameplay-stylings-meet-modern-horror/): a modern, deliberate revisit of original-PlayStation visual rules. We borrow restraint and readable low-resolution shape language, not its deliberately simple geometry.

## Do / do not

Do preserve bark cracks, individual frond groupings, chipped rock planes, mud shine and cloth seams through the downsample. Do use asymmetry, damage, wet/dry variation and cold crevice color. Do judge assets at native size and in a 640 × 360 scene, not only enlarged.

Do not use Kenney Nature Kit art, flat material recolors, smooth gradients inside final sprites, black universal outlines, neon grass, pastel beaches, uniform random tints, per-prop light directions, or 1 m single-pattern grass tiles. Do not allow bloom/DoF/texture filtering to melt the pixel grid. Do not call a low-poly mesh “retro” and treat that as the target.
