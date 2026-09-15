---
name: jurassic-art-direction
description: Use when designing, importing, placing, or reviewing art for Jurassic Park: pixelated realistic assets, oppressive jungle density, modular facilities, shoreline rendering, and player visibility.
---

# Jurassic Park art direction

Read `docs/ART_DIRECTION.md`, `CREDITS.md`, and `CLAUDE.md` before changing art.
Use the official Unity plugin's rendering and unity-cli skills for implementation.

## Visual contract

- Pixelate realistic material detail and silhouettes. Aim for an uncanny
  year-2000 game made with modern lighting, not cute low-poly art.
- Keep the shared palette, point sampling, uncompressed textures and no mipmaps.
  Source density is 64 PPU; authored facility framing cells have explicit world
  dimensions rather than an assumed one-pixel-to-world scale.
- Keep tall varied trees, separate boulder/rock/stone tiers and layered clutter.
  Boulders are scenery; small resource stones remain gatherable.
- Preserve the approved bloom and vignette. Maintain cold shadows, persistent
  fog and warm firelight without making the playable area unreadable.
- Keep the survivor visible with the existing soft player-centered see-through
  mask. Do not instantly hide whole trees or facility pieces.

## Placement and integration

- Change builders and ScriptableObject defaults, then regenerate through the
  official Unity CLI. Never hand-edit generated scene, prefab or asset YAML.
- Facility sprites need separate collider proxies for doors, legs and service
  lanes. A large sprite does not imply one large solid box.
- Boat parts must sit on reachable ground outside solid proxies. Keep all five
  routes reachable from the dry beach spawn.
- Keep the dock deck level and walkable; ground other facility pieces at their
  own world positions. Orient the dock toward the sea.
- Keep animated water point-filtered and repeating, with a stepped depth ramp
  and shoreline foam. Enable the camera depth texture.
- Build the facility library and sea material before regenerating scenes.
  Persist generated TerrainData and NavMeshData through the existing LFS-backed
  asset helpers.

## Delivery

Inspect the settled beach spawn, camp, facility routes and shoreline in play,
not just asset contact sheets or an immediate pre-camera-settle frame. Check
day and night water, occlusion transitions, and the actual macOS player.
Run the existing relevant tests and build commands; retain existing user work.
Attribute asset provenance accurately: current v2 art is project-generated,
not Poly Haven photogrammetry. Do not add a new asset without license evidence.
