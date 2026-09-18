#!/usr/bin/env python3
"""Decode the terrain, doodads and script regions of the original map into docs/original/map_layout.json.

Reads the raw map files the repository does not carry (`war3map.w3e` terrain, `war3map.doo` doodads,
`war3map.j` script) plus the decoded object tables under `docs/original/extracted`, and writes one
deterministic JSON file and a 512 x 512 preview PNG. This is the spatial counterpart of
`tools/original_catalog.py`: it records what the sample map contains, it does not design a new map.

The raw files are not in the repository. Point `--raw` at the directory that holds them; the default is the
iCloud archive under Areas/Courses/3D遊戲程式/original-map/extracted. They are read, never written.

Usage: python3 tools/original_map_layout.py [--raw DIR] [--check]   (--check exits 1 when the outputs are stale)
"""
from __future__ import annotations

import argparse
import json
import re
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXTRACTED = ROOT / "docs" / "original" / "extracted"
OUTPUT_JSON = ROOT / "docs" / "original" / "map_layout.json"
OUTPUT_PNG = ROOT / "docs" / "original" / "map_layout.png"
DEFAULT_RAW = Path.home() / "Library/Mobile Documents/com~apple~CloudDocs/Areas/Courses/3D遊戲程式/original-map/extracted"

UNITS_PER_CELL = 128.0
CELLS = 128
PIXELS_PER_CELL = 4

# Doodad and destructable rawcodes are <tileset><category><variant>. The category letter is the classifier:
# 'T' a destructable tree wall, 'R' a rock, 'P' a plant or prop. Lower-case categories and every other letter
# stay "other" and are listed under unresolved, so nothing is guessed.
TREE_PREFIXES = ("LT", "AT", "BT", "CT", "DT", "FT", "GT", "IT", "JT", "KT", "NT", "OT", "WT", "YT", "ZT")
CATEGORY_CLASS = {"R": "rock", "P": "plant"}

# Preview palette: one green per cliff layer height, low ground dark and high ground pale, so the terraces of
# the original read at 4 px per cell even though 84 per cent of the cells sit on layer 3.
LEVEL_GREENS = [(28, 46, 30), (40, 62, 38), (54, 80, 46), (72, 98, 58), (96, 118, 70),
                (124, 140, 88), (154, 162, 110), (184, 186, 136), (210, 208, 166)]


# --- war3map.w3e ------------------------------------------------------------------------------------------

class Reader:
    def __init__(self, data: bytes) -> None:
        self.data = data
        self.at = 0

    def take(self, count: int) -> bytes:
        chunk = self.data[self.at:self.at + count]
        if len(chunk) != count:
            raise ValueError(f"war3map file truncated at {self.at}")
        self.at += count
        return chunk

    def unpack(self, fmt: str):
        values = struct.unpack_from("<" + fmt, self.data, self.at)
        self.at += struct.calcsize("<" + fmt)
        return values

    def rawcode(self) -> str:
        return self.take(4).decode("ascii", "replace")

    @property
    def remaining(self) -> int:
        return len(self.data) - self.at


