# Jurassic Park

> Pick a camp, wall it off, keep the supply line alive, and get everyone to the helicopter in 30 minutes.

[![CI](https://github.com/zyx1121/jurassic-park/actions/workflows/ci.yml/badge.svg)](https://github.com/zyx1121/jurassic-park/actions) &nbsp;[![Unity](https://img.shields.io/badge/Unity-6000.3%20LTS-111111)](https://unity.com/releases/lts) &nbsp;[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](#license)

The Warcraft III custom map "Jurassic Park" turned base defense into a co-op survival story: choose one of many camps, seal the entrance, grow an economy under pressure, then abandon it all for the evacuation. This project rebuilds that game as a standalone RTS in Unity, with every rule taken from the original 6.3 map, physical logistics added on top, and computer allies that can fill any empty seat. Built for the NYCU 3D Game Programming course, Fall 2026.

<!-- Hero capture goes here once M2 is playable. -->

> [!NOTE]
> The repository was reset on 2026-09-17. `main` restarts from the v1 design in
> [docs/PLAN.md](docs/PLAN.md); there is no game code yet. The earlier WASD survival
> prototype lives on the [`legacy`](https://github.com/zyx1121/jurassic-park/tree/legacy) branch as reference.

## Features

- **Command survivors and support units** with RTS selection, right-click orders and a fixed-orientation camera
- **Haul every resource by hand** from source to depot to building site; nothing is paid from a shared counter
- **Seal your camp and watch it get breached**: dinosaurs route around walls, or break the one that actually opens a path
- **Play with friends or computer allies**: each seat is a human or an AI, and an AI takes over when someone drops

## Tech stack

| Layer | Choice |
|-------|--------|
| Engine | Unity 6000.3 LTS, Universal Render Pipeline |
| Language | C# |
| Simulation | Pure C# core, host-authoritative, grid A* pathfinding |
| Networking | Netcode for GameObjects: commands in, snapshots out |
| Art | Digitized sprites: real source, Blender render, 64 px/m, shared jungle palette |
| Targets | macOS (Apple Silicon), Windows |

## Getting started

```bash
git clone https://github.com/zyx1121/jurassic-park && cd jurassic-park
git lfs install && git lfs pull
unity open .        # Unity CLI, or open the folder in Unity Hub with 6000.3.24f1
tools/test.sh       # EditMode tests; exits non-zero unless a fresh report shows zero failures
```

Code is split into five assemblies: `Simulation` (pure C#, no engine references), `Presentation`, `Net`, `Editor` and `Tests.EditMode`. Gameplay rules live only in `Simulation`.

## Documentation

| Document | Covers |
|----------|--------|
| [docs/PLAN.md](docs/PLAN.md) | v1 design: decisions, command pipeline, logistics, map, match flow, milestones |
| [docs/ART_DIRECTION.md](docs/ART_DIRECTION.md) | Readable RTS jungle: 60 degree camera, green palette, digitized pipeline |
| [docs/PROPOSAL.md](docs/PROPOSAL.md) | Course context and dates; gameplay sections are superseded by the plan |

## Roadmap

| Milestone | Due | Scope |
|-----------|-----|-------|
| M0 Bootstrap and proposal | 10/18 | Unity project, test and CI skeleton, original content inventory, proposal slides |
| M1 Command and logistics core | 11/08 | Simulation core, fixed map, seats and command routing, hauling loop, two-seat networking |
| M2 Online demo 1 | 11/15 | Construction, dynamic blocking, one dinosaur with breach behavior, RTS HUD |
| M3 Fog, allies and match | 11/22 | Per-seat knowledge and fog, computer ally loop, 30 minute match and evacuation |
| M4 Original content | 12/06 | Buildings, tech, units, items and dinosaurs from the 6.3 map; 12-camp map |
| M5 Online demo 2 | 12/13 | Green jungle digitized art, final GUI |
| M6 Final | 12/27 | Score persistence, audio and FX, balance, release builds, report |

## License

MIT. No Warcraft III models, textures, audio, UI or map scripts are imported; only rules, system relationships and numbers are studied.

Clever girl.
