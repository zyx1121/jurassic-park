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

Move with **WASD** and sprint with **Shift**. **Left click** a nearby resource or
usable object to act directly; **hold left click** to continue gathering. **E**
is the keyboard interaction shortcut and uses the nearest reachable usable
object when nothing is pointed at. There is no persistent RTS selection and no
click-to-move. A small contextual prompt shows the action and gathering progress.

**B** toggles the clickable build palette. Choose a structure, point at nearby
ground, **R** to rotate, and **left click** to place. **Right click**, **Esc**, or
**B** cancels building. Placement follows the mouse rather than the player's
facing and is limited to the configured reach.

**Tab** opens inventory, **M** opens the map, and **Esc** closes the current
panel or opens the pause menu. Menus have clickable controls and prevent input
from leaking into the world. Offline pause stops the simulation; a network
session continues while its menu is open. Start from the in-game solo/LAN lobby,
or launch with `--offline`, `--host`, or `--join IP`.

The HUD displays actual health, day/time, wood, stone, food, and carried boat
parts. Food is an inventory count, not hunger. Boat parts show carrying capacity,
not repaired-boat progress; hunger and the boat-repair UI remain future work.
The interface uses bundled OFL-licensed Source Sans 3 and Source Serif 4 fonts,
not an operating-system font fallback. The pointer remains the familiar native
cursor; action readiness appears in the contextual prompt rather than a colored
debug arrow. HUD colors/text sizing and picking settings live in `Assets/Data/Hud.asset` and
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