def read_terrain(path: Path) -> dict:
    """Vertex grid of war3map.w3e. Each vertex is 7 bytes; the parser must land exactly on the end of file.

    Byte layout per vertex: int16 ground height, int16 water level with the boundary bit, one byte holding the
    flag nibble in the high half and the ground texture index in the low half, one texture-details byte, and
    one byte holding the cliff texture index in the high half and the cliff layer height in the low half.
    The flag nibble is 0x1 ramp, 0x2 blight, 0x4 water, 0x8 camera boundary; bit 0x4000 of the water int16 is
    the map-boundary marker, not water (verified against the data: it forms the unplayable border ring that
    matches the w3i camera complements, while the 0x4 flag forms the lakes and the coastline).
    """
    reader = Reader(path.read_bytes())
    magic = reader.rawcode()
    if magic != "W3E!":
        raise ValueError(f"{path.name}: expected the W3E! magic, found {magic!r}")
    version, = reader.unpack("i")
    tileset = reader.take(1).decode("ascii")
    custom_tilesets, = reader.unpack("i")
    ground_count, = reader.unpack("i")
    ground_tilesets = [reader.rawcode() for _ in range(ground_count)]
    cliff_count, = reader.unpack("i")
    cliff_tilesets = [reader.rawcode() for _ in range(cliff_count)]
    width, height = reader.unpack("ii")          # vertices, one more than the cell count on each axis
    offset_x, offset_y = reader.unpack("ff")     # world position of vertex (0, 0)

    vertices = []
    for _ in range(width * height):
        ground, water, texture_flags, details, cliff = reader.unpack("hhBBB")
        vertices.append({
            "ground": ground,
            "waterLevel": water & 0x3FFF,
            "boundary": bool(water & 0x4000),
            "flags": texture_flags >> 4,
            "water": bool((texture_flags >> 4) & 0x4),
            "ramp": bool((texture_flags >> 4) & 0x1),
            "texture": texture_flags & 0x0F,
            "details": details,
            "cliffTexture": cliff >> 4,
            "layer": cliff & 0x0F,
        })
    return {
        "version": version,
        "tileset": tileset,
        "customTilesets": bool(custom_tilesets),
        "groundTilesets": ground_tilesets,
        "cliffTilesets": cliff_tilesets,
        "width": width,
        "height": height,
        "offset": (offset_x, offset_y),
        "vertices": vertices,
        "remainingBytes": reader.remaining,
    }


# --- war3map.doo ------------------------------------------------------------------------------------------

def read_doodads(path: Path) -> dict:
    """Doodad and destructable placements of war3map.doo, version 8.

    Per doodad: rawcode, variation int, x y z floats, angle float, scale x y z floats, flags byte, life byte
    (percent), then the version 8 item table fields (an int random-item-set pointer and an int count of item
    sets, each set being a count of items followed by rawcode plus chance pairs), then the editor id int.
    The special doodads section that follows is an int format version, an int count and per entry a rawcode,
    a variation int and the x and y cell indices. The parser must consume the file exactly.
    """
    reader = Reader(path.read_bytes())
    magic = reader.rawcode()
    if magic != "W3do":
        raise ValueError(f"{path.name}: expected the W3do magic, found {magic!r}")
    version, subversion, count = reader.unpack("iii")
    if version != 8:
        raise ValueError(f"{path.name}: only doodad format version 8 is decoded, found {version}")

    doodads = []
    for _ in range(count):
        type_id = reader.rawcode()
        variation, = reader.unpack("i")
        x, y, z, angle, scale_x, scale_y, scale_z = reader.unpack("7f")
        flags, life = reader.unpack("BB")
        _item_pointer, set_count = reader.unpack("ii")
        for _ in range(set_count):
            item_count, = reader.unpack("i")
            for _ in range(item_count):
                reader.rawcode()
                reader.unpack("i")
        editor_id, = reader.unpack("i")
        doodads.append({
            "id": type_id, "variation": variation, "x": x, "y": y, "z": z, "angle": angle,
            "scale": (scale_x, scale_y, scale_z), "flags": flags, "life": life, "editorId": editor_id,
        })

    special_version, special_count = reader.unpack("ii")
    specials = []
    for _ in range(special_count):
        special_id = reader.rawcode()
        variation, cell_x, cell_y = reader.unpack("iii")
        specials.append({"id": special_id, "variation": variation, "cellX": cell_x, "cellY": cell_y})

    return {
        "version": version,
        "subversion": subversion,
        "doodads": doodads,
        "specialVersion": special_version,
        "specials": specials,
        "remainingBytes": reader.remaining,
    }


# --- classification ---------------------------------------------------------------------------------------

def object_table(name: str) -> dict:
    return json.loads((EXTRACTED / f"{name}.json").read_text(encoding="utf-8"))


