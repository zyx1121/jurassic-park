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

### Demo controls and feedback

Move with **WASD**, sprint with **Shift**, and interact with **E**. Point at an
object to inspect it; **left click** keeps it selected, **right click** or **Esc**
clears the selection. Movement stays keyboard-controlled, not click-to-move.
The cursor turns green for a usable target and amber when it cannot currently
be used. Selection brackets and the target panel show its name, stock or health,
distance, and the actual interaction requirement. E uses that target; without a
pointed-at or selected object, it uses the nearest reachable usable object.

**Tab** enters building mode and cycles structures, **Q** rotates, **left click**
places, and **Esc** cancels. The HUD shows cost and placement status. Clicking
HUD panels never attacks or places structures behind them. **M** expands the
map; the expanded map blocks world controls until closed.

The HUD displays actual health, day/time, wood, stone, food, and carried boat
parts. Food is an inventory count, not hunger. Boat parts show carrying capacity,
not repaired-boat progress; hunger and the boat-repair UI remain future work.
HUD colors/text sizing and cursor settings live in `Assets/Data/Hud.asset` and
`Assets/Data/Selection.asset`. Their builders (`build_hud_assets`,
`build_selection_assets`) are also called by both scene builders.

## Project layout

```
Assets/
  Scenes/      Island (main scene), later MainMenu and test scenes
  Scripts/     one folder per system: Core, Player, Dinosaurs, Building, Combat, UI, Net, FX
  Prefabs/     Player, Dinosaurs, Structures, Pickups, FX
  Sprites/     pixel art; the import postprocessor forces point filter, no compression, no mipmaps, 32 PPU
  Textures/    pixel textures for 3D props, same import rules, repeat wrap
  Data/        ScriptableObjects holding every tunable number
  Settings/    URP asset (PC_RPAsset), renderer, volume profiles
  Editor/      editor-only tooling (import postprocessors, CLI commands)
  Materials/ Audio/ UI/
docs/          proposal, design notes, art spec
```

## Development

Work is tracked in [issues](https://github.com/zyx1121/jurassic-park/issues) grouped by [milestones](https://github.com/zyx1121/jurassic-park/milestones) that match the course dates. Branch from `main`, open a PR, let the hygiene CI pass, squash-merge. Releases use SemVer tags with macOS and Windows builds attached.

## Contributing

Issues and PRs welcome: start with [CONTRIBUTING.md](https://github.com/zyx1121/.github/blob/main/CONTRIBUTING.md).

## License

[MIT](LICENSE) · Life, uh, finds a way.
