# Jurassic Park

> Survive the nights, fix the boat, get off the island. Together.

[![CI](https://github.com/zyx1121/jurassic-park/actions/workflows/ci.yml/badge.svg)](https://github.com/zyx1121/jurassic-park/actions) &nbsp;[![Unity](https://img.shields.io/badge/Unity-6000.3%20LTS-111111)](https://unity.com/releases/lts) &nbsp;[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](#license)

The Warcraft III custom map "Jurassic Park" had a simple loop that never got old: gather by day, build by dusk, hide by night, and hope the raptors pick someone else. This project rebuilds that loop as a 1 to 4 player co-op game in the HD-2D look of Octopath Traveler: pixel-art survivors and dinosaurs inside a lit, tilt-shifted 3D island. Built for the NYCU 3D Game Programming course, Fall 2026.

<!-- Hero screenshot goes here once the vertical slice exists. -->

## Features

- **Survive a day-night cycle** where the sun, fog, spawn tables, and music all follow one clock
- **Build fences, campfires, and traps** that carve the NavMesh so dinosaurs have to route around them
- **Repair the boat** by recovering 5 engine parts from the park's ruined facilities before the T-Rex reaches the beach
- **Play co-op** as Ranger, Engineer, Medic, or Hunter over Unity Netcode, or run the whole thing alone

## Tech stack

| Layer | Choice |
|-------|--------|
| Engine | Unity 6000.3 LTS, Universal Render Pipeline |
| Language | C# |
| Navigation | Unity AI Navigation (NavMesh) |
| Networking | Netcode for GameObjects + Unity Relay |
| Input | Unity Input System |
| Targets | macOS (Apple Silicon), Windows |

## Getting started

```bash
git clone https://github.com/zyx1121/jurassic-park && cd jurassic-park
git lfs install && git lfs pull
unity open .        # Unity CLI, or open the folder in Unity Hub with 6000.3.24f1
```

Play from `Assets/Scenes/Island.unity`. Builds go to `Builds/` (ignored):

```bash
unity test . --mode EditMode
unity build . --target StandaloneOSX --output-path Builds/macOS/JurassicPark.app
```

## Project layout

```
Assets/
  Scenes/      Island, MainMenu, test scenes
  Scripts/     one folder per system: Core, Player, Dinosaurs, Building, Combat, UI, Net, FX
  Prefabs/     Player, Dinosaurs, Structures, Pickups, FX
  Sprites/     pixel art, point filtered
  Materials/ Textures/ Audio/ UI/
docs/          proposal, design notes, art spec
```

## Development

Work is tracked in [issues](https://github.com/zyx1121/jurassic-park/issues) grouped by [milestones](https://github.com/zyx1121/jurassic-park/milestones) that match the course dates. Branch from `main`, open a PR, let CI pass, squash-merge. Releases use SemVer tags and ship both platform builds.

## Contributing

Issues and PRs welcome: start with [CONTRIBUTING.md](https://github.com/zyx1121/.github/blob/main/CONTRIBUTING.md).

## License

[MIT](LICENSE) · Life, uh, finds a way.
