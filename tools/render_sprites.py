"""Render a rigged GLB into direction/animation frame PNGs for digitized sprites.

usage: blender -b -P render_sprites.py -- <model.glb> <out_dir> [--anim NAME[:frames]]... [--size 512] [--dirs 4|8]

Camera: orthographic, 3/4 view (30 degrees down), model rotated per direction.
Output: <out_dir>/<anim>/<dir>_<frame:02d>.png with transparent background.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
model, out_dir = argv[0], argv[1]
size = 512
dirs = 4
anims = []  # (name, frame_count)
i = 2
while i < len(argv):
    if argv[i] == "--anim":
        spec = argv[i + 1]
        name, _, n = spec.partition(":")
        anims.append((name, int(n) if n else 1))
        i += 2
    elif argv[i] == "--size":
        size = int(argv[i + 1]); i += 2
    elif argv[i] == "--dirs":
        dirs = int(argv[i + 1]); i += 2
    else:
        i += 1

exposure = 0.0
ambient = 0.45
j = 0
while j < len(argv):
    if argv[j] == "--exposure":
        exposure = float(argv[j + 1]); j += 2
    elif argv[j] == "--ambient":
        ambient = float(argv[j + 1]); j += 2
    else:
        j += 1

anim_files = {}
j = 0
while j < len(argv):
    if argv[j] == "--anim-file":
        k, _, v = argv[j + 1].partition("="); anim_files[k] = v; j += 2
    else:
        j += 1

# Clean scene
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
if model.lower().endswith(".fbx"):
    bpy.ops.import_scene.fbx(filepath=model, use_anim=True, ignore_leaf_bones=True, automatic_bone_orientation=False)
else:
    bpy.ops.import_scene.gltf(filepath=model)

# Extra animation FBX files (Mixamo "motion" exports): import, keep the action, delete the objects
for anim_name, path in anim_files.items():
    before = set(bpy.data.actions)
    before_objs = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, ignore_leaf_bones=True, automatic_bone_orientation=False)
    new_actions = [a for a in bpy.data.actions if a not in before]
    for a in new_actions:
        a.name = anim_name
        a.use_fake_user = True
    for o in [o for o in bpy.data.objects if o not in before_objs]:
        bpy.data.objects.remove(o, do_unlink=True)

# Find armature and meshes
armature = next((o for o in scene.objects if o.type == "ARMATURE"), None)
meshes = [o for o in scene.objects if o.type == "MESH"]
root = armature if armature else meshes[0]
while root.parent:
    root = root.parent

# Pivot: an empty that rotates the whole model
pivot = bpy.data.objects.new("Pivot", None)
scene.collection.objects.link(pivot)
root.parent = pivot

# Bounds of the rest pose
def bounds():
    lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
    dg = bpy.context.evaluated_depsgraph_get()
    for m in meshes:
        ev = m.evaluated_get(dg)
        for v in ev.data.vertices:
            w = ev.matrix_world @ v.co
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    return lo, hi

lo, hi = bounds()
center = (lo + hi) / 2
extent = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z)
pivot.location = Vector((0, 0, 0))
root.location -= Vector((center.x, center.y, lo.z))  # feet on the ground, centered

# Camera: orthographic, 30 degrees down, looking at model center
cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = extent * 1.1
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
elev = math.radians(30)
dist = extent * 4
cam.location = Vector((0, -dist * math.cos(elev), (hi.z - lo.z) / 2 + dist * math.sin(elev)))
look = Vector((0, 0, (hi.z - lo.z) / 2))
cam.rotation_euler = (look - cam.location).to_track_quat("-Z", "Y").to_euler()

# Lighting: key sun + fill, so shading has clear light/dark for quantization
# Optional flat palette override for low-poly models: --recolor Mat=r,g,b (0-1)
recolor = {}
j = 0
while j < len(argv):
    if argv[j] == "--recolor":
        k, _, v = argv[j + 1].partition("="); recolor[k] = tuple(float(x) for x in v.split(","))
        j += 2
    else:
        j += 1
for mat in bpy.data.materials:
    if mat.name in recolor and mat.use_nodes:
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (*recolor[mat.name], 1.0)
            bsdf.inputs["Roughness"].default_value = 0.9

sun = bpy.data.lights.new("Sun", "SUN"); sun.energy = 9.0
sun_o = bpy.data.objects.new("Sun", sun); scene.collection.objects.link(sun_o)
sun_o.rotation_euler = (math.radians(50), math.radians(-20), math.radians(30))
fill = bpy.data.lights.new("Fill", "SUN"); fill.energy = 1.0
fill_o = bpy.data.objects.new("Fill", fill); scene.collection.objects.link(fill_o)
fill_o.rotation_euler = (math.radians(60), math.radians(30), math.radians(-150))
world = bpy.data.worlds.new("World"); scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.35, 0.35, 0.45, 1); bg.inputs[1].default_value = ambient

# Render settings
scene.render.engine = "BLENDER_EEVEE" if hasattr(bpy.types, "SceneEEVEE") and "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = size
scene.render.resolution_y = size
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.view_settings.exposure = exposure

# Animations
actions = {a.name: a for a in bpy.data.actions}
def find_action(name):
    for k, a in actions.items():
        if k.lower() == name.lower() or k.lower().endswith("|" + name.lower()):
            return a
    return None

dir_names = ["front", "right", "back", "left"] if dirs == 4 else ["s", "sw", "w", "nw", "n", "ne", "e", "se"]
for anim_name, nframes in anims:
    act = find_action(anim_name)
    target = armature if armature else root
    if act:
        if not target.animation_data:
            target.animation_data_create()
        target.animation_data.action = act
        f0, f1 = act.frame_range
    else:
        f0, f1 = 1, 1
    os.makedirs(os.path.join(out_dir, anim_name), exist_ok=True)
    for d, dname in enumerate(dir_names):
        pivot.rotation_euler = (0, 0, math.radians(d * 360 / dirs))
        for k in range(nframes):
            frame = f0 + (f1 - f0) * k / max(nframes, 1) if nframes > 1 else f0
            scene.frame_set(int(round(frame)))
            scene.render.filepath = os.path.join(out_dir, anim_name, f"{dname}_{k:02d}.png")
            bpy.ops.render.render(write_still=True)
print("DONE", [a for a in actions], "extent", extent)
