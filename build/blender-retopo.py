# PLATE: blood bag retopology (headless Blender 4.x+)
# Usage: blender -b --python build/blender-retopo.py -- <source.glb> [output_dir]
# Pipeline: import GLB (mesh + textures) -> low-poly copy (~24k tris, geometry
# decimate) -> NEW clean UV layout (Smart UV Project) -> rebake albedo + normal
# from the original onto the new UVs (Cycles bake) -> export FBX + PNG into the
# Unity project.
import math
import os
import sys

import bpy

# arguments after the "--" separator; the output directory defaults to the
# Unity project next to this script
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if not argv:
    raise SystemExit(
        "usage: blender -b --python build/blender-retopo.py -- <source.glb> [output_dir]")

GLB = argv[0]
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = argv[1] if len(argv) > 1 else os.path.join(REPO_ROOT, "unity", "Assets", "PLATE")
FBX_OUT = os.path.join(OUT_DIR, "Models", "blood_bag_retopo.fbx")
ALBEDO_OUT = os.path.join(OUT_DIR, "Textures", "blood_bag_baked_albedo.png")
NORMAL_OUT = os.path.join(OUT_DIR, "Textures", "blood_bag_baked_normal.png")
TARGET_TRIS = 24000
BAKE_SIZE = 2048
CAGE_EXTRUSION = 0.02  # metres: bake cage clearance above the surface

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
assert meshes, "GLB has no meshes"
high = meshes[0]
high.name = "blood_bag_high"
src_tris = len(high.data.loop_triangles) or sum(
    max(len(p.vertices) - 2, 0) for p in high.data.polygons)
print(f"[PLATE] high-poly: {src_tris} tris")

# --- low-poly copy: decimate the geometry, discard the UVs ---
low = high.copy()
low.data = high.data.copy()
low.name = "blood_bag"  # object name = mesh name in the FBX
bpy.context.collection.objects.link(low)

mod = low.modifiers.new("dec", "DECIMATE")
mod.ratio = TARGET_TRIS / max(src_tris, 1)
bpy.context.view_layer.objects.active = low
bpy.ops.object.modifier_apply(modifier="dec")

# fresh unwrap on top of the simplified geometry
bpy.ops.object.select_all(action="DESELECT")
low.select_set(True)
bpy.context.view_layer.objects.active = low
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003)
bpy.ops.object.mode_set(mode="OBJECT")

# --- low-poly material with the bake target maps ---
img_d = bpy.data.images.new("bake_albedo", BAKE_SIZE, BAKE_SIZE, alpha=False)
img_n = bpy.data.images.new("bake_normal", BAKE_SIZE, BAKE_SIZE, alpha=False)
img_n.colorspace_settings.name = "Non-Color"

mat = bpy.data.materials.new("blood_bag_baked")
mat.use_nodes = True
low.data.materials.clear()
low.data.materials.append(mat)
nt = mat.node_tree
node_d = nt.nodes.new("ShaderNodeTexImage")
node_d.image = img_d
node_n = nt.nodes.new("ShaderNodeTexImage")
node_n.image = img_n

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 32
scene.render.bake.use_pass_direct = False    # albedo colour only,
scene.render.bake.use_pass_indirect = False  # no lighting or shadows
scene.render.bake.margin = 8

def bake(node, bake_type):
    bpy.ops.object.select_all(action="DESELECT")
    high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    nt.nodes.active = node
    bpy.ops.object.bake(type=bake_type, use_selected_to_active=True,
                        cage_extrusion=CAGE_EXTRUSION, use_clear=True)

print("[PLATE] baking albedo...")
bake(node_d, "DIFFUSE")
img_d.filepath_raw = ALBEDO_OUT
img_d.file_format = "PNG"
img_d.save()

print("[PLATE] baking normal...")
bake(node_n, "NORMAL")
img_n.filepath_raw = NORMAL_OUT
img_n.file_format = "PNG"
img_n.save()

# --- export: low-poly only ---
bpy.data.objects.remove(high, do_unlink=True)
bpy.ops.object.select_all(action="DESELECT")
low.select_set(True)
os.makedirs(os.path.dirname(FBX_OUT), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=FBX_OUT, use_selection=True,
                         apply_scale_options="FBX_SCALE_ALL", path_mode="STRIP")
tris = len(low.data.loop_triangles) or sum(
    max(len(p.vertices) - 2, 0) for p in low.data.polygons)
print(f"[PLATE] RETOPO OK: {tris} tris -> {FBX_OUT}")
