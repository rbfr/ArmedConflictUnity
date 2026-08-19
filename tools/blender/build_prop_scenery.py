"""Play-space scenery: things that ARE something, not stacked boxes.

Camera sees Blender −Y (Unity +Z). X is the battlefield. Z is up.
Origin at the base centre. Do not Normalize in here — LevelScenery does.

Colour is authored and must survive: set PropPlacement.keepColors.
Prefixes still follow the house rule so a keepColors=false plant
does not explode (body / accent_ / trim_).

Headless:
  ~/blender/blender-5.1.2-linux-x64/blender --background --python \\
      tools/blender/build_prop_scenery.py
"""
from __future__ import annotations

import math
import os
import sys

import bpy
from mathutils import Vector

OUT_DIR = "/home/rob/UnityProjects/ArmedConflictSpike/Assets/Models"


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


def cyl(radius, depth, loc, rot=None, verts=8):
    bpy.ops.mesh.primitive_cylinder_add(
        radius=radius, depth=depth, vertices=verts, location=loc
    )
    obj = bpy.context.active_object
    bpy.ops.object.shade_flat()
    if rot is not None:
        obj.rotation_euler = rot
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return obj


def join(objs, name, material, origin=(0.0, 0.0, 0.0)):
    objs = [o for o in objs if o is not None]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    merged = bpy.context.active_object
    merged.name = name
    merged.data.materials.clear()
    merged.data.materials.append(material)
    bpy.context.scene.cursor.location = origin
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return merged


def clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def export(name):
    path = os.path.join(OUT_DIR, f"{name}.glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB")
    print(f"exported {path}")


def build_wreck_car():
    """Two-box hatchback, burned. Cabin is the same WIDTH as the
    hull — a crate sitting on a deck was the last read. Hood is a
    short low volume; wheels are large and sit in notches. 28° yaw
    so the 6° camera sees a 3/4, not a lid."""
    rust = mat("rust", (0.50, 0.26, 0.10))
    char = mat("char", (0.15, 0.13, 0.12))
    hole = mat("hole", (0.03, 0.03, 0.03))
    tyre = mat("tyre", (0.06, 0.05, 0.05))

    # Lower hull sits HIGH so the wheels break the outline.
    hull = [
        box((2.10, 0.95, 0.38), (0.05, 0.00, 0.62)),           # main body
        box((0.70, 0.95, 0.22), (0.95, 0.00, 0.78)),           # hood, lower
        box((0.22, 0.90, 0.16), (1.38, 0.00, 0.62), (0, 0, 0.4)),  # nose
    ]
    join(hull, "body_hull", char)

    # Cabin = the rear two-thirds, SAME width as the hull.
    cabin = [
        box((1.25, 0.95, 0.72), (-0.28, 0.00, 1.16)),
        box((0.55, 0.93, 0.50), (0.48, 0.00, 1.08), (0, 0, 0.62)),  # screen rake
    ]
    join(cabin, "body_cabin", char)

    rust_bits = [
        box((0.55, 0.98, 0.10), (0.90, 0.00, 0.92)),           # hood rust
        box((0.18, 0.98, 0.08), (1.48, -0.08, 0.36), (0.5, 0, 0.3)),  # bumper
        box((0.30, 0.20, 0.12), (-0.20, -0.50, 0.70)),         # hanging door
    ]
    join(rust_bits, "accent_rust", rust)

    glass = [
        box((0.48, 0.78, 0.40), (0.22, -0.02, 1.16)),          # screen — big
        box((0.55, 0.78, 0.38), (-0.35, -0.02, 1.18)),         # side
    ]
    join(glass, "accent_glass", hole)

    wheels = []
    for x, y, zz, lean in (
        (0.72, 0.50, 0.28, 0.0),
        (0.72, -0.50, 0.28, 0.0),
        (-0.72, 0.50, 0.28, 0.0),
        (-0.72, -0.50, 0.18, 0.4),
    ):
        wheels.append(cyl(0.28, 0.18, (x, y, zz),
                          rot=(1.5708, 0, lean), verts=8))
    join(wheels, "accent_wheels", tyre)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.join()
    car = bpy.context.active_object
    car.name = "wreck_car"
    car.rotation_euler = (0.0, 0.0, 0.48)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")


def build_cactus():
    """Saguaro. Vertical is the whole point."""
    green = mat("cactus", (0.28, 0.42, 0.18))
    dark = mat("cactus_dark", (0.18, 0.28, 0.12))

    trunk = [
        cyl(0.22, 2.20, (0.00, 0.00, 1.12), verts=8),
        cyl(0.18, 0.55, (0.00, 0.00, 2.38), verts=8),
    ]
    join(trunk, "body_trunk", green)

    arms = [
        # Left arm: out then up.
        cyl(0.12, 0.55, (-0.38, 0.00, 1.35), rot=(0, 1.15, 0), verts=7),
        cyl(0.11, 0.70, (-0.58, 0.00, 1.82), verts=7),
        # Right arm, lower.
        cyl(0.11, 0.42, (0.32, 0.00, 1.05), rot=(0, -1.20, 0), verts=7),
        cyl(0.10, 0.48, (0.50, 0.00, 1.38), verts=7),
    ]
    join(arms, "accent_arms", dark)


def build_dead_tree():
    """Blasted pine. Trunk + three broken limbs, no canopy."""
    bark = mat("bark", (0.22, 0.16, 0.11))
    char = mat("charwood", (0.10, 0.09, 0.08))

    trunk = [
        cyl(0.16, 2.35, (0.04, 0.00, 1.18), rot=(0.08, 0, 0.06), verts=7),
        cyl(0.11, 0.70, (-0.02, 0.02, 2.50), rot=(0.15, 0, -0.20), verts=6),
    ]
    join(trunk, "body_trunk", bark)

    limbs = [
        cyl(0.07, 0.85, (0.42, 0.05, 1.70), rot=(0, 1.05, 0.3), verts=6),
        cyl(0.06, 0.55, (-0.38, -0.04, 2.05), rot=(0.2, -1.15, 0), verts=6),
        cyl(0.05, 0.40, (0.18, 0.08, 2.55), rot=(0.4, 0.6, 0), verts=5),
        cyl(0.05, 0.32, (-0.12, -0.06, 1.35), rot=(0.1, -0.9, 0.4), verts=5),
    ]
    join(limbs, "accent_limbs", char)


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    builders = (
        ("prop_wreck_car", build_wreck_car),
        ("prop_cactus", build_cactus),
        ("prop_dead_tree", build_dead_tree),
    )
    for name, fn in builders:
        clear()
        fn()
        export(name)
