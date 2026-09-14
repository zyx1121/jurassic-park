# Jurassic Park: an HD-2D Co-op Dinosaur Survival Game

3D Game Programming, Fall 2026. Project proposal draft, 2026-09-14.

Student: 詹詠翔 (314551002), solo project.

## 1. Concept

A 1 to 4 player co-op survival game. The players are stranded on a jungle island full of dinosaurs. Gather resources by day, build fences and campfires, and survive the nights while dinosaurs hunt you. The only way to win is to find the boat parts and repair the boat before the island overruns you.

Gameplay reference: the Warcraft III custom map "Jurassic Park" (co-op survival, resource gathering, base building, dinosaurs that hunt the players).

Visual reference: Octopath Traveler's HD-2D style. Pixel-art characters as billboard sprites inside a low-poly 3D diorama, with depth of field, bloom, and real-time lighting.

The game is fully playable alone. Multiplayer is host-client co-op over Unity Netcode for GameObjects; the single-player path is the fallback if networking slips.

## 2. Core Loop

1. Day: explore, collect wood, stone, food, and boat parts scattered across the island.
2. Dusk: place fences, light the campfire, carry parts to the boat.
3. Night: dinosaurs spawn and hunt. Hide, fight, or run. Downed players can be revived by teammates.
4. Dawn: survivors heal, the world gets harder (more and larger dinosaurs, parts spawn farther away).

Win: all boat parts installed and every living player on board. Lose: all players downed at the same time.

Player controls: WASD movement, mouse aim, one attack, one interact, one build menu. No RTS unit selection.

## 3. Game Design

### 3.1 Story

A research team's supply plane crashes on an abandoned island theme park. The park's power failed years ago and the dinosaurs now roam free. The only intact vehicle is a boat at the old visitor dock, missing its engine parts, which were scattered across the park's facilities during the evacuation. The players have a few days before the resident T-Rex, drawn by the noise, decides to move to the beach.

Story delivery is light: an intro card, a note or terminal log found at each facility (5 in total), and an ending card. The 9/21 lecture on the hero's journey will be used to structure the beats: call to adventure (crash), trials (each night), ordeal (T-Rex night), return (escape).

### 3.2 Player Characters

Up to 4 playable survivors, each with one passive perk so co-op roles differ. All share the same base kit (melee, throw, gather, build).

| Character | Perk |
|---|---|
| Ranger | Longer sight range at night, faster movement |
| Engineer | Fences and repairs cost fewer resources, faster boat repair |
| Medic | Faster revive, food heals more |
| Hunter | Higher spear damage, retrieves thrown spears automatically nearby |

Visual: 4 recolors of one pixel-art sprite sheet, so the art cost is one character.

### 3.3 Enemy Characters

| Dinosaur | Role | Behavior |
|---|---|---|
| Compsognathus | Nuisance, day and night | Swarms in groups of 5 to 8, steals dropped food, flees when hit |
| Velociraptor | Main threat at night | Packs of 3, flanking, jumps low fences after night 3 |
| Dilophosaurus (optional) | Ranged | Spits to blind the screen (vignette pulse), keeps distance. First species to cut if sprite work runs late |
| Tyrannosaurus | Boss, from night 4 | Slow, breaks any fence in one hit, roar scatters other dinosaurs, retreats at dawn |

Neutral fauna: Parasaurolophus herds as huntable food that also alert predators when panicked.

### 3.4 Objects

Player-built structures (placed on a grid, cost resources, have durability):

- Wooden fence, stone wall, gate
- Campfire (light, cooking, stealth penalty), torch post
- Storage chest (shared inventory in co-op)
- Spike trap, noise lure

World structures (fixed on the island, each holds one boat part and one story log):

- Visitor dock with the boat (base and win location)
- Crashed plane (start point)
- Visitor center ruins
- Power station
- Raptor paddock (broken)
- Ranger lookout tower

Enemy structures:

- Raptor nests: spawn points at night, can be burned down with a torch to reduce raptor count
- T-Rex lair: marks the boss's home; approaching it early triggers an early boss appearance

Pickups: wood, stone, food (raw and cooked), boat parts (5), spears, medkits, story logs.

## 4. System Breakdown

The course grading slide splits a game into a Game System that drives Character Animation, Path Finding, GUI, Attack and Defense, and FX. The breakdown below follows that structure and adds the systems this project needs on top. Items marked (M) are mini-exercise candidates.

### 4.1 Game System (core)

- Game state machine: Lobby, Day, Dusk, Night, Dawn, Win, Lose.
- Day-night cycle: one timer drives sun angle, fog density, spawn tables, and music.
- Resource and inventory model: wood, stone, food, boat parts; per-player inventory plus a shared boat progress.
- Building: fence segments and campfires placed on a grid, with durability and repair.
- Boat repair as the win condition: N parts, each installed at the boat, progress visible to all players.
- Save and load of the current day (single-player only).
- Difficulty curve table: dinosaur count and species per night.

### 4.2 Multiplayer

- Netcode for GameObjects, host-client, 1 to 4 players; Unity Relay so no port forwarding.
- Server-authoritative: dinosaur AI, spawns, resources, boat progress, damage all run on the host.
- Client prediction for player movement only.
- Synced state: player position and animation state, inventory, structures, day timer, dinosaur transforms.
- Join in lobby only; disconnected player's character becomes an idle NPC until they return.
- Testing: two Editor instances via ParrelSync or a Mac build plus a Windows build.

### 4.3 Character Animation (M)

