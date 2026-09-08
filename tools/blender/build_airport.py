"""War-torn airfield kit: hangar (gameplay structure) + wrecked planes,
a bombed runway, and a burned control tower (keepColors props).

Camera sees Blender −Y (Unity +Z). X is the battlefield. Z is up.
Origin at the base centre.

Hangar is in STRUCTURE units (worldScale 2.5 in Unity), same family as
barracks. Props are authored size; LevelScenery Normalize / absoluteScale
handles planting.

Headless:
  ~/blender/blender-5.1.2-linux-x64/blender --background --python \\
      tools/blender/build_airport.py
"""
from __future__ import annotations

import math
import os
import sys

import bpy

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


def paint(obj, material):
    obj.data.materials.clear()
    obj.data.materials.append(material)
    return obj


def clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.actions, bpy.data.armatures):
        for item in list(block):
            block.remove(item)


def export(name):
    path = os.path.join(OUT_DIR, f"{name}.glb")
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


def origin_base():
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")


# ---------------------------------------------------------------------------
# Hangar — gameplay structure. Separate chunk_* meshes so it sheds.
# Units: ~1.4 wide, 0.72 tall → worldScale 2.5 → ~3.5 × 1.8.
# Open bay faces the camera (Blender −Y).
# ---------------------------------------------------------------------------
def build_hangar():
    """Wide open-mouth hangar, not a keep.

    The thick-lid pass read as a two-storey warehouse on device
    (Rob, 2026-09-08: "that's not what we want"). The 6° camera
    needs a VERTICAL fascia under the garrison, not a second
    storey. CORE stays; chunks are skin and a roof scar — a
    hanging door in the mouth read as a closed bay. The jet
    in the bay stays (Rob, 2026-09-08).
    """
    olive = mat("hangar_body", (0.22, 0.24, 0.18))
    olive_lt = mat("hangar_roof", (0.30, 0.32, 0.24))
    dark = mat("hangar_dark", (0.07, 0.07, 0.06))
    soot = mat("hangar_soot", (0.10, 0.09, 0.08))

    # Piers + back + thin roof. Mouth is OPEN toward camera (−Y).
    # Width 1.90 → world 4.75. Wall 0.70 → world 1.75. Low and wide.
    join([
        box((0.16, 1.00, 0.68), (-0.72, 0.00, 0.34)),         # left pier
        box((0.16, 1.00, 0.68), (0.72, 0.00, 0.34)),          # right pier
        box((1.44, 0.14, 0.68), (0.00, 0.46, 0.34)),          # back wall
        box((1.52, 1.05, 0.12), (0.00, 0.02, 0.74)),          # thin roof
    ], "Hangar", olive)

    # Lintel + CATWALK toward the camera. A parapet hid the garrison
    # (Rob: "barely see the enemy units on top"). They stand on this
    # plank, silhouetted in the mouth — 6° sees bodies, not helmets.
    join([
        box((1.54, 0.14, 0.22), (0.00, -0.48, 0.69)),         # lintel fascia
        box((1.48, 0.28, 0.10), (0.00, -0.62, 0.79)),         # catwalk
    ], "trim_Hangar", olive_lt)

    # Interior lining only. A front-facing dark slab reads as a
    # closed garage door at 6° — the mouth has to be a HOLE.
    join([
        box((0.04, 0.82, 0.55), (-0.62, 0.00, 0.30)),         # inner L
        box((0.04, 0.82, 0.55), (0.62, 0.00, 0.30)),          # inner R
        box((1.16, 0.82, 0.04), (0.00, 0.02, 0.66)),          # soffit
        box((1.16, 0.70, 0.03), (0.00, 0.02, 0.02)),          # floor stain
    ], "accent_Hangar", dark)

    join([
        box((0.08, 0.48, 0.20), (-0.84, -0.12, 0.46)),
        box((0.10, 0.16, 0.12), (-0.86, 0.18, 0.58)),
    ], "accent_chunk_1", olive_lt)

    join([
        box((0.08, 0.48, 0.20), (0.84, -0.12, 0.46)),
        box((0.10, 0.16, 0.12), (0.86, 0.18, 0.58)),
    ], "accent_chunk_2", olive_lt)

    join([
        box((0.36, 0.20, 0.05), (0.28, -0.08, 0.81), (0, 0, 0.15)),
        box((0.20, 0.14, 0.04), (0.42, 0.10, 0.80)),
    ], "accent_chunk_3", soot)

    origin_base()
    zs = []
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        mw = o.matrix_world
        for v in o.data.vertices:
            zs.append((mw @ v.co).z)
    print(f"hangar maxZ={max(zs):.3f}  fascia~0.84 walkway")


