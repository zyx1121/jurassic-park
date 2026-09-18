# Extracted object data of the original map

Object tables of the Warcraft III custom map "侏儸紀公園 6.3 繁體中文版" (author SaMmM, Traditional Chinese
localisation by ntcalex, downloaded from epicwar.com/maps/200141), decoded on 2026-09-17 with StormLib and a
small format parser. They are the machine-readable source behind `docs/ORIGINAL_CONTENT.md` and the input of
the data-driven content import (issue #111). Only fields the map overrides are present: a missing field means
the Blizzard base object's value, which is not part of the map file.

| File | Contents | Entries |
|---|---|---|
| `w3u.json` | units and buildings (`original` = modified stock objects, `custom` = new objects) | 6 + 156 |
| `w3t.json` | items | 6 + 92 |
| `w3a.json` | abilities | 25 + 182 |
| `w3q.json` | upgrades and research | 2 + 68 |
| `w3b.json` | destructables | 7 + 3 |
| `w3d.json` | doodads | 7 |
| `w3i.json` | map info: players, forces, start locations, fog settings | 1 |
| `war3map.wts` | string table for `TRIGSTR_n` references | 79 |

Each object is `{old_id, new_id, mods: [{id, type, value, extras}]}`. `id` is the four-letter field rawcode
(`unam` name, `uhpm` hit points, `usid` sight by day, `usin` sight by night, `ugor` gold cost, `ulum` lumber cost,
`ubld` build time, `uabi` abilities, ...); `type` is 0 int, 1 real, 2 unreal, 3 string; `extras` holds the level
and data-column for per-level ability fields. The map's script (`war3map.j`), terrain and doodad placement are
not included here.
