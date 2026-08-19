"""Tall player-side flank — fills the aim-frame dirt, not the horizon.

The aiming camera frames the player line only (~6 wide, camZ ~8). Backdrop
strips sit at z = -30 and cannot fill the tan ground between the soldiers
and the mountains. This prop is play-space scenery: origin at the base,
~4 tall, ~1.3 wide, camera face is Blender −Y.

Colour prefixes: body (earth), accent_ (bags / timber). Runtime tones
props with the player structure materials.

Headless:
  ~/blender/blender-5.1.2-linux-x64/blender --background --python \\
      tools/blender/build_prop_flank.py
"""
from __future__ import annotations

import math
import os
import sys

import bpy

OUT = "/home/rob/UnityProjects/ArmedConflictSpike/Assets/Models/prop_flank.glb"


def mat(name, color):
    m = bpy.data.materials.new(name=name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    if "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = 0.92
    return m


def box(dims, loc, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.active_object
    obj.dimensions = dims
    if rot is not None:
        obj.rotation_euler = rot
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def join(objs, name, material):
    objs = [o for o in objs if o is not None]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    merged = bpy.context.active_object
    merged.name = name
    merged.data.materials.clear()
    merged.data.materials.append(material)
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return merged


def clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def build():
    earth = mat("earth", (0.38, 0.32, 0.22))
    bag = mat("bag", (0.42, 0.40, 0.30))

    # Sloped berm, not a tower. Longest axis is HEIGHT ~2.15 so
    # Normalize(scale) ≈ world height. Camera face at y ≈ 0.
    berm = [
        box((2.10, 1.35, 0.70), (0.00, 0.40, 0.35)),
        box((1.70, 1.10, 0.65), (0.04, 0.28, 0.95)),
        box((1.25, 0.82, 0.55), (-0.05, 0.18, 1.50)),
        box((0.80, 0.55, 0.40), (0.06, 0.10, 1.92)),
    ]
    join(berm, "body_berm", earth)

    bags = []
    for i, (z, n, w) in enumerate(((0.18, 5, 1.85), (0.42, 5, 1.65),
                                   (0.88, 4, 1.30), (1.28, 3, 0.95))):
        pitch = w / n
        for k in range(n):
            x = -w / 2 + pitch * (k + 0.5)
            bags.append(box((pitch * 0.86, 0.20, 0.18),
                            (x + (0.02 if (i + k) % 2 else -0.02),
                             -0.04, z)))
    bags.append(box((0.12, 0.12, 1.15), (-0.55, 0.06, 1.15)))
    join(bags, "accent_bags", bag)


def export():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB")
    print(f"exported {OUT}")


if __name__ == "__main__":
    argv = sys.argv
    if "--" in argv:
        rest = argv[argv.index("--") + 1:]
        if rest:
            OUT = rest[0]
    clear()
    build()
    export()