# ---------------------------------------------------------------------------
# Wrecked fighter — keepColors prop. 3/4 yaw so 6° camera sees a plane.
# ---------------------------------------------------------------------------
def build_wreck_fighter():
    """Nose-up wreck. Height is the read at 6° — a long flat fuselage
    is a smear, same trap as a ground decal. Tail and a broken wing
    stand up; hull sits as high as the wrecked car."""
    char = mat("f_char", (0.16, 0.14, 0.12))
    rust = mat("f_rust", (0.48, 0.24, 0.10))
    hole = mat("f_hole", (0.04, 0.03, 0.03))
    metal = mat("f_metal", (0.22, 0.22, 0.20))
    tyre = mat("f_tyre", (0.07, 0.06, 0.05))

    # Fuselage sits HIGH, nose pitched up. Length kept close to height
    # so Normalize does not squash the tail.
    fuse = [
        box((1.90, 0.48, 0.50), (0.05, 0.00, 0.78), (0, -0.22, 0)),
        box((0.50, 0.42, 0.32), (1.10, 0.00, 0.95), (0, -0.35, 0)),
        box((0.55, 0.40, 0.36), (-0.95, 0.04, 0.62), (0, -0.10, 0)),
    ]
    join(fuse, "body_fuse", char)

    join([
        box((0.48, 0.34, 0.28), (0.25, 0.00, 1.12), (0, -0.22, 0)),
        box((0.22, 0.28, 0.18), (0.55, 0.00, 1.18), (0, -0.40, 0)),
    ], "accent_canopy", hole)

    # Broken wing STANDING — the silhouette.
    join([
        box((0.16, 0.42, 1.55), (0.10, 0.28, 1.55), (0.18, 0.12, 0.10)),
        box((0.12, 0.28, 0.45), (0.18, 0.42, 2.35), (0.45, 0.20, 0.25)),
    ], "body_wing_up", char)

    # Other wing in the dirt, still has a vertical stub.
    join([
        box((0.36, 1.20, 0.10), (0.15, -0.70, 0.22), (0.90, -0.08, -0.15)),
        box((0.14, 0.18, 0.55), (0.20, -0.35, 0.55), (0.30, 0, -0.20)),
    ], "body_wing_down", char)

    # Tail fin — tall, slightly bent. This is the plane.
    join([
        box((0.14, 0.10, 1.15), (-1.00, 0.02, 1.35), (0.12, 0.28, 0.15)),
        box((0.45, 0.08, 0.12), (-0.85, 0.18, 1.85), (0, 0.35, 0.20)),
    ], "trim_tail", rust)

    join([
        cyl(0.14, 0.20, (1.32, 0.00, 0.92), rot=(0, 1.35, 0), verts=8),
        cone(0.09, 0.16, (1.46, 0.00, 0.98), rot=(0, 1.35, 0), verts=7),
    ], "accent_nose", metal)

    join([
        box((0.06, 0.06, 0.40), (0.45, 0.18, 0.28), (0.4, 0, 0.3)),
        cyl(0.16, 0.10, (0.52, 0.28, 0.14), rot=(1.5708, 0.4, 0), verts=8),
        cyl(0.16, 0.10, (-0.35, -0.22, 0.12), rot=(1.2, 0.2, 0.5), verts=8),
    ], "accent_gear", tyre)

    join([
        box((0.32, 0.22, 0.22), (0.30, -0.20, 0.82)),
        box((0.20, 0.16, 0.16), (-0.40, 0.16, 0.72)),
    ], "accent_holes", hole)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.join()
    plane = bpy.context.active_object
    plane.name = "wreck_fighter"
    plane.rotation_euler = (0.0, 0.0, 0.48)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    origin_base()


