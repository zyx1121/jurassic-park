# Jurassic Park

Unity 6000.3 LTS, URP, C#. A co-op RTS survival game that rebuilds the Warcraft III "Jurassic Park" 6.3 map, for the NYCU 3D Game Programming course (Fall 2026). Solo project. Read [README.md](README.md) first.

`docs/PLAN.md` is the one design source of truth (v1, 2026-09-17). `docs/ART_DIRECTION.md` owns the visuals. The old prototype is on the `legacy` branch, for reference only. Do not copy code back from `legacy` in bulk.

## Rules

- Follow the plan. Do not reopen the items marked confirmed in PLAN section 1. Content and numbers come from the original 6.3 map (`docs/ORIGINAL_CONTENT.md`), not from invention. When the plan says nothing, raise a decision. Do not improvise.
- The simulation is pure C# with no MonoBehaviour dependency. Each input (player, GUI, AI seat) goes through Command, Task, Plan and Action. Rendering, animation and GUI never change simulation state.
- Host-authoritative. Clients send commands and draw snapshots. No client prediction, no lockstep. Single player is the same path with nobody connected.
- Use Netcode for GameObjects only for the connection and named messages (`NetMessages`): no NetworkObjects, no NetworkBehaviours, no scene management. The seat of a command comes from `SeatBinder`, never from the payload.
- Presentation reads `MatchReadModel` and sends through `GameSession.Commands`. It never touches `SimulationRuntime`, so the same code runs on a host and on a client.
- Each gameplay number lives in a ScriptableObject under `Assets/Data/`, not as a literal in code.
- Scripts go in `Assets/Scripts/<System>/`, namespace `JurassicPark.<System>`. One MonoBehaviour or ScriptableObject per file, and the file name equals the class name. Otherwise builds lose the script reference.
- Never hand-edit `.unity`, `.prefab` or `.asset` YAML while an Editor is open. Builder commands generate scenes and prefabs: change the builder, run it again and commit the result.
- New files under `Assets/` need their `.meta` in the commit. Binary assets go through Git LFS. Sprites use point filtering, no compression and no mipmaps.
- Repository documents, code comments and commit messages are English, in the imperative mood.

## Verify

```sh
tools/test.sh
tools/test.sh PlayMode
```

`tools/test.sh` must show zero failures from a new report. A real build or a Play Mode run also counts. CI looks only at repo hygiene.

Unity automation goes through the Unity CLI (`unity run`, `unity test`, `unity build`). Read the unity-cli skill references first. Unity runs on the MacBook, by the owner's decision (2026-09-17): tests, the resident Editor, Play runs, captures and builds. The target is a MacBook Air M2 at 60 fps. Use one terrain mesh, instanced materials and no allocations per frame in views. Give batch-mode Editors a low frame rate, so the fanless machine does not throttle.

## Work

GitHub issues under milestones M0 to M3. Branch, PR, CI green, squash-merge. Course dates: 10/19 proposal, 11/09 report 1, 11/16 demo 1, 11/23 report 2, 12/07 report 3, 12/14 demo 2, 12/28 final.