def overrides(table: dict) -> dict[str, dict]:
    """Rawcode the placement refers to -> {"base": stock id, "fields": {rawcode: value}}.

    A custom object is addressed by its new id and derives from `old_id`; a modified stock object keeps its
    own id. Only fields the map overrides are present, exactly as in docs/original/extracted.
    """
    result: dict[str, dict] = {}
    for obj in table["custom"]:
        result[obj["new_id"]] = {"base": obj["old_id"], "fields": {mod["id"]: mod["value"] for mod in obj["mods"]}}
    for obj in table["original"]:
        result[obj["old_id"]] = {"base": obj["old_id"], "fields": {mod["id"]: mod["value"] for mod in obj["mods"]}}
    return result


def classify(type_id: str, base_id: str, name: str | None) -> str:
    """tree / rock / plant / other. The map's own name wins; otherwise the rawcode category letter decides."""
    if name:
        if "樹" in name or "Tree" in name:
            return "tree"
        if "岩" in name or "石" in name or "Rock" in name:
            return "rock"
        if "門" in name or "Gate" in name:
            return "other"
    if base_id[:2] in TREE_PREFIXES:
        return "tree"
    return CATEGORY_CLASS.get(base_id[1], "other")


def pathing_of(fields: dict, klass: str) -> tuple[bool, str]:
    """(blocks ground, evidence). A pathing texture the map sets wins over the stock destructable default.

    In a Warcraft III path texture the red channel is unwalkable, the green channel unflyable and the blue
    channel unbuildable, so a "...Unflyable" texture blocks air only and leaves the ground open. An empty
    texture removes the footprint. When the map overrides nothing, the stock default applies: destructable
    tree walls and rocks block the ground, decorative doodads do not.
    """
    texture = fields.get("bptx", fields.get("dptx"))
    if texture is not None:
        texture = str(texture)
        if texture == "":
            return False, "map sets an empty pathing texture"
        if "Unflyable" in texture:
            return False, f"map sets {texture}, which blocks air only"
        return True, f"map sets {texture}"
    if klass in ("tree", "rock"):
        return True, "stock destructable default (tree walls and rocks block)"
    return False, "stock doodad default (decoration, no footprint)"


# --- war3map.j regions ------------------------------------------------------------------------------------

def read_rects(script: str) -> tuple[dict[str, dict], list[str]]:
    """Global name -> {bounds, jassLine} for every `set <name>=Rect(x1,y1,x2,y2)` in the script, plus its lines."""
    rects: dict[str, dict] = {}
    for number, line in enumerate(script.splitlines(), start=1):
        match = re.fullmatch(r"set ([A-Za-z_][A-Za-z0-9_]*)=Rect\(([-.\d]+),([-.\d]+),([-.\d]+),([-.\d]+)\)", line.strip())
        if match:
            name = match.group(1)
            bounds = [float(match.group(i)) for i in range(2, 6)]
            rects[name] = {"bounds": bounds, "jassLine": number}
    return rects, script.splitlines()


def find_lines(lines: list[str], pattern: str) -> list[int]:
    expression = re.compile(pattern)
    return [number for number, line in enumerate(lines, start=1) if expression.search(line)]


# --- assembly ---------------------------------------------------------------------------------------------