- Sprite-sheet animation for the player: idle, walk, attack, gather, downed, revive.
- 3 dinosaur species: small pack hunter (raptor), medium solo hunter, T-Rex boss. Each with idle, walk, run, attack, hit, death.
- Billboard sprites in 3D: always face the camera, flip left-right, sort by depth.
- 8-direction or 4-direction sprites decided by asset availability.
- Animation state driven by the AI or input state machine, not by separate triggers.

### 4.4 Path Finding (M)

- NavMesh baked on the 3D terrain; fences and campfires are NavMesh obstacles with carving so dinosaurs route around new walls.
- NavMeshAgent per dinosaur with species-specific speed, radius, and acceleration.
- Player navigation is direct WASD, not NavMesh.
- Group behavior: raptors pick flanking points around the target instead of the same path.
- Steering fallback when the target is unreachable (attack the fence in the way).

### 4.5 AI: Finite State Machine and Group Behaviors

- Per-dinosaur FSM: Idle, Patrol, Investigate (heard noise), Chase, Attack, Flee (low health), Eat (food bait).
- Senses: sight cone, hearing radius; campfire light reduces stealth at night, noise from building attracts.
- Pack coordination for raptors: one leader chooses the target, followers take flank slots.
- T-Rex: slow, breaks fences in one hit, roars to scatter smaller dinosaurs.
- Spawn director: spawn out of sight of every player, weighted by night number.

### 4.6 GUI (M)

- HUD: health, hunger, day counter and clock, resource counts, boat progress bar.
- Build menu: radial or hotbar, shows cost and placement preview.
- Inventory panel and boat repair panel.
- Co-op: teammate health markers, offscreen arrows, "revive" prompt.
- Lobby: host or join with code, ready state.
- Pause, settings, win and lose screens with per-player stats.

### 4.7 Attack and Defense System (M)

- Player attacks: melee (club, spear) and thrown spear with pickup.
- Damage model: hit points, armor from crafted gear, knockback.
- Fence durability and repair cost; T-Rex bypasses durability.
- Dinosaur attacks: bite with wind-up telegraph, pounce for raptors.
- Downed and revive: a downed player bleeds out over 30 seconds unless revived.
- Friendly fire disabled by default.

### 4.8 FX Effects

- URP Volume post-processing: depth of field (diorama look), bloom on fire and eyes, vignette, night color grading.
- Particles: campfire, torch, rain, dust on footsteps, blood on hit, splinters on fence break.
- Camera shake on T-Rex footsteps, hit flash on sprites.
- Audio: 3D positional roars, footsteps scaled by distance, night ambience.

### 4.9 Scene, Terrain and Camera

- One island scene: beach with boat, jungle, ruins, clearing for base.
- Low-poly 3D terrain with pixel textures, point filtering, no mip blur.
- Perspective camera tilted about 30 degrees, following the local player, with soft clamp at island edges.
- Lighting: directional sun on the day timer, real-time point lights for fire, shadow casters capped at 4.

### 4.10 Game Physics

- Rigidbody and collider only for thrown spears and dropped items.
- Dinosaurs and players use character controllers plus triggers for attacks.
- Fences are static colliders that also carve the NavMesh.

## 5. Art Direction (HD-2D)

- 3D terrain and props built from simple meshes with pixel-art textures.
- Characters and dinosaurs are 2D sprites that always face the camera.
- Camera: perspective, tilted about 30 degrees, following the player.
- Post-processing via URP Volume: depth of field, bloom, vignette, color grading.
- Real-time point lights with shadows on the 3D scene.

## 6. Technology

- Unity 6 LTS (6000.3), Universal Render Pipeline, C#.
- Unity AI Navigation package for NavMesh.
- Unity Netcode for GameObjects and Unity Relay for co-op.
- Unity Input System.
- Target platform: macOS (Apple Silicon) with a Windows build for the TA demo.
- Version control: Git on GitHub.

## 7. Schedule

| Date | Milestone |
|---|---|
| 10/19 | Proposal presentation |
| 11/09 | Progress report 1: HD-2D scene, player movement and animation, camera, day-night lighting |
| 11/16 | Online demo 1: Assignment 1, mini-exercises 1 and 2 (animation, path finding) |
| 11/23 | Progress report 2: dinosaur FSM, resource gathering, fence building, first co-op sync |
| 12/07 | Progress report 3: combat, HUD, boat repair, full loop playable in co-op |
| 12/14 | Online demo 2: Assignment 2, mini-exercises 3 and 4 (GUI, attack and defense) |
| 12/28 | Final presentation, live demo, printed report |

## 8. Use of AI

AI tools (Claude, Copilot) will be used for boilerplate C#, shader and post-processing setup, and for generating first-pass dinosaur sprite sheets that are then hand-corrected. Game design, system architecture, AI state machines, network authority design, and integration are written by me. The final report will state per system what was AI-assisted and what was hand-written.

## 9. Risks

| Risk | Mitigation |
|---|---|
| Multiplayer is the largest scope item for a solo developer | Single-player is complete on its own; networking layered on top after the loop works; host-authoritative to avoid reconciliation bugs |
| Pixel-art dinosaur sprites are scarce | Core is 3 species (Compsognathus, Velociraptor, T-Rex); Dilophosaurus is cut first if art runs late; AI-generated sheets plus manual cleanup; fall back to recolored free packs |
| Post-processing cost on a laptop | Low-poly meshes, shadow casters capped at 4 lights |
| Name "Jurassic Park" is a trademark | Fine for a course project; rename before any public release |
| Solo scope creep | Sections 4.1 to 4.7 are required; 4.8 to 4.10 are polish; stretch goals listed separately |

Cut order if behind schedule: Dilophosaurus, enemy structures (nests and lair), character perks, story logs, then multiplayer (game ships single-player).

Stretch goals: weather system, procedural resource placement, a second island biome, dinosaur taming.
