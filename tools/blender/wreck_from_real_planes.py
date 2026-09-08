"""Pose real plane meshes as burned wrecks.

The box-built fighter/transport did not read as aircraft on device
(Rob, 2026-09-08). These keep the same export names so the level
plants do not change.

  ~/blender/blender-5.1.2-linux-x64/blender --background --python \\
      tools/blender/wreck_from_real_planes.py
"""
from __future__ import annotations

import math
import os

import bpy
from mathutils import Vector

ROOT = "/home/rob/UnityProjects/ArmedConflictSpike"
OUT = os.path.join(ROOT, "Assets", "Models")
CC0 = os.path.join(ROOT, "tools", "blender", "cc0_planes")
JET = os.path.join(CC0, "Jetliner.obj")


def mat(name, color):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    if "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = 0.95
    if "Metallic" in bsdf.inputs:
        bsdf.inputs["Metallic"].default_value = 0.15
    return m


def clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for item in list(block):
            block.remove(item)


def meshes():
    return [o for o in bpy.data.objects if o.type == "MESH"]


def paint_all(char, rust, hole):
    ms = meshes()
    for i, o in enumerate(ms):
        o.data.materials.clear()
        # Alternate rust on smaller bits so the hulk isn't one grey blob.
        name = o.name.lower()
        if any(k in name for k in ("canopy", "glass", "window", "cockpit")):
            o.data.materials.append(hole)
        elif i % 3 == 1 or "engine" in name or "fan" in name or "wheel" in name:
            o.data.materials.append(rust)
        else:
            o.data.materials.append(char)


def join_all(name):
    ms = meshes()
    if not ms:
        return None
    for o in ms:
        if o.parent:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
    bpy.ops.object.select_all(action="DESELECT")
    for o in ms:
        o.select_set(True)
    bpy.context.view_layer.objects.active = ms[0]
    if len(ms) > 1:
        bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o


def ground(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    mw = obj.matrix_world
    zs = [(mw @ v.co).z for v in obj.data.vertices]
    xs = [(mw @ v.co).x for v in obj.data.vertices]
    ys = [(mw @ v.co).y for v in obj.data.vertices]
    if not zs:
        return
    dz = min(zs)
    obj.location.z -= dz
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    print(f"  {obj.name} size x={max(xs)-min(xs):.2f} y={max(ys)-min(ys):.2f} z={max(zs)-min(zs):.2f}")


def scale_longest(obj, units):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    mw = obj.matrix_world
    pts = [mw @ v.co for v in obj.data.vertices]
    dx = max(p.x for p in pts) - min(p.x for p in pts)
    dy = max(p.y for p in pts) - min(p.y for p in pts)
    dz = max(p.z for p in pts) - min(p.z for p in pts)
    longest = max(dx, dy, dz)
    if longest < 1e-4:
        return
    s = units / longest
    obj.scale = (s, s, s)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)


def export(name):
    path = os.path.join(OUT, f"{name}.glb")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_cameras=False,
        export_lights=False,
    )
    print(f"exported {path} ({os.path.getsize(path)} bytes)")


def wreck_airliner(name, yaw, longest):
    """CC0 wide-body, sitting on the dirt. Rob: use the plane behind the hangar."""
    bpy.ops.wm.obj_import(filepath=JET)
    char = mat("t_char", (0.20, 0.18, 0.15))
    rust = mat("t_rust", (0.50, 0.28, 0.11))
    hole = mat("t_hole", (0.05, 0.04, 0.04))
    paint_all(char, rust, hole)
    o = join_all(name)
    o.rotation_mode = "XYZ"
    o.rotation_euler = (0.04, 0.05, yaw)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    scale_longest(o, longest)
    ground(o)


def main():
    os.makedirs(OUT, exist_ok=True)
    clear()
    wreck_airliner("wreck_fighter", yaw=0.55, longest=3.8)
    export("prop_wreck_fighter")
    clear()
    wreck_airliner("wreck_transport", yaw=-0.50, longest=4.4)
    export("prop_wreck_transport")


if __name__ == "__main__":
    main()
