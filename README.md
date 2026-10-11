<div align="center">

<h1>Jurassic Park</h1>

<p><strong>Pick a camp, wall it off, keep the supply line alive, and get everyone to the helicopter in 30 minutes.</strong></p>

[![CI](https://img.shields.io/github/actions/workflow/status/zyx1121/jurassic-park/ci.yml?branch=main&style=flat-square&label=CI)](https://github.com/zyx1121/jurassic-park/actions)
[![Unity](https://img.shields.io/badge/Unity-6000.3%20LTS-000000?style=flat-square&logo=unity)](ProjectSettings/ProjectVersion.txt)
[![License](https://img.shields.io/github/license/zyx1121/jurassic-park?style=flat-square)](LICENSE)

**English** | [繁體中文](README.zh-TW.md) | [简体中文](README.zh-CN.md)

</div>

![M1 slice: a survivor leaves the camp entrance for the grove](docs/screenshots/m1-orthographic-2026-09-17.png)

The Warcraft III custom map "Jurassic Park" made base defense a co-op survival story. You pick one of many camps, seal the entrance and grow an economy under pressure. Then you leave it all for the evacuation. This project rebuilds that game as a standalone RTS in Unity, for the NYCU 3D Game Programming course, Fall 2026. The rules come from the original 6.3 map, with physical logistics on top.

## Features

- **RTS control**: select survivors and support units, and give orders with a right click. The camera has a fixed angle.
- **Haul everything**: each resource moves by hand from source to depot to building site. No shared counter pays for anything.
- **Walls that matter**: dinosaurs go around walls, or break the one wall that opens a path.
- **Friends or computer allies**: each seat is a human or an AI. An AI takes over when a player drops.

## Quick start

```sh
git clone https://github.com/zyx1121/jurassic-park && cd jurassic-park
git lfs install && git lfs pull
unity open .        # or open the folder in Unity Hub with 6000.3.24f1
```

Open `Assets/Scenes/M1.unity` and press Play. Drag to select survivors, then right-click the ground, a tree or a depot.

## Usage

| Input | Does |
|-------|------|
| Left click, drag | Select |
| Right click | Order: walk, gather from a tree, drop at a depot |
| Shift | Queue the order |
| X | Stop |
| WASD, screen edge | Pan |
| Wheel | Zoom |

**Multiplayer**: on Play, the lobby offers **Play alone**, **Host** and **Join**. A build also takes `--host [port]`, `--join address[:port]` or `--offline`. With `-batchmode -nographics` it runs headless and logs a `[Status]` line every 5 seconds. A player who joins takes the first free seat, and the computer takes it back when the player leaves.

## Configuration

| Needs | For |
|-------|-----|
| Unity 6000.3.24f1 | the project |
| Git LFS | sprites and other binary assets |
| Unity CLI (`unity`) | tests, scene builders and builds |

Gameplay numbers live in ScriptableObjects under `Assets/Data/`. Builder commands generate the scenes: `unity run . -- -executeMethod JurassicPark.Editor.BuildM1Scene.Run`.

## How it works

- 5 assemblies: `Simulation` (pure C#, no engine references), `Presentation`, `Net`, `Editor` and `Tests.EditMode`. Gameplay rules live only in `Simulation`.
- The host runs the only simulation. A client sends commands and draws snapshots, through Netcode for GameObjects named messages.
- Each input, from a player, the GUI or an AI seat, goes through Command, Task, Plan and Action.
- Art uses digitized sprites: a real source, a Blender render, 64 px per meter and one jungle palette.

| Document | Covers |
|----------|--------|
| [docs/PLAN.md](docs/PLAN.md) | The v1 design: decisions, command pipeline, logistics, map, match flow, milestones |
| [docs/ART_DIRECTION.md](docs/ART_DIRECTION.md) | The 60 degree camera, the green palette and the sprite pipeline |
| [docs/PROPOSAL.md](docs/PROPOSAL.md) | Course context and dates |

| Milestone | Due | Scope |
|-----------|-----|-------|
| M0 Bootstrap and proposal | 10/18 | Project, tests, CI, content inventory, proposal |
| M1 Command and logistics core (done) | 11/08 | Simulation core, seats, command routing, hauling, 2-seat networking |
| M2 MVP | 11/15 | A full match: building, dinosaurs, breach, fog, AI ally, 12-camp map, art pass, GUI |
| M3 Fine-tune and final | 12/27 | Balance, scores, audio, release builds, report |

## Limits

- No Warcraft III models, textures, audio, UI or map scripts are in the repo. The project studies only rules, system links and numbers.
- Targets: macOS on Apple Silicon and Windows. The performance target is 60 fps on a MacBook Air M2.

## For AI agents

Facts for coding agents and LLM tools. The rules for changing the code are in [AGENTS.md](AGENTS.md).

- **Summary**: Unity 6 URP co-op RTS that rebuilds the Warcraft III "Jurassic Park" 6.3 map. Pure C# host-authoritative simulation, Netcode for GameObjects messages.
- **Type**: app
- **Interfaces**: the game. Build flags `--host [port]`, `--join address[:port]`, `--offline`, `-batchmode -nographics`.
- **Install**: `git clone https://github.com/zyx1121/jurassic-park && cd jurassic-park && git lfs pull`
- **Run**: `unity open .`, then play `Assets/Scenes/M1.unity`
- **Verify**: `tools/test.sh` (EditMode) and `tools/test.sh PlayMode`. Each needs zero failures in a new report.
- **Config**: gameplay numbers in `Assets/Data/*.asset`
- **Secrets**: none
- **Limits**: Unity 6000.3.24f1. CI looks only at repo hygiene, not the game.

## Development

```sh
tools/test.sh             # EditMode tests, non-zero exit unless a new report shows 0 failures
tools/test.sh PlayMode    # scene wiring
```

CI looks at the repo: no generated folders, a `.meta` for each asset, LFS pointers for binaries, and the decoded original data against their generators.

## License

[MIT](LICENSE)

Clever girl.