# ---------------------------------------------------------------------------
# Wrecked transport — fatter, split, one wing in the dirt.
# ---------------------------------------------------------------------------
def build_wreck_transport():
    """Fat hull, one tail still standing. Same 6° rule as the fighter."""
    char = mat("t_char", (0.18, 0.16, 0.13))
    rust = mat("t_rust", (0.50, 0.26, 0.11))
    hole = mat("t_hole", (0.04, 0.03, 0.03))
    metal = mat("t_metal", (0.28, 0.28, 0.26))
    tyre = mat("t_tyre", (0.07, 0.06, 0.05))

    fuse = [
        box((2.40, 0.72, 0.70), (0.05, 0.00, 0.90)),
        box((0.55, 0.58, 0.48), (1.35, 0.00, 0.78), (0, -0.18, 0)),
        box((0.70, 0.52, 0.42), (-1.30, 0.08, 0.72), (0, 0, 0.22)),
    ]
    join(fuse, "body_fuse", char)

    join([
        box((0.40, 0.50, 0.32), (1.10, 0.00, 1.18)),
        box((0.24, 0.42, 0.20), (1.35, 0.00, 1.08), (0, -0.30, 0)),
    ], "accent_cockpit", hole)

    # One wing still on the roof, the other a vertical break.
    join([
        box((0.42, 1.40, 0.12), (0.10, 0.55, 1.32), (0.12, 0.05, 0.08)),
    ], "body_wing", char)
    join([
        box((0.16, 0.40, 1.40), (0.15, -0.30, 1.55), (0.20, 0.15, 0.18)),
        box((0.12, 0.22, 0.40), (0.22, -0.42, 2.25), (0.40, 0.20, 0.25)),
    ], "body_wing_up", char)

    join([
        box((0.16, 0.12, 1.35), (-1.40, 0.16, 1.55), (0.15, 0.22, 0.12)),
        box((0.12, 0.10, 0.45), (-1.28, -0.22, 0.70), (0.85, -0.35, 0.30)),
    ], "trim_tail", rust)

    join([
        cyl(0.18, 0.55, (0.20, 0.70, 1.18), rot=(0, 1.5708, 0.15), verts=8),
        cyl(0.12, 0.16, (0.52, 0.74, 1.16), rot=(0, 1.5708, 0.15), verts=8),
    ], "accent_engine", metal)

    join([
        box((0.42, 0.28, 0.30), (0.10, -0.30, 0.95)),
        box((0.28, 0.22, 0.24), (-0.55, 0.28, 0.98)),
        box((0.50, 0.08, 0.55), (0.20, 0.00, 0.55)),
    ], "accent_holes", hole)

    join([
        cyl(0.20, 0.14, (0.70, 0.28, 0.16), rot=(1.5708, 0.3, 0), verts=8),
        cyl(0.20, 0.14, (-0.50, -0.22, 0.14), rot=(1.1, 0.4, 0.4), verts=8),
        box((0.08, 0.08, 0.45), (0.60, 0.20, 0.38), (0.5, 0, 0.2)),
    ], "accent_gear", tyre)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.join()
    plane = bpy.context.active_object
    plane.name = "wreck_transport"
    plane.rotation_euler = (0.0, 0.0, -0.40)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    origin_base()


