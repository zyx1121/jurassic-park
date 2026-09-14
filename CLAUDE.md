# CLAUDE.md

Unity 6000.3 LTS, URP, C#. HD-2D co-op dinosaur survival game for the NYCU 3D Game Programming course (Fall 2026). Design source of truth: `docs/PROPOSAL.md`. Work is tracked as GitHub issues under course-dated milestones.

## Rules

- Never hand-edit `.unity`, `.prefab`, or `.asset` YAML while an Editor is open. Use `unity status` first; drive the live Editor or run batchmode.
- Scripts live in `Assets/Scripts/<System>/`; one MonoBehaviour per file, namespace `JurassicPark.<System>`.
- Every gameplay number (speeds, costs, HP, spawn tables) goes in a ScriptableObject under `Assets/Data/`, not a literal in code.
- Networking is host-authoritative. Gameplay logic must run correctly with `NetworkManager` absent (single-player path).
- Binary assets go through Git LFS (see `.gitattributes`). Sprites are point filtered, no compression, no mipmaps.
- Verify before claiming done: `unity test . --mode EditMode`, `unity build . --target StandaloneOSX --output-path Builds/macOS/JurassicPark.app`, or a Play-mode check in the Editor.
- Branch, PR, CI green, squash-merge. Commit messages in English, imperative mood.
- Unity automation goes through the Unity CLI (`unity run` / `unity test` / `unity build`), never hand-rolled batchmode commands. Read the unity-cli skill references first.
- New files under `Assets/` need their `.meta`: after adding, run `unity run . -- -nographics` once and commit the generated `.meta` files.

## Course dates

10/19 proposal, 11/09 report 1, 11/16 demo 1, 11/23 report 2, 12/07 report 3, 12/14 demo 2, 12/28 final. Details in `docs/PROPOSAL.md`.
