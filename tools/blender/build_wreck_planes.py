"""One airliner, broken in half, burned at the snap.

The 6° camera has to see a PLANE that crashed — not missing faces, not
a pile of boxes. Import the CC0 jetliner, bisect the fuselage, fill the
cut with a charred cap, pull the tail off-axis. Belly on the tarmac.

  ~/blender/blender-5.1.2-linux-x64/blender --background --python \\
      tools/blender/build_wreck_planes.py

Bay parked jet stays wreck_from_real_planes.py.
build_airport.py must not overwrite these glbs.
"""
from __future__ import annotations

import math
import os

import bmesh
import bpy
from mathutils import Vector

OUT = "/home/rob/UnityProjects/ArmedConflictSpike/Assets/Models"
JET = os.path.join(
    "/home/rob/UnityProjects/ArmedConflictSpike/tools/blender/cc0_planes",
    "Jetliner.obj",
)


def mat(name, color):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    if "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = 0.94
    m.diffuse_color = (*color, 1.0)
    return m


def apply_all(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def wpts(objs):
    pts = []
    for o in objs:
        mw = o.matrix_world
        pts.extend(mw @ Vector(v.co) for v in o.data.vertices)
    return pts


def clear_meshes():
    for o in list(bpy.data.objects):
        if o.type in ("MESH", "EMPTY"):
            bpy.data.objects.remove(o, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


CUT_X = 0.05


def half(src, keep_plus, name, char, burn):
    """Keep one side of the snap. Cap the cut with the fuselage's own
    outline — a floating disc is how the last pass missed the hull."""
    d = src.copy()
    d.data = src.data.copy()
    bpy.context.collection.objects.link(d)
    d.name = name
    d.data.materials.clear()
    d.data.materials.append(char)
    d.data.materials.append(burn)
    bm = bmesh.new()
    bm.from_mesh(d.data)
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    ret = bmesh.ops.bisect_plane(
        bm, geom=geom, dist=0.0001,
        plane_co=Vector((CUT_X, 0.0, 0.0)), plane_no=Vector((1.0, 0.0, 0.0)),
        clear_inner=keep_plus, clear_outer=not keep_plus)
    cut_edges = [e for e in ret.get("geom_cut", []) if isinstance(e, bmesh.types.BMEdge)]
    if cut_edges:
        bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)
    bm.faces.ensure_lookup_table()
    bm.normal_update()
    for f in bm.faces:
        c = f.calc_center_median()
        on_cut = abs(c.x - CUT_X) < 0.03
        nx = f.normal.x
        if on_cut and abs(nx) > 0.45:
            f.material_index = 1
            # Outward: nose looks -X, tail looks +X.
            if keep_plus and nx > 0:
                f.normal_flip()
            if (not keep_plus) and nx < 0:
                f.normal_flip()
        else:
            f.material_index = 0
    bm.to_mesh(d.data)
    bm.free()
    d.data.update()
    bpy.ops.object.select_all(action="DESELECT")
    d.select_set(True)
    bpy.context.view_layer.objects.active = d
    bpy.ops.object.shade_flat()
    return d


def export_crash(filename, yaw):
    bpy.ops.wm.obj_import(filepath=JET)
    parts = [o for o in bpy.data.objects if o.type == "MESH"]
    for o in parts:
        apply_all(o)

    pts = wpts(parts)
    z0 = min(p.z for p in pts)
    for o in parts:
        o.location.z -= z0
        apply_all(o)

    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.context.scene.tool_settings.transform_pivot_point = "CURSOR"
    bpy.ops.transform.rotate(value=-math.pi / 2, orient_axis="Z", orient_type="GLOBAL")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    pts = wpts(parts)
    dx = max(p.x for p in pts) - min(p.x for p in pts)
    dy = max(p.y for p in pts) - min(p.y for p in pts)
    dz = max(p.z for p in pts) - min(p.z for p in pts)
    s = 4.5 / max(dx, dy, dz)
    for o in parts:
        o.scale = (s, s, s)
        apply_all(o)
    pts = wpts(parts)
    cx = (max(p.x for p in pts) + min(p.x for p in pts)) / 2
    cy = (max(p.y for p in pts) + min(p.y for p in pts)) / 2
    z0 = min(p.z for p in pts)
    for o in parts:
        o.location -= Vector((cx, cy, z0))
        apply_all(o)

    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    hull = bpy.context.active_object
    hull.name = "hull"

    char = mat("w_char", (0.14, 0.12, 0.10))
    burn = mat("w_burn", (0.04, 0.03, 0.025))
    nose = half(hull, True, "crash_nose", char, burn)
    tail = half(hull, False, "crash_tail", char, burn)
    bpy.data.objects.remove(hull, do_unlink=True)

    nose.location.x += 0.55
    tail.location.x -= 0.90
    tail.location.y += 0.50

    pts = [tail.matrix_world @ Vector(v.co) for v in tail.data.vertices]
    cx = sum(p.x for p in pts) / len(pts)
    cy = sum(p.y for p in pts) / len(pts)
    cz = sum(p.z for p in pts) / len(pts)
    h = bpy.data.objects.new("tyaw", None)
    bpy.context.collection.objects.link(h)
    h.location = (cx, cy, cz)
    bpy.ops.object.select_all(action="DESELECT")
    tail.select_set(True)
    h.select_set(True)
    bpy.context.view_layer.objects.active = h
    bpy.ops.object.parent_set(type="OBJECT", keep_transform=True)
    h.rotation_euler = (0.08, 0.0, 0.32)
    bpy.ops.object.select_all(action="DESELECT")
    tail.select_set(True)
    bpy.context.view_layer.objects.active = tail
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.data.objects.remove(h, do_unlink=True)

    live = [nose, tail]
    pts = wpts(live)
    z0 = min(p.z for p in pts)
    for o in live:
        o.location.z -= z0
        apply_all(o)

    bpy.ops.object.select_all(action="DESELECT")
    for o in live:
        o.select_set(True)
    bpy.context.view_layer.objects.active = nose
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.transform.rotate(value=yaw, orient_axis="Z", orient_type="GLOBAL")
    bpy.ops.object.join()
    plane = bpy.context.active_object
    plane.name = filename
    apply_all(plane)
    pts = [plane.matrix_world @ Vector(v.co) for v in plane.data.vertices]
    plane.location.z -= min(p.z for p in pts)
    apply_all(plane)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

    path = os.path.join(OUT, f"{filename}.glb")
    bpy.ops.object.select_all(action="DESELECT")
    plane.select_set(True)
    bpy.context.view_layer.objects.active = plane
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True,
        export_apply=True, export_cameras=False, export_lights=False)
    print(f"exported {path}")


def main():
    os.makedirs(OUT, exist_ok=True)
    clear_meshes()
    export_crash("prop_wreck_fighter", 0.55)


if __name__ == "__main__":
    main()
