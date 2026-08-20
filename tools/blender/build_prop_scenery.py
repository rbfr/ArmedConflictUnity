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


def cone(radius, depth, loc, rot=None, verts=7):
    bpy.ops.mesh.primitive_cone_add(
        radius1=radius, radius2=0.0, depth=depth, vertices=verts, location=loc
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


def build_pine():
    """Living pine. Three overlapping skirts so the 6° camera sees a
    notched outline, not one cone. Darker than Forest ground so it
    silhouettes — a mid-green pine on mid-green dirt vanishes."""
    bark = mat("bark", (0.22, 0.16, 0.10))
    needle = mat("needle", (0.14, 0.22, 0.10))
    dark = mat("needle_dark", (0.10, 0.16, 0.08))

    join([
        cyl(0.13, 1.55, (0.00, 0.00, 0.78), verts=7),
        cyl(0.08, 0.55, (0.02, 0.00, 1.70), rot=(0.08, 0, 0.04), verts=6),
    ], "body_trunk", bark)

    # Offset in X so the 6° outline is notched, not one triangle.
    join([
        cone(0.78, 1.20, (-0.16, 0.04, 1.32), verts=7),
        cone(0.55, 1.05, (0.18, -0.02, 1.95), verts=7),
    ], "accent_skirt", needle)
    join([
        cone(0.34, 0.95, (-0.04, 0.03, 2.48), verts=6),
    ], "trim_top", dark)


def build_stump():
    """Thick remnant + one broken limb. Height is the longest axis so
    Normalize does not flatten it into a log."""
    bark = mat("bark", (0.28, 0.18, 0.10))
    char = mat("charwood", (0.10, 0.08, 0.07))

    join([
        cyl(0.32, 1.15, (0.00, 0.00, 0.58), verts=8),
        cyl(0.22, 0.40, (0.04, 0.02, 1.22), rot=(0.15, 0, 0.10), verts=7),
    ], "body_trunk", bark)
    join([
        cyl(0.09, 0.70, (0.48, 0.04, 0.95), rot=(0, 1.10, 0.2), verts=6),
        cyl(0.07, 0.38, (-0.28, -0.04, 1.15), rot=(0.2, -0.9, 0), verts=5),
        box((0.55, 0.50, 0.12), (0.02, 0.00, 1.42), (0.2, 0.1, 0.3)),
    ], "accent_break", char)


def build_boulder():
    """Standing rock. Height wins so it is a mass, not a pancake."""
    rock = mat("rock", (0.32, 0.30, 0.28))
    dark = mat("rock_dark", (0.18, 0.17, 0.16))

    join([
        box((1.15, 0.95, 1.10), (0.00, 0.08, 0.55), (0.08, 0.05, 0.18)),
        box((0.85, 0.78, 0.95), (0.12, -0.06, 1.25), (0.12, -0.08, -0.15)),
        box((0.55, 0.52, 0.70), (-0.10, 0.10, 1.90), (0.18, 0.10, 0.22)),
    ], "body_rock", rock)
    join([
        box((0.40, 0.95, 0.55), (-0.42, 0.02, 0.70), (0.2, 0, 0.3)),
        box((0.28, 0.40, 0.45), (0.38, -0.20, 1.45), (0.4, 0.2, -0.2)),
    ], "accent_split", dark)


def build_rubble():
    """Broken masonry slab. Darker than CityRuins ground or it is a
    grey object on grey dirt."""
    brick = mat("masonry", (0.26, 0.22, 0.18))
    void = mat("void", (0.10, 0.09, 0.08))

    join([
        box((0.95, 0.38, 1.55), (0.00, 0.00, 0.78)),
        box((0.55, 0.36, 1.10), (0.42, 0.04, 1.15), (0, 0, 0.35)),
        box((0.40, 0.34, 0.70), (-0.38, -0.02, 1.55), (0.1, 0, -0.25)),
    ], "body_wall", brick)
    join([
        box((0.50, 0.40, 0.45), (0.12, 0.02, 1.35)),
        box((0.32, 0.40, 0.55), (-0.22, 0.00, 0.95)),
        box((0.55, 0.28, 0.22), (0.55, 0.10, 0.18), (0, 0.4, 0.5)),
        box((0.35, 0.22, 0.18), (-0.45, -0.08, 0.14), (0.2, 0, 0.4)),
    ], "accent_bite", void)


def build_lamp():
    """Street lamp. Vertical is the whole point; arm faces the camera
    (Blender −Y) so the 6° view is not a pole."""
    metal = mat("metal", (0.12, 0.12, 0.13))
    hood = mat("hood", (0.22, 0.18, 0.08))

    join([
        cyl(0.07, 2.35, (0.00, 0.00, 1.18), verts=6),
        box((0.18, 0.18, 0.12), (0.00, 0.00, 0.08)),
    ], "body_pole", metal)
    join([
        box((0.10, 0.55, 0.10), (0.00, -0.28, 2.32)),
        box((0.22, 0.28, 0.14), (0.00, -0.62, 2.22)),
        box((0.16, 0.16, 0.10), (0.00, -0.62, 2.10)),
    ], "accent_lamp", hood)


def build_barrel_cactus():
    """Squat barrel. Different silhouette from the saguaro — fat, not
    armed. Height still wins Normalize."""
    green = mat("cactus", (0.34, 0.38, 0.14))
    dark = mat("cactus_dark", (0.22, 0.26, 0.10))

    join([
        cyl(0.42, 1.35, (0.00, 0.00, 0.68), verts=8),
        cyl(0.28, 0.35, (0.00, 0.00, 1.42), verts=8),
        cyl(0.16, 0.55, (-0.48, 0.00, 0.55), verts=7),
        cyl(0.14, 0.42, (0.46, 0.04, 0.85), verts=7),
    ], "body_barrel", green)
    join([
        box((0.08, 0.86, 1.10), (0.00, 0.00, 0.70)),
        box((0.86, 0.08, 1.10), (0.00, 0.00, 0.70)),
        cyl(0.08, 0.22, (0.10, 0.04, 1.65), verts=5),
    ], "accent_ribs", dark)


def build_snow_pine():
    """Winter pine. Darker and tighter than the forest pine — two
    skirts, no snow cap. A white slab at 6° is a white object."""
    bark = mat("bark", (0.16, 0.12, 0.08))
    needle = mat("needle", (0.08, 0.14, 0.08))
    dark = mat("needle_dark", (0.06, 0.10, 0.06))

    join([
        cyl(0.12, 1.85, (0.00, 0.00, 0.92), verts=6),
    ], "body_trunk", bark)
    join([
        cone(0.62, 1.40, (-0.12, 0.03, 1.50), verts=7),
    ], "accent_skirt", needle)
    join([
        cone(0.34, 1.20, (0.14, -0.02, 2.30), verts=6),
    ], "trim_top", dark)


def build_skiff():
    """Cabin skiff. A keel-down hull is a lid at 6° — the cabin is the
    same trick as the hatchback. 23° yaw for a 3/4."""
    hull = mat("hull", (0.18, 0.14, 0.10))
    rust = mat("rust", (0.48, 0.24, 0.10))
    cabin = mat("cabin", (0.12, 0.11, 0.10))

    join([
        box((2.05, 0.90, 0.42), (0.00, 0.00, 0.38)),
        box((0.60, 0.78, 0.28), (1.12, 0.00, 0.48), (0, 0, 0.50)),
        box((0.22, 0.70, 0.22), (1.48, 0.00, 0.40)),
        box((2.00, 0.08, 0.36), (0.00, 0.44, 0.58)),
        box((2.00, 0.08, 0.36), (0.00, -0.44, 0.58)),
    ], "body_hull", hull)
    join([
        box((0.90, 0.86, 0.88), (-0.42, 0.00, 1.05)),
        box((0.38, 0.82, 0.48), (0.08, 0.00, 0.95), (0, 0, 0.35)),
    ], "trim_cabin", cabin)
    join([
        box((0.40, 0.88, 0.12), (0.50, 0.00, 0.62)),
        box((0.18, 0.90, 0.14), (1.40, -0.06, 0.24), (0.5, 0, 0.3)),
        box((0.30, 0.70, 0.28), (-0.48, -0.02, 1.35)),
        cyl(0.045, 1.15, (-0.20, 0.05, 1.85), rot=(0.35, 0.25, 0), verts=5),
    ], "accent_rust", rust)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.join()
    boat = bpy.context.active_object
    boat.name = "skiff"
    boat.rotation_euler = (0.0, 0.0, 0.55)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")


def build_city_road():
    """Broken boulevard. A flat decal at 6° is a smear — the read is
    the near kerb and the tilted slabs. World units; Unity plants it
    with absoluteScale so Normalize cannot shrink it to a stamp."""
    tar = mat("asphalt", (0.17, 0.16, 0.15))
    kerb_c = mat("kerb", (0.50, 0.47, 0.42))
    hole = mat("hole", (0.07, 0.06, 0.055))
    slab = mat("slab", (0.28, 0.25, 0.22))

    # Three plates with a break at the crater. Thickness 0.08.
    join([
        box((5.4, 2.55, 0.08), (-4.4, 0.04, 0.04)),
        box((3.2, 2.40, 0.08), (0.2, 0.10, 0.04), (0, 0, 0.05)),
        box((3.4, 2.20, 0.08), (3.9, -0.10, 0.045), (0, 0, -0.07)),
    ], "body_asphalt", tar)

    # Near kerb (Blender −Y = toward camera). The AIMING camera sees
    # the player-side stretch (x ≈ −7..−3) — that is where it must break.
    join([
        box((1.6, 0.20, 0.24), (-6.4, -1.30, 0.12)),
        box((1.1, 0.18, 0.22), (-4.4, -1.22, 0.11), (0, 0, 0.18)),
        box((0.70, 0.22, 0.20), (-3.2, -0.95, 0.16), (0.55, 0.15, 0.40)),
        box((1.4, 0.18, 0.22), (-1.5, -1.26, 0.11)),
        box((1.0, 0.18, 0.16), (0.6, -1.18, 0.10), (0.2, 0, -0.22)),
        box((2.4, 0.20, 0.22), (3.6, -1.20, 0.11), (0, 0.04, 0.10)),
        box((0.55, 0.24, 0.18), (1.8, -0.78, 0.14), (0.50, 0.25, 0.55)),
    ], "accent_kerb", kerb_c)

    join([
        box((5.4, 0.16, 0.14), (-4.0, 1.24, 0.07)),
        box((3.2, 0.16, 0.14), (1.4, 1.18, 0.07)),
        box((2.0, 0.16, 0.14), (4.8, 1.08, 0.07), (0, 0, -0.12)),
    ], "trim_farkerb", kerb_c)

    join([
        box((1.8, 1.20, 0.04), (-3.4, 0.05, 0.015)),
        box((1.1, 0.80, 0.04), (-0.4, 0.25, 0.015)),
        box((0.80, 0.60, 0.04), (3.6, 0.15, 0.015)),
    ], "accent_holes", hole)

    # Two leftover slabs — the rest of the "broken" read is named
    # street objects, not more grey boxes.
    join([
        box((0.85, 0.16, 0.42), (-2.4, -0.35, 0.22), (1.00, 0.08, 0.20)),
        box((0.60, 0.14, 0.32), (1.6, -0.45, 0.18), (0.95, 0.12, 0.28)),
    ], "trim_slabs", slab)

    # --- things ON the road. Height is the whole point. ---
    iron = mat("iron", (0.14, 0.13, 0.12))
    paint = mat("paint", (0.58, 0.48, 0.16))
    concrete = mat("jersey", (0.52, 0.49, 0.43))
    rust = mat("bollard", (0.38, 0.22, 0.12))

    # Raised lane dashes. A painted line at 6° is a smear; 8cm of
    # face is a tick.
    dashes = []
    for x in (-5.2, -3.8, -1.6, 0.2, 1.6, 3.2):
        dashes.append(box((0.70, 0.16, 0.12), (x, 0.02, 0.12)))
    join(dashes, "trim_dashes", paint)

    # Manhole: rim is the vertical, well is the dark. Right of the
    # aiming rank so it is not hidden behind a body.
    join([
        cyl(0.40, 0.10, (-2.15, 0.40, 0.10), verts=10),
        cyl(0.42, 0.06, (-2.15, 0.40, 0.04), verts=10),
    ], "accent_manhole", iron)
    join([
        cyl(0.28, 0.08, (-2.15, 0.40, 0.03), verts=8),
    ], "accent_well", hole)

    # Gutter drain, tucked into the near kerb.
    join([
        box((0.85, 0.28, 0.10), (-5.0, -1.18, 0.08)),
        box((0.18, 0.22, 0.08), (-5.22, -1.18, 0.12)),
        box((0.18, 0.22, 0.08), (-4.78, -1.18, 0.12)),
    ], "accent_drain", iron)

    # Jersey barrier — the tall read. Standing piece sits in the
    # aiming frame, behind the right of the rank. Two values so
    # the 6° face is not one grey card.
    dark_c = mat("jersey_dark", (0.30, 0.28, 0.25))
    join([
        box((2.00, 0.28, 0.55), (-1.5, 0.50, 0.82)),
        box((0.22, 0.40, 0.40), (-0.38, 0.45, 0.95), (0.2, 0, 0.35)),
    ], "body_jersey", concrete)
    join([
        box((2.20, 0.48, 0.55), (-1.5, 0.50, 0.30)),
    ], "accent_jersey_base", dark_c)
    join([
        box((1.10, 0.48, 0.50), (2.55, -0.15, 0.28), (1.15, 0.10, 0.25)),
        box((0.90, 0.28, 0.45), (2.55, -0.05, 0.62), (1.05, 0.08, 0.20)),
    ], "trim_jersey_down", concrete)

    # Bollards along the near kerb. Skinny + tall.
    join([
        cyl(0.08, 0.95, (-6.15, -1.12, 0.52), verts=6),
        cyl(0.10, 0.10, (-6.15, -1.12, 1.02), verts=6),
        cyl(0.08, 0.90, (-2.55, -1.10, 0.50), verts=6),
        cyl(0.10, 0.10, (-2.55, -1.10, 0.98), verts=6),
        cyl(0.08, 0.85, (4.35, -1.08, 0.46), verts=6),
        cyl(0.10, 0.10, (4.35, -1.08, 0.92), verts=6),
    ], "accent_bollard", rust)


def build_piling():
    """Three posts + a crossbeam. Height of the tallest wins."""
    wood = mat("piling", (0.20, 0.14, 0.09))
    dark = mat("piling_dark", (0.10, 0.08, 0.06))

    join([
        cyl(0.11, 2.05, (-0.35, 0.05, 1.02), rot=(0.04, 0, 0.05), verts=6),
        cyl(0.10, 1.55, (0.38, -0.04, 0.78), rot=(-0.05, 0, -0.08), verts=6),
        cyl(0.09, 1.80, (0.02, 0.18, 0.90), rot=(0.08, 0, 0.02), verts=6),
    ], "body_posts", wood)
    join([
        box((0.85, 0.10, 0.10), (0.00, 0.08, 1.45), (0, 0.15, 0.08)),
        cyl(0.12, 0.18, (-0.35, 0.05, 2.10), verts=6),
    ], "accent_beam", dark)


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    # Do not re-export wreck_car / cactus / dead_tree — those three are
    # signed. New fileIDs on a signed GLB are a scene-rebuild tax with
    # nothing to show for it.
    builders = (
        ("prop_pine", build_pine),
        ("prop_stump", build_stump),
        ("prop_boulder", build_boulder),
        ("prop_rubble", build_rubble),
        ("prop_lamp", build_lamp),
        ("prop_barrel_cactus", build_barrel_cactus),
        ("prop_snow_pine", build_snow_pine),
        ("prop_skiff", build_skiff),
        ("prop_piling", build_piling),
        ("prop_city_road", build_city_road),  # world units; plant with absoluteScale
    )
    extra = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    only = set(extra) if extra else None
    for name, fn in builders:
        if only and name not in only:
            continue
        clear()
        fn()
        export(name)
