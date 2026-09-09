"""Pose real plane meshes as burned wrecks.

The box-built fighter/transport did not read as aircraft on device
(Rob, 2026-09-08). These keep the same export names so the level
plants do not change.

Apron hulks are modeled in build_wreck_planes.py (a hull, not a
deleted airliner). This file only exports the parked hangar-bay jet.

  ~/blender/blender-5.1.2-linux-x64/blender --background --python \\
      tools/blender/wreck_from_real_planes.py
"""
from __future__ import annotations

import math
import os
import random

import bmesh
import bpy
from mathutils import Vector, noise

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
        bsdf.inputs["Metallic"].default_value = 0.08
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


def bounds(bm):
    xs = [v.co.x for v in bm.verts]
    ys = [v.co.y for v in bm.verts]
    zs = [v.co.z for v in bm.verts]
    return min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)


def copy_chunk(src, verts_ok, name):
    """A fallen wing/tail panel next to the hull. Small roll, still on the dirt."""
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.verts.ensure_lookup_table()
    kill = [v for v in bm.verts if not verts_ok(v.co)]
    if kill:
        bmesh.ops.delete(bm, geom=kill, context="VERTS")
    if len(bm.verts) < 8:
        bm.free()
        return None
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.clear()
    if src.data.materials:
        o.data.materials.append(src.data.materials[0])
    return o


def damage_mesh(obj, seed, break_plus_x):
    """Break a wing, bite the tail, open holes, crumple. Stays a hull on the dirt."""
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    x0, x1, y0, y1, z0, z1 = bounds(bm)
    dx, dy, dz = x1 - x0, y1 - y0, z1 - z0
    y_mid = (y0 + y1) * 0.5
    z_hi_pos = max((v.co.z for v in bm.verts if v.co.y > y_mid), default=z0)
    z_hi_neg = max((v.co.z for v in bm.verts if v.co.y < y_mid), default=z0)
    tail_high_y = z_hi_pos >= z_hi_neg

    rng = random.Random(seed)

    # Half the wing gone — the 6° silhouette has to stop being a plane.
    wing_cut = (x0 + dx * 0.52) if break_plus_x else (x1 - dx * 0.52)
    tail_cut = (y1 - dy * 0.18) if tail_high_y else (y0 + dy * 0.18)

    def on_broken_wing(co):
        if break_plus_x:
            return co.x > wing_cut + dx * 0.08
        return co.x < wing_cut - dx * 0.08

    debris = copy_chunk(obj, on_broken_wing, obj.name + "_wing")

    kill = []
    for v in bm.verts:
        if break_plus_x and v.co.x > wing_cut:
            kill.append(v)
        elif (not break_plus_x) and v.co.x < wing_cut:
            kill.append(v)
        elif tail_high_y and v.co.y > tail_cut:
            kill.append(v)
        elif (not tail_high_y) and v.co.y < tail_cut:
            kill.append(v)
    if kill:
        bmesh.ops.delete(bm, geom=kill, context="VERTS")
        bm.verts.ensure_lookup_table()
        bm.faces.ensure_lookup_table()

    hole_faces = []
    for f in bm.faces:
        c = f.calc_center_median()
        nx = abs((c.x - (x0 + x1) * 0.5) / max(dx, 1e-4))
        ny = (c.y - y0) / max(dy, 1e-4)
        nz = (c.z - z0) / max(dz, 1e-4)
        in_cabin = nx < 0.18 and 0.22 < ny < 0.78 and 0.32 < nz < 0.92
        blast = nx < 0.28 and 0.44 < ny < 0.66 and 0.15 < nz < 0.78
        if in_cabin or blast:
            hole_faces.append(f)
    if hole_faces:
        bmesh.ops.delete(bm, geom=hole_faces, context="FACES")
        bm.verts.ensure_lookup_table()

    x0, x1, y0, y1, z0, z1 = bounds(bm)
    dx, dy, dz = max(x1 - x0, 1e-4), max(y1 - y0, 1e-4), max(z1 - z0, 1e-4)
    for v in bm.verts:
        n = noise.noise(v.co * (1.8 / dy) + Vector((seed * 0.37, 0.2, -0.1)))
        side = (v.co.x - x0) / dx
        if not break_plus_x:
            side = 1.0 - side
        k = 0.05 * dy * (0.45 + 0.9 * side)
        v.co += Vector((n * k * 0.35, n * k * 0.12, n * k * 0.55))
        along = (v.co.y - y0) / dy
        if abs(v.co.x - (x0 + x1) * 0.5) < dx * 0.22 and v.co.z > z0 + dz * 0.48:
            if 0.20 < along < 0.80:
                v.co.z -= dz * (0.16 + 0.05 * rng.random())

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    if debris is not None:
        # Fallen panel beside the broken stub, small roll — not the 80°
        # whole-plane flop that was rejected.
        sign = 1.0 if break_plus_x else -1.0
        debris.rotation_mode = "XYZ"
        debris.rotation_euler = (0.35 * sign, 0.15, 0.25 * sign)
        debris.location = Vector((sign * dx * 0.12, dy * 0.05, 0.0))
        bpy.ops.object.select_all(action="DESELECT")
        debris.select_set(True)
        bpy.context.view_layer.objects.active = debris
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.join()


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


def wreck_airliner(name, yaw, longest, *, damage, break_plus_x=True, seed=1, olive=False):
    """CC0 wide-body, sitting on the dirt. Rob: use the plane behind the hangar."""
    bpy.ops.wm.obj_import(filepath=JET)
    if olive:
        char = mat("t_char", (0.36, 0.44, 0.28))
        rust = mat("t_rust", (0.48, 0.32, 0.16))
        hole = mat("t_hole", (0.06, 0.06, 0.05))
    else:
        # Darker than the first pass — that charcoal still read as a grey airliner.
        char = mat("t_char", (0.11, 0.10, 0.09))
        rust = mat("t_rust", (0.46, 0.22, 0.08))
        hole = mat("t_hole", (0.04, 0.03, 0.03))
    paint_all(char, rust, hole)
    o = join_all(name)
    if damage:
        damage_mesh(o, seed=seed, break_plus_x=break_plus_x)
    o.rotation_mode = "XYZ"
    o.rotation_euler = (0.04, 0.05, yaw)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    scale_longest(o, longest)
    ground(o)


def main():
    os.makedirs(OUT, exist_ok=True)
    clear()
    wreck_airliner("wreck_fighter_bay", yaw=0.55, longest=3.8,
                   damage=False, olive=True)
    export("prop_wreck_fighter_bay")


if __name__ == "__main__":
    main()