def cell_of(value: float, offset: float) -> int:
    return max(0, min(CELLS - 1, int((value - offset) // UNITS_PER_CELL)))


def rect_record(name: str, rect: dict, offset: tuple[float, float], purpose: str, evidence: list[int]) -> dict:
    x1, y1, x2, y2 = rect["bounds"]
    return {
        "global": name,
        "purpose": purpose,
        "bounds": {"minX": x1, "minY": y1, "maxX": x2, "maxY": y2},
        "centre": [round((x1 + x2) / 2, 1), round((y1 + y2) / 2, 1)],
        "cells": {
            "minX": cell_of(x1, offset[0]), "minY": cell_of(y1, offset[1]),
            "maxX": cell_of(x2, offset[0]), "maxY": cell_of(y2, offset[1]),
        },
        "centreCell": [cell_of((x1 + x2) / 2, offset[0]), cell_of((y1 + y2) / 2, offset[1])],
        "jassLines": [rect["jassLine"]] + evidence,
    }


def build(raw: Path) -> tuple[dict, dict]:
    terrain = read_terrain(raw / "war3map.w3e")
    placement = read_doodads(raw / "war3map.doo")
    script = (raw / "war3map.j").read_text(encoding="utf-8", errors="replace")
    info = json.loads((EXTRACTED / "w3i.json").read_text(encoding="utf-8"))
    destructables = overrides(object_table("w3b"))
    doodad_types = overrides(object_table("w3d"))

    if terrain["remainingBytes"] or placement["remainingBytes"]:
        raise ValueError("a binary parser did not consume its whole file")

    width, offset = terrain["width"], terrain["offset"]
    vertices = terrain["vertices"]

    def vertex(x: int, y: int) -> dict:
        return vertices[y * width + x]

    # Cells. A cell (x, y) is the square between vertices (x, y) and (x + 1, y + 1); its ground texture is the
    # one of its lower-left vertex, its cliff level the lowest of its four corner layers.
    cliff_rows, water_rows, texture_rows, passable_rows = [], [], [], []
    for y in range(CELLS - 1, -1, -1):        # rows[0] is the northernmost row, so the grids read like the PNG
        corners = [[vertex(x, y), vertex(x + 1, y), vertex(x, y + 1), vertex(x + 1, y + 1)] for x in range(CELLS)]
        cliff_rows.append("".join(f"{min(c['layer'] for c in four):x}" for four in corners))
        water_rows.append("".join("1" if all(c["water"] for c in four) else "0" for four in corners))
        texture_rows.append("".join(f"{vertex(x, y)['texture']:x}" for x in range(CELLS)))
        passable_rows.append("".join(
            "0" if all(c["water"] for c in four) or len({c["layer"] for c in four}) > 1 else "1"
            for four in corners))

    # Doodads.
    type_records: dict[str, dict] = {}
    rows = []
    for doodad in placement["doodads"]:
        type_id = doodad["id"]
        record = type_records.get(type_id)
        if record is None:
            entry = destructables.get(type_id) or doodad_types.get(type_id) or {}
            base_id = entry.get("base", type_id)
            fields = entry.get("fields", {})
            name = fields.get("bnam") or fields.get("dnam")
            klass = classify(type_id, base_id, name)
            blocks, evidence = pathing_of(fields, klass)
            record = type_records[type_id] = {
                "id": type_id, "base": base_id, "name": name, "class": klass,
                "blocks": blocks, "pathing": evidence, "count": 0,
            }
        record["count"] += 1
        rows.append("|".join([
            type_id, record["class"],
            f"{doodad['x']:.1f}", f"{doodad['y']:.1f}",
            str(cell_of(doodad["x"], offset[0])), str(cell_of(doodad["y"], offset[1])),
            "1" if record["blocks"] else "0",
        ]))

    # Regions from the script.
    rects, lines = read_rects(script)
    base_globals = ["do", "Co", "co", "Bo", "bo", "No", "Ao", "Io", "Ro", "Oo", "Xo", "Eo"]
    evacuation_globals = ["Ar", "Nr", "br", "Br", "cr", "Cr", "dr", "Dr", "fr", "Fr"]
    base_evidence = find_lines(lines, r'TriggerRegisterPlayerChatEvent\(ZR,Player\(0\),"-base"') + \
        find_lines(lines, r"^function SW takes")
    evacuation_evidence = find_lines(lines, r"set cx\[1\]=Ar") + find_lines(lines, r"cx\[GetRandomInt\(1,10\)\]")
    survivor_evidence = find_lines(lines, r"CreateUnit\(p,'h000'")

    base_rects = [rect_record(name, rects[name], offset,
                              "-base minimap hint, pinged by the chat handler", base_evidence)
                  for name in base_globals]
    evacuation_rects = [rect_record(name, rects[name], offset,
                                    "rescue helicopter landing candidate, one of cx[1..10]", evacuation_evidence)
                        for name in evacuation_globals]

    other_regions = [
        rect_record("Vo", rects["Vo"], offset, "survivor gathering point near the map centre: the seven h000 "
                    "survivors are placed inside it and a converted survivor respawns at its centre",
                    survivor_evidence + find_lines(lines, r"'o00D',GetOwningPlayer\(GetDyingUnit\(\)\),GetRectCenter\(Vo\)")),
        rect_record("Zo", rects["Zo"], offset, "weather region, heavy rain", find_lines(lines, r"set we=AddWeatherEffect\(Zo")),
        rect_record("vr", rects["vr"], offset, "weather region, heavy rain", find_lines(lines, r"set we=AddWeatherEffect\(vr")),
        rect_record("er", rects["er"], offset, "east quadrant: weather and dinosaur spawn area",
                    find_lines(lines, r"GetRandomLocInRect\(er\)")),
        rect_record("xr", rects["xr"], offset, "north-east quadrant: weather and dinosaur spawn area",
                    find_lines(lines, r"GetRandomLocInRect\(xr\)")[:2]),
        rect_record("rr", rects["rr"], offset, "north-west quadrant: weather and unit enter/leave region",
                    find_lines(lines, r"set we=AddWeatherEffect\(rr")),
        rect_record("ir", rects["ir"], offset, "south quadrant: weather and dinosaur spawn area",
                    find_lines(lines, r"GetRandomLocInRect\(ir\)")[:2]),
        rect_record("no", rects["no"], offset, "spawn area of the twelve o00W units",
                    find_lines(lines, r"GetRandomLocInRect\(no\)")),
        rect_record("Ir", rects["Ir"], offset, "critter spawn area (ncrb and nhmc)",
                    find_lines(lines, r"GetRandomLocInRect\(Ir\)")[:1]),
        rect_record("Rr", rects["Rr"], offset, "spawn point of ten o00X units",
                    find_lines(lines, r"set Pe=GetRectCenter\(Rr\)")),
        rect_record("ao", rects["ao"], offset, "point the rescue helicopter is ordered to after boarding",
                    find_lines(lines, r"GetRectCenter\(ao\)")),
    ]
    teleport_globals = ["Do", "fo", "Fo", "go", "Go", "ho", "Ho", "jo", "Jo", "ko", "Ko", "lo", "Lo", "mo", "Mo",
                        "po", "Po", "qo", "Qo", "so", "So", "to", "To", "uo", "Uo", "wo", "yo", "Wo", "Yo", "zo"]
    teleport_evidence = find_lines(lines, r"^function hW takes")
    other_regions += [rect_record(name, rects[name], offset,
                                  "out-of-bounds pocket: entering it teleports the unit to a random playable "
                                  "location (trigger tR, action hW)", teleport_evidence)
                      for name in teleport_globals]

    starts = []
    for player in info["players"]:
        starts.append({
            "player": player["id"],
            "x": player["start_x"], "y": player["start_y"],
            "cell": [cell_of(player["start_x"], offset[0]), cell_of(player["start_y"], offset[1])],
            "fixedStart": bool(player["fixed_start"]),
        })

    unknown_ids = sorted(record["id"] for record in type_records.values() if record["class"] == "other")
    layout = {
        "source": "侏儸紀公園 6.3 繁體中文版 (epicwar.com/maps/200141); war3map.w3e, war3map.doo and war3map.j of "
                  "the same sample decoded by tools/original_map_layout.py. The raw files are not in this repository.",
        "map": {
            "tileset": terrain["tileset"],
            "vertices": [terrain["width"], terrain["height"]],
            "cells": [CELLS, CELLS],
            "unitsPerCell": UNITS_PER_CELL,
            "centreOffset": [offset[0], offset[1]],
            "worldBounds": {"minX": offset[0], "minY": offset[1],
                            "maxX": offset[0] + CELLS * UNITS_PER_CELL, "maxY": offset[1] + CELLS * UNITS_PER_CELL},
            "cameraBounds": {"minX": info["camera_bounds"][0], "minY": info["camera_bounds"][1],
                             "maxX": info["camera_bounds"][6], "maxY": info["camera_bounds"][3]},
            "playableCells": [info["playable_width"], info["playable_height"]],
            "groundTilesets": terrain["groundTilesets"],
            "cliffTilesets": terrain["cliffTilesets"],
        },
        "grids": {
            "rowOrder": "rows[0] is the northernmost cell row (y = 127) and characters run west to east (x = 0..127)",
            "cliffLevel": {
                "rule": "one hex digit per cell: the lowest cliff layer height of the cell's four corner vertices",
                "rows": cliff_rows},
            "water": {
                "rule": "1 when all four corner vertices carry the water flag (bit 0x4 of the vertex flag nibble)",
                "rows": water_rows},
            "groundTexture": {
                "rule": "one hex digit per cell: index into map.groundTilesets, taken from the cell's lower-left vertex",
                "rows": texture_rows},
            "passable": {
                "rule": "0 when the cell is water or its four corner vertices do not all share one cliff layer "
                        "height, that is the cell is a cliff edge; 1 otherwise. This is a terrain-only "
                        "approximation: the original also ships a separate 512 x 512 pathing map (war3map.wpm) "
                        "and doodad footprints, which this grid does not merge in.",
                "rows": passable_rows},
        },
        "doodads": {
            "count": len(placement["doodads"]),
            "specials": placement["specials"],
            "fields": ["id", "class", "x", "y", "cellX", "cellY", "blocks"],
            "rowFormat": "pipe separated; x and y are Warcraft units, cellX and cellY are cell indices, blocks is 0 or 1",
            "rows": rows,
        },
        "doodadTypes": sorted(type_records.values(), key=lambda record: record["id"]),
        "baseRects": base_rects,
        "evacuationRects": evacuation_rects,
        "otherRegions": other_regions,
        "startLocations": starts,
        "unresolved": {
            "dinosaurSpawnRectNames": "docs/original/match_flow.json names no rectangle: every spawn entry it "
                                      "records uses bj_mapInitialPlayableArea. The quadrant rectangles er, xr, "
                                      "ir listed under otherRegions are spawn areas found in war3map.j, not in "
                                      "match_flow.json, so they are reported with their own script evidence.",
            "doodadIdsClassedOther": unknown_ids,
            "unusedRects": sorted(name for name in ("io", "ar", "nr", "Vr", "Er")
                                  if name in rects and len(find_lines(lines, rf"\b{name}\b")) <= 2),
            "notes": [
                "The 0x4000 bit of the vertex water int16 marks the unplayable map border, not water: it forms "
                "the ring that matches the w3i camera complements. Water is bit 0x4 of the flag nibble.",
                "The map overrides the pathing texture of the tree walls ZTtw and ZTtc to 2x2Unflyable.tga, "
                "which blocks air only, so those 3,120 trees do not block ground movement in the original.",
                "Doodad scale and angle are decoded but not written, to keep the file small.",
            ],
        },
    }
    return layout, {"terrain": terrain, "placement": placement}


# --- preview ----------------------------------------------------------------------------------------------

def render(layout: dict, path: Path) -> None:
    from PIL import Image, ImageDraw

    size = CELLS * PIXELS_PER_CELL
    image = Image.new("RGB", (size, size))
    draw = ImageDraw.Draw(image)
    cliff = layout["grids"]["cliffLevel"]["rows"]
    water = layout["grids"]["water"]["rows"]
    passable = layout["grids"]["passable"]["rows"]

    for row in range(CELLS):
        for column in range(CELLS):
            if water[row][column] == "1":
                colour = (38, 78, 140)
            elif passable[row][column] == "0":
                colour = (22, 28, 22)
            else:
                colour = LEVEL_GREENS[min(int(cliff[row][column], 16), len(LEVEL_GREENS) - 1)]
            box = (column * PIXELS_PER_CELL, row * PIXELS_PER_CELL)
            draw.rectangle([box[0], box[1], box[0] + PIXELS_PER_CELL - 1, box[1] + PIXELS_PER_CELL - 1], fill=colour)

    classes = {record["id"]: record["class"] for record in layout["doodadTypes"]}
    dots = {"tree": (18, 64, 24), "rock": (128, 128, 128)}
    for row_text in layout["doodads"]["rows"]:
        type_id, klass, _x, _y, cell_x, cell_y, _blocks = row_text.split("|")
        colour = dots.get(klass)
        if colour is None:
            continue
        px = int(cell_x) * PIXELS_PER_CELL + 1
        py = (CELLS - 1 - int(cell_y)) * PIXELS_PER_CELL + 1
        draw.rectangle([px, py, px + 1, py + 1], fill=colour)

    def outline(rect: dict, colour: tuple[int, int, int]) -> None:
        cells = rect["cells"]
        left = cells["minX"] * PIXELS_PER_CELL
        right = (cells["maxX"] + 1) * PIXELS_PER_CELL - 1
        top = (CELLS - 1 - cells["maxY"]) * PIXELS_PER_CELL
        bottom = (CELLS - cells["minY"]) * PIXELS_PER_CELL - 1
        draw.rectangle([left - 1, top - 1, right + 1, bottom + 1], outline=colour)

    for rect in layout["evacuationRects"]:
        outline(rect, (236, 214, 58))
    for rect in layout["baseRects"]:
        outline(rect, (220, 48, 48))
    for start in layout["startLocations"]:
        px = start["cell"][0] * PIXELS_PER_CELL
        py = (CELLS - 1 - start["cell"][1]) * PIXELS_PER_CELL
        draw.ellipse([px - 1, py - 1, px + 3, py + 3], fill=(255, 255, 255))

    image.save(path, format="PNG", optimize=True)


# --- entry point ------------------------------------------------------------------------------------------

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw", type=Path, default=DEFAULT_RAW, help="directory holding war3map.w3e, .doo and .j")
    parser.add_argument("--check", action="store_true", help="fail when the committed outputs are stale")
    arguments = parser.parse_args()

    if not (arguments.raw / "war3map.w3e").exists():
        print(f"raw map files not found under {arguments.raw}; pass --raw DIR", file=sys.stderr)
        return 2

    layout, decoded = build(arguments.raw)
    text = json.dumps(layout, ensure_ascii=False, indent=1, sort_keys=False) + "\n"

    if arguments.check:
        current = OUTPUT_JSON.read_text(encoding="utf-8") if OUTPUT_JSON.exists() else ""
        if current != text:
            print(f"{OUTPUT_JSON.relative_to(ROOT)} is stale; run tools/original_map_layout.py", file=sys.stderr)
            return 1
        render(layout, OUTPUT_PNG.with_suffix(".check.png"))
        fresh = OUTPUT_PNG.with_suffix(".check.png")
        same = OUTPUT_PNG.exists() and fresh.read_bytes() == OUTPUT_PNG.read_bytes()
        fresh.unlink()
        if not same:
            print(f"{OUTPUT_PNG.relative_to(ROOT)} is stale; run tools/original_map_layout.py", file=sys.stderr)
            return 1
        print("map_layout.json and map_layout.png are up to date")
        return 0

    OUTPUT_JSON.write_text(text, encoding="utf-8")
    render(layout, OUTPUT_PNG)
    print(f"wrote {OUTPUT_JSON.relative_to(ROOT)}: {len(layout['doodads']['rows'])} doodads "
          f"({len(layout['doodadTypes'])} types) + {len(layout['doodads']['specials'])} special, "
          f"{len(layout['baseRects'])} base rects, {len(layout['evacuationRects'])} evacuation rects, "
          f"{len(layout['startLocations'])} start locations; "
          f"remaining bytes w3e {decoded['terrain']['remainingBytes']}, doo {decoded['placement']['remainingBytes']}")
    print(f"wrote {OUTPUT_PNG.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
