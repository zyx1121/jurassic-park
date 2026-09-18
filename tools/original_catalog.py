#!/usr/bin/env python3
"""Normalise the decoded object tables of the original map into docs/original/catalog.json.

Reads docs/original/extracted/{w3u,w3t,w3q,w3a}.json and war3map.wts, resolves TRIGSTR references, converts
Warcraft III units to the game's metres and seconds, and writes one deterministic JSON file the Editor importer
reads. Only fields the map overrides are present; a field the map leaves to the Blizzard base object is `null`
and listed under `missing`, so nothing here is invented. Conversion: 128 Warcraft units = one 2 m cell, so 64
units per metre; speeds are units per second; time fields are already seconds; damage is base + dice * sides
expected value (base + dice * (sides + 1) / 2).

Usage: python3 tools/original_catalog.py [--check]   (--check exits 1 when the committed file is stale)
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXTRACTED = ROOT / "docs" / "original" / "extracted"
OUTPUT = ROOT / "docs" / "original" / "catalog.json"
UNITS_PER_METRE = 64.0

# Base object kinds: buildings vs units, from the Blizzard object the custom one derives from.
BUILDING_BASES = {"hhou", "hars", "harm", "htow", "otrb", "oalt", "ngol", "hbar", "hlum", "hvlt", "hgtw", "halt", "hwtw", "harm"}
UNIT_BASES = {"hpea", "otau", "ownr", "ewsp", "hmtt", "hgyr", "nalb", "hfoo", "hrif", "nfrl", "nsno", "hhes", "hkni", "hspt",
              "ncrb", "nech", "nfbr", "nfnp", "nfro", "nglm", "nhmc", "nspr", "okod", "otbr", "owyv", "ufro"}   # creeps, dummies and effect carriers

# Fields the game needs for a unit or building. A missing one is reported, never guessed.
UNIT_FIELDS = ("unam", "uhpm", "udef", "umvs", "ugol", "ulum", "ubld", "ufoo", "ufma", "usid", "usin", "ua1b", "ua1d", "ua1s", "ua1c", "ua1r",
               "uacq", "ucol", "upat", "uabi", "ubui", "utra", "ures", "ureq", "uupt", "usei", "ubba", "ubdi", "ubsi", "upgr", "umdl", "utip", "utub")
ITEM_FIELDS = ("unam", "igol", "ilum", "ilev", "icla", "isto", "istr", "iuse", "idrp", "iabi", "iper", "ipaw", "utip", "utub", "ides")
UPGRADE_FIELDS = ("gnam", "glvl", "gglb", "gglm", "glmb", "glmm", "gtib", "gtim", "greq", "gef1", "gef2", "gef3", "gef4", "gba1", "gmo1", "gtp1", "gub1")
ABILITY_FIELDS = ("anam", "atp1", "aub1", "acdn", "amcs", "aare", "adur", "ahdu", "alev", "aher", "atar", "amat")


def load_strings() -> dict[int, str]:
    text = (EXTRACTED / "war3map.wts").read_text(encoding="utf-8-sig", errors="replace")
    return {int(n): body.strip() for n, body in re.findall(r"STRING (\d+)\s*\{\s*(.*?)\s*\}", text, re.S)}


def resolve(value, strings: dict[int, str]):
    if isinstance(value, str):
        match = re.fullmatch(r"TRIGSTR_(\d+)", value.strip())
        if match:
            return strings.get(int(match.group(1)), value)
    return value


def mods_of(obj, strings) -> dict:
    """Field rawcode → value. Per-level fields (extras hold level and column) become {level: value} maps."""
    fields: dict = {}
    for mod in obj["mods"]:
        value = resolve(mod["value"], strings)
        if mod.get("extras"):
            level = mod["extras"][0]
            fields.setdefault(mod["id"], {})[str(level)] = value
        else:
            fields[mod["id"]] = value
    # A per-level field with a single level reads like a plain field.
    return {code: (next(iter(value.values())) if isinstance(value, dict) and len(value) == 1 else value) for code, value in fields.items()}


def metres(units):
    return None if units is None else round(float(units) / UNITS_PER_METRE, 4)


def split_codes(value):
    if not value or not isinstance(value, str):
        return []
    return [code.strip() for code in value.split(",") if code.strip()]


def footprint_cells(pathing):
    """A pathing map file name like '4x4SimpleSolid.tga' is N x N Warcraft pathing squares of 32 units; 4 squares per cell side."""
    if not isinstance(pathing, str):
        return None
    match = re.search(r"(\d+)x(\d+)", pathing)
    if not match:
        return None
    return {"width": max(1, int(match.group(1)) // 4), "height": max(1, int(match.group(2)) // 4)}


def expected_damage(base, dice, sides):
    if base is None:
        return None
    dice = dice if dice is not None else 1
    sides = sides if sides is not None else 1
    return round(float(base) + float(dice) * (float(sides) + 1.0) / 2.0, 3)


def unit_record(obj, strings) -> dict:
    fields = mods_of(obj, strings)
    base = obj["old_id"]
    kind = "Building" if base in BUILDING_BASES else "Unit" if base in UNIT_BASES else "Unknown"
    get = fields.get
    return {
        "id": obj["new_id"],
        "base": base,
        "kind": kind,
        "name": get("unam"),
        "tooltip": get("utip"),
        "description": get("utub"),
        "model": get("umdl"),
        "hitPoints": get("uhpm"),
        "armor": get("udef"),
        "moveSpeedMetresPerSecond": metres(get("umvs")),
        "cost": {"gold": get("ugol"), "lumber": get("ulum")},
        "buildSeconds": get("ubld"),
        "foodUsed": get("ufoo"),
        "foodMade": get("ufma"),
        "sightMetres": {"day": metres(get("usid")), "night": metres(get("usin"))},
        "attack": {
            "base": get("ua1b"), "dice": get("ua1d"), "sides": get("ua1s"),
            "expectedDamage": expected_damage(get("ua1b"), get("ua1d"), get("ua1s")),
            "cooldownSeconds": get("ua1c"), "rangeMetres": metres(get("ua1r")),
        },
        "acquireRangeMetres": metres(get("uacq")),
        "collisionMetres": metres(get("ucol")),
        "footprintCells": footprint_cells(get("upat")),
        "bounty": {"base": get("ubba"), "dice": get("ubdi"), "sides": get("ubsi")},
        "abilities": split_codes(get("uabi")),
        "builds": split_codes(get("ubui")),
        "trains": split_codes(get("utra")),
        "researches": split_codes(get("ures")),
        "requires": split_codes(get("ureq")),
        "upgradesTo": split_codes(get("uupt")),
        "sells": split_codes(get("usei")),
        "upgradesUsed": split_codes(get("upgr")),
        "missing": [code for code in UNIT_FIELDS if code not in fields],
        "raw": {code: fields[code] for code in sorted(fields)},
    }


def item_record(obj, strings) -> dict:
    fields = mods_of(obj, strings)
    get = fields.get
    return {
        "id": obj["new_id"], "base": obj["old_id"], "name": get("unam"), "tooltip": get("utip"), "description": get("utub"),
        "cost": {"gold": get("igol"), "lumber": get("ilum")}, "level": get("ilev"), "class": get("icla"),
        "shopStock": get("isto"), "shopRestockSeconds": get("istr"), "usable": get("iuse"), "droppable": get("idrp"),
        "perishable": get("iper"), "pawnable": get("ipaw"), "abilities": split_codes(get("iabi")),
        "missing": [code for code in ITEM_FIELDS if code not in fields], "raw": {code: fields[code] for code in sorted(fields)},
    }


def upgrade_record(obj, strings) -> dict:
    fields = mods_of(obj, strings)
    get = fields.get
    return {
        "id": obj["new_id"], "base": obj["old_id"], "name": get("gnam"), "levels": get("glvl"),
        "gold": {"base": get("gglb"), "perLevel": get("gglm")}, "lumber": {"base": get("glmb"), "perLevel": get("glmm")},
        "researchSeconds": {"base": get("gtib"), "perLevel": get("gtim")}, "requires": split_codes(get("greq")),
        "effects": [{"code": get(f"gef{i}"), "base": get(f"gba{i}"), "perLevel": get(f"gmo{i}")} for i in range(1, 5) if get(f"gef{i}")],
        "missing": [code for code in UPGRADE_FIELDS if code not in fields], "raw": {code: fields[code] for code in sorted(fields)},
    }


def ability_record(obj, strings) -> dict:
    fields = mods_of(obj, strings)
    get = fields.get
    return {
        "id": obj["new_id"], "base": obj["old_id"], "name": get("anam"), "tooltip": get("atp1"), "description": get("aub1"),
        "cooldownSeconds": get("acdn"), "castRangeMetres": metres(get("amcs")) if not isinstance(get("amcs"), dict) else {k: metres(v) for k, v in get("amcs").items()},
        "areaMetres": metres(get("aare")) if not isinstance(get("aare"), dict) else {k: metres(v) for k, v in get("aare").items()},
        "durationSeconds": get("adur"), "heroDurationSeconds": get("ahdu"), "levels": get("alev"), "hero": get("aher"), "targets": get("atar"),
        "missing": [code for code in ABILITY_FIELDS if code not in fields], "raw": {code: fields[code] for code in sorted(fields)},
    }


def table(name, record):
    data = json.loads((EXTRACTED / f"{name}.json").read_text(encoding="utf-8"))
    strings = load_strings()
    custom = [record(obj, strings) for obj in data["custom"]]
    original = [record(obj, strings) for obj in data["original"]]
    for row in original:
        row["id"] = row["base"]   # a modified stock object keeps its own id
    return sorted(custom + original, key=lambda row: row["id"])


def build() -> dict:
    return {
        "source": "侏儸紀公園 6.3 繁體中文版 (epicwar.com/maps/200141), decoded object tables under docs/original/extracted",
        "conversion": {"unitsPerMetre": UNITS_PER_METRE, "cellMetres": 2.0, "note": "null means the map keeps the Blizzard base object's value, which is not in the map file"},
        "units": table("w3u", unit_record),
        "items": table("w3t", item_record),
        "upgrades": table("w3q", upgrade_record),
        "abilities": table("w3a", ability_record),
    }


def main() -> int:
    text = json.dumps(build(), ensure_ascii=False, indent=1, sort_keys=False) + "\n"
    if "--check" in sys.argv:
        current = OUTPUT.read_text(encoding="utf-8") if OUTPUT.exists() else ""
        if current != text:
            print(f"{OUTPUT.relative_to(ROOT)} is stale; run tools/original_catalog.py", file=sys.stderr)
            return 1
        print("catalog.json is up to date")
        return 0
    OUTPUT.write_text(text, encoding="utf-8")
    data = json.loads(text)
    print(f"wrote {OUTPUT.relative_to(ROOT)}: {len(data['units'])} units, {len(data['items'])} items, {len(data['upgrades'])} upgrades, {len(data['abilities'])} abilities")
    return 0


if __name__ == "__main__":
    sys.exit(main())