# ---------------------------------------------------------------------------
# Bombed runway — world units, plant with absoluteScale.
# Thickness + raised dashes so 6° camera sees tarmac, not a smear.
# ---------------------------------------------------------------------------
def build_runway():
    tar = mat("rwy_tar", (0.14, 0.13, 0.12))
    tar2 = mat("rwy_tar2", (0.18, 0.16, 0.14))
    paint_c = mat("rwy_paint", (0.72, 0.70, 0.62))
    hole = mat("rwy_hole", (0.07, 0.06, 0.05))
    dirt = mat("rwy_dirt", (0.32, 0.24, 0.14))

    # Three plates with a crater break. ~16 wide, 3.2 deep.
    join([
        box((6.4, 3.10, 0.08), (-5.2, 0.04, 0.04)),
        box((4.0, 2.90, 0.08), (0.2, 0.12, 0.04), (0, 0, 0.04)),
        box((5.2, 2.70, 0.08), (4.6, -0.08, 0.045), (0, 0, -0.05)),
    ], "body_tarmac", tar)

    # Scorched darker patch under the crater.
    join([
        box((2.4, 1.80, 0.04), (0.4, 0.10, 0.02)),
        box((1.4, 1.10, 0.04), (3.2, -0.20, 0.02)),
    ], "accent_scorch", tar2)

    # Crater.
    join([
        cyl(0.85, 0.06, (0.55, 0.05, 0.02), verts=9),
        cyl(0.55, 0.08, (0.55, 0.05, 0.00), verts=8),
    ], "accent_crater", hole)

    # Dirt thrown from the hole.
    join([
        box((0.70, 0.40, 0.10), (1.20, 0.45, 0.08), (0.2, 0.1, 0.4)),
        box((0.50, 0.30, 0.08), (-0.10, -0.55, 0.07), (0.3, 0, -0.3)),
    ], "trim_ejecta", dirt)

    # Centreline dashes — 8cm face so they tick at 6°.
    dashes = []
    for x in (-6.4, -5.0, -3.6, -2.2, 1.6, 2.8, 4.0, 5.4):
        dashes.append(box((0.85, 0.18, 0.12), (x, 0.02, 0.12)))
    join(dashes, "trim_dashes", paint_c)

    # Threshold bars on the player-side stretch (aiming camera).
    bars = []
    for y in (-1.10, -0.70, -0.30, 0.30, 0.70, 1.10):
        bars.append(box((1.10, 0.16, 0.10), (-6.6, y, 0.11)))
    join(bars, "trim_threshold", paint_c)

    # Near edge — a low lip so the strip has a silhouette.
    join([
        box((7.0, 0.16, 0.14), (-4.4, -1.52, 0.08)),
        box((4.2, 0.16, 0.12), (2.8, -1.40, 0.07), (0, 0, 0.08)),
        box((0.55, 0.22, 0.16), (-2.0, -1.10, 0.12), (0.4, 0.1, 0.5)),
    ], "accent_edge", tar2)


# ---------------------------------------------------------------------------
# Burned control tower — keepColors mid-ground prop, not a structure.
# ---------------------------------------------------------------------------
def build_control_tower():
    char = mat("ct_char", (0.20, 0.19, 0.16))
    rust = mat("ct_rust", (0.46, 0.24, 0.11))
    hole = mat("ct_hole", (0.05, 0.04, 0.04))
    glass = mat("ct_glass", (0.08, 0.10, 0.12))
    pale = mat("ct_pale", (0.32, 0.30, 0.26))

    # Stalk, slightly leaned.
    join([
        box((0.55, 0.55, 1.80), (0.00, 0.00, 0.90), (0.08, 0, 0.06)),
        box((0.62, 0.62, 0.12), (0.00, 0.00, 0.08)),          # plinth
    ], "body_stalk", char)

    # Cab, bigger than the stalk — that is the silhouette.
    join([
        box((1.15, 1.00, 0.55), (0.08, -0.05, 2.05), (0.10, 0.05, 0.08)),
        box((1.25, 1.10, 0.08), (0.08, -0.05, 2.36), (0.10, 0.05, 0.08)),  # roof
    ], "body_cab", pale)

    # Dark glass band.
    join([
        box((1.05, 0.12, 0.32), (0.08, -0.52, 2.08), (0.10, 0.05, 0.08)),
        box((0.12, 0.90, 0.32), (0.62, -0.05, 2.08), (0.10, 0.05, 0.08)),
        box((0.12, 0.90, 0.32), (-0.46, -0.05, 2.08), (0.10, 0.05, 0.08)),
    ], "accent_glass", glass)

    # Snapped aerial.
    join([
        cyl(0.04, 0.85, (0.35, 0.20, 2.70), rot=(0.6, 0.2, 0.4), verts=6),
        box((0.18, 0.08, 0.08), (0.55, 0.40, 2.95), (0.4, 0.3, 0.5)),
    ], "trim_aerial", rust)

    # Burn scar on the stalk.
    join([
        box((0.20, 0.18, 0.70), (0.22, -0.22, 1.10)),
        box((0.28, 0.16, 0.22), (0.18, -0.48, 2.00)),
    ], "accent_burn", hole)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.join()
    t = bpy.context.active_object
    t.name = "control_tower_wreck"
    t.rotation_euler = (0.0, 0.0, 0.22)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    origin_base()


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    builders = (
        ("hangar", build_hangar),
        # Wreck planes are CC0 airliners from wreck_from_real_planes.py.
        # The box builders stay as reference; do not overwrite the glbs.
        ("prop_runway", build_runway),
        ("prop_control_tower", build_control_tower),
    )
    only = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    for name, fn in builders:
        if only and name not in only:
            continue
        clear()
        fn()
        export(name)


if __name__ == "__main__":
    main()
