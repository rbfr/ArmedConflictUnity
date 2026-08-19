"""CityRuins backdrop — two world-scale strips that replace the 1D block profile.

Contract (do not break):
  Blender Z-up. Origin at the base of the FRONT plane (y = 0).
  Facades face -Y (glTF / Unity +Z, toward the camera).
  Mass extends +Y (away from the camera) so a layer placed at NearZ / FarZ
  stays behind the ground plane's far edge (z = -28).
  World units = game units. Do NOT Normalize in Unity — width is the span.
  Colour by mesh-name prefix: body / trim_* (darker recesses) / accent_* (embers).

  Near strip ~50 wide, tallest 16. Far strip ~68 wide, tallest 13.
  Those are Backdrop.City's layer heights at the existing depths.

Never bpy.ops.wm.read_factory_settings — it kills the MCP addon.
"""
import math
import os
import bpy

OUT_DIR = "/home/rob/UnityProjects/ArmedConflictSpike/Assets/Models"
PREVIEW = "/tmp/backdrop_city.png"


def mat(name, color):
    m = bpy.data.materials.new(name=name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    if "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = 0.94
    spec = bsdf.inputs.get("Specular IOR Level") or bsdf.inputs.get("Specular")
    if spec is not None:
        spec.default_value = 0.04
    return m


def box(dims, loc, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.active_object
    obj.dimensions = dims
    if rot is not None:
        obj.rotation_euler = rot
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def join(objs, name, material, origin=(0.0, 0.0, 0.0)):
    objs = [o for o in objs if o is not None]
    if not objs:
        return None
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
    bpy.ops.object.shade_flat()
    return merged


def clear_meshes():
    keep_types = {"LIGHT", "CAMERA"}
    keep_names = {"studio_cam", "ground", "key", "fill", "rim"}
    for o in list(bpy.data.objects):
        if o.type in keep_types or o.name in keep_names:
            continue
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)
    for m in list(bpy.data.materials):
        if m.users == 0:
            bpy.data.materials.remove(m)


def hash01(n, seed=0):
    h = n * 374761393 + seed * 668265263
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0x7FFFFFFF) / 0x7FFFFFFF


# ---------------------------------------------------------------------------
# Building pieces
# ---------------------------------------------------------------------------

def rubble_skirt(body, x0, x1, depth, seed, height=0.55):
    """Jagged low mass so standing blocks read as what is LEFT of a district.
    Dense on purpose: overlap the chunks so it is a continuous ruin line,
    not a row of pebbles."""
    x = x0
    i = 0
    while x < x1:
        w = 0.55 + hash01(i, seed) * 1.15
        h = height * (0.55 + hash01(i + 3, seed) * 1.15)
        d = depth * (0.40 + hash01(i + 7, seed) * 0.70)
        yaw = (hash01(i + 11, seed) - 0.5) * 0.45
        body.append(box((w, d, h), (x + w * 0.5, d * 0.40, h * 0.5), rot=(0, 0, yaw)))
        if hash01(i + 19, seed) > 0.55:
            body.append(box((w * 0.45, d * 0.35, h * 0.55),
                            (x + w * 0.3, d * 0.22, h * 0.70),
                            rot=(0.15, 0.4 * (hash01(i, seed) - 0.5), 0.2)))
        x += w * (0.42 + hash01(i + 13, seed) * 0.22)
        i += 1


def rubble_front(body, x0, x1, seed):
    """Heaps on the CAMERA side of the facade (negative Y). They sit between
    the battlefield and the skyline so fire reads as coming out of wreckage."""
    x = x0
    i = 0
    while x < x1:
        w = 0.70 + hash01(i, seed) * 1.50
        h = 0.40 + hash01(i + 3, seed) * 0.95
        d = 0.55 + hash01(i + 7, seed) * 0.80
        cy = -0.50 - hash01(i + 5, seed) * 0.85
        yaw = (hash01(i + 11, seed) - 0.5) * 0.55
        body.append(box((w, d, h), (x + w * 0.5, cy, h * 0.48), rot=(0, 0, yaw)))
        if hash01(i + 17, seed) > 0.40:
            body.append(box((w * 0.45, d * 0.40, h * 0.55),
                            (x + w * 0.35, cy - 0.12, h * 0.70),
                            rot=(0.2, 0.3 * (hash01(i, seed) - 0.5), 0.25)))
        if hash01(i + 19, seed) > 0.50:
            body.append(box((w * 0.28, d * 0.30, h * 0.35),
                            (x + w * 0.15, cy + 0.10, h * 0.22),
                            rot=(0, 0.4, 0.4)))
        x += w * (0.62 + hash01(i + 13, seed) * 0.35)
        i += 1


def scorch_stain(trim, cx, w, h, seed):
    """Irregular char patches on the front face — the scorch the player asked for."""
    n = 2 + int(hash01(seed, seed) * 2.5)
    for i in range(n):
        pw = 0.35 + hash01(i + 2, seed) * 1.05
        ph = 0.30 + hash01(i + 4, seed) * 0.90
        px = cx + (hash01(i + 6, seed) - 0.5) * w * 0.55
        pz = 0.25 + hash01(i + 8, seed) * (h * 0.55)
        trim.append(box((pw, 0.05, ph), (px, 0.025, pz)))


FIRE_MARKS = []


def mark_fire(x, y, z):
    """Tiny cube at an opening. Unity hides it and plants a flame here.
    Blender (x, +Y into the block, Z up) → Unity (x, z, −y), so a small
    +Y puts the flame just behind the facade."""
    o = box((0.10, 0.10, 0.10), (x, y, z))
    o.name = f"fx_fire_{len(FIRE_MARKS)}"
    FIRE_MARKS.append(o)


def facade_dress(trim, cx, w, face_y, h, seed):
    """Floor bands, soot skirt, dark window recesses ON remaining wall.
    This is the 'texture' — the set is flat/unlit; breakup has to be mesh."""
    trim.append(box((w * 0.94, 0.07, min(0.50, h * 0.11)), (cx, face_y + 0.04, 0.26)))
    storeys = max(2, int(h / 2.2))
    for s in range(1, storeys):
        z = s * (h * 0.80 / storeys)
        if z >= h * 0.78:
            continue
        trim.append(box((w * 0.90, 0.04, 0.07), (cx, face_y + 0.03, z)))
    cols = max(2, int(w / 1.05))
    rows = max(1, storeys - 1)
    for r in range(rows):
        for c in range(cols):
            if hash01(r * 11 + c, seed) > 0.58:
                continue
            wx = cx - w * 0.34 + c * (w * 0.68 / max(cols - 1, 1))
            wz = 0.85 + r * (h * 0.52 / max(rows, 1))
            if wz > h * 0.68:
                continue
            trim.append(box((0.28, 0.11, 0.38), (wx, face_y + 0.02, wz)))


def solid_block(body, trim, cx, w, d, h, nicks=2, seed=0):
    """Occupied block with a bitten roofline plus facade dress."""
    body.append(box((w, d, h * 0.82), (cx, d * 0.5, h * 0.41)))
    step_w = w / max(nicks, 1)
    for s in range(nicks):
        nick = 0.08 + 0.28 * hash01(s + 2, seed)
        sh = h * (1.0 - nick)
        sx = cx - w * 0.5 + (s + 0.5) * step_w
        body.append(box((step_w * 0.96, d * 0.72, max(0.12, sh - h * 0.80)),
                        (sx, d * 0.40, (h * 0.80 + sh) * 0.5)))
    facade_dress(trim, cx, w, face_y=0.0, h=h * 0.82, seed=seed)
    scorch_stain(trim, cx, w, h, seed)


def _opening(body, cx, z0, hole_w, hole_h, wall_t, wall_d, jamb_h):
    """A hole cut through remaining wall: both jambs, a sill, a lintel.
    Skip this and you get a pane hanging in the sky."""
    half = hole_w * 0.5 + wall_t * 0.5
    total_h = hole_h + wall_t * 2
    # Don't let an opening stand taller than the wall it is cut from.
    if z0 + total_h > jamb_h:
        return
    body.append(box((wall_t, wall_d, total_h),
                    (cx - half, wall_d * 0.5, z0 + total_h * 0.5)))
    body.append(box((wall_t, wall_d, total_h),
                    (cx + half, wall_d * 0.5, z0 + total_h * 0.5)))
    body.append(box((hole_w, wall_d, wall_t),
                    (cx, wall_d * 0.5, z0 + wall_t * 0.5)))
    body.append(box((hole_w, wall_d, wall_t),
                    (cx, wall_d * 0.5, z0 + total_h - wall_t * 0.5)))


def gutted(body, trim, ember, cx, w, d, h, storeys, bays, seed, embers=True):
    """Broken remaining wall, not a window grid. Openings exist only inside
    masonry that still has both sides. Everything else is ragged stubs and
    debris — the destroyed feel, not an empty office block."""
    t = 0.38
    left_h = h * (0.70 + 0.30 * hash01(1, seed))
    right_h = h * (0.38 + 0.40 * hash01(2, seed))
    # Two surviving corners, different heights, thick enough to be wall.
    body.append(box((t * 1.4, d, left_h), (cx - w * 0.5 + t * 0.7, d * 0.5, left_h * 0.5)))
    body.append(box((t * 1.3, d * 0.85, right_h), (cx + w * 0.5 - t * 0.65, d * 0.42, right_h * 0.5)))
    body.append(box((t, t, left_h * 0.55), (cx - w * 0.5 + t * 0.5, d - t * 0.5, left_h * 0.28)))
    body.append(box((w, d, 0.22), (cx, d * 0.5, 0.11)))
    # Windows in the TALL corner — the wall is thick enough that a hole is a
    # hole, and each one is a place a flame can sit.
    left_x = cx - w * 0.5 + t * 0.7
    if left_h > 3.0:
        _opening(body, left_x, 0.85, 0.42, 0.62, 0.18, d * 0.45, left_h)
        mark_fire(left_x, 0.22, 1.15)
    if left_h > 5.5:
        _opening(body, left_x, 2.15, 0.38, 0.55, 0.18, d * 0.45, left_h)
        mark_fire(left_x, 0.22, 2.40)
    facade_dress(trim, left_x, t * 1.3, 0.0, left_h * 0.9, seed + 3)
    scorch_stain(trim, cx, w, h * 0.7, seed + 9)

    # Mid-wall chunks: irregular slabs sitting ON the plinth, not a bay grid.
    n_slabs = 2 + (1 if w > 3.4 else 0)
    span = w - t * 2.4
    for i in range(n_slabs):
        sw = span * (0.18 + 0.16 * hash01(20 + i, seed))
        sx = cx - span * 0.42 + i * (span / max(n_slabs - 1, 1)) * 0.85
        sh = h * (0.28 + 0.42 * hash01(30 + i, seed))
        # Keep the slab shorter than the taller corner so it reads as leftover.
        sh = min(sh, max(left_h, right_h) * 0.72)
        body.append(box((sw, d * 0.70, sh), (sx, d * 0.38, sh * 0.5)))
        # At most one punched opening, and only if the slab is wide and tall
        # enough that jamb + sill + lintel all fit.
        if sw > 1.1 and sh > 2.2 and hash01(40 + i, seed) > 0.40:
            hole_w = min(0.55, sw * 0.38)
            hole_h = min(0.85, sh * 0.32)
            hz = 0.55 + hash01(41 + i, seed) * 0.4
            _opening(body, sx, hz, hole_w, hole_h, 0.22, d * 0.55, sh)
            mark_fire(sx, 0.22, hz + hole_h * 0.45)

    # A lintel only when it can sit on BOTH corners (or a slab and a corner).
    if min(left_h, right_h) > 1.8 and hash01(8, seed) > 0.35:
        lint_z = min(left_h, right_h) * (0.45 + 0.2 * hash01(9, seed))
        body.append(box((w * 0.55, t, t), (cx - w * 0.08, t * 0.5, lint_z)))

    if hash01(10, seed) > 0.22:
        lean = 0.55 + 0.28 * hash01(11, seed)
        body.append(box((w * 0.58, d * 0.50, 0.16),
                        (cx - w * 0.05, d * 0.34, h * (0.36 + 0.16 * hash01(12, seed))),
                        rot=(0, lean, 0.10 * (hash01(13, seed) - 0.5))))
    body.append(box((w * 0.40, d * 0.45, 0.32),
                    (cx + (hash01(14, seed) - 0.5) * w * 0.18, d * 0.24, 0.22),
                    rot=(0.12, 0, 0.35 * (hash01(15, seed) - 0.5))))


def sheared_tower(body, trim, ember, cx, w, d, h, seed):
    """One surviving corner and the wreck of the rest. Not a ladder of
    floating floor plates — those were the 'windows attached to nothing'."""
    t = 0.42
    left_h = h
    right_h = h * (0.28 + 0.22 * hash01(1, seed))
    body.append(box((t * 1.35, d, left_h), (cx - w * 0.5 + t * 0.7, d * 0.5, left_h * 0.5)))
    body.append(box((t * 1.15, d * 0.70, right_h), (cx + w * 0.5 - t * 0.55, d * 0.38, right_h * 0.5)))
    body.append(box((t, t, left_h * 0.45), (cx - w * 0.5 + t * 0.5, d - t * 0.5, left_h * 0.22)))
    body.append(box((w, d, 0.20), (cx, d * 0.5, 0.10)))
    # Flame in the empty bay at the foot of the tall corner.
    mark_fire(cx + w * 0.08, 0.25, 0.70)
    if left_h > 4.0:
        _opening(body, cx - w * 0.5 + t * 0.7, 1.1, 0.36, 0.55, 0.16, d * 0.40, left_h)
        mark_fire(cx - w * 0.5 + t * 0.7, 0.22, 1.35)
    # Cantilevers OFF the tall corner, never spanning the empty bay.
    for i, frac in enumerate((0.28, 0.52, 0.74)):
        stub = w * (0.32 + 0.12 * hash01(10 + i, seed))
        body.append(box((stub, d * 0.55, 0.16),
                        (cx - w * 0.5 + t + stub * 0.5, d * 0.36, frac * left_h)))
    # Ragged bite out of the tall corner near the top.
    body.append(box((t * 0.9, t, t), (cx - w * 0.22, t * 0.5, left_h - 0.25)))
    body.append(box((w * 0.40, d * 0.40, 0.14),
                    (cx + w * 0.05, d * 0.30, right_h + 0.35),
                    rot=(0, 0.55, 0.12)))
    scorch_stain(trim, cx, w, h * 0.5, seed + 5)


def collapsed(body, cx, w, d, h):
    """A building that is mostly gone: heap + uneven stubs. Heights go down
    AND back up so it is not a staircase."""
    body.append(box((w * 0.95, d * 0.95, h * 0.28), (cx, d * 0.45, h * 0.14)))
    body.append(box((w * 0.72, d * 0.60, 0.18),
                    (cx + w * 0.06, d * 0.38, h * 0.42),
                    rot=(0, 0.62, 0.10)))
    body.append(box((w * 0.40, d * 0.45, 0.14),
                    (cx - w * 0.12, d * 0.30, h * 0.22),
                    rot=(0.2, -0.4, 0.15)))
    # Stubs: tall, drop, up again, thin.
    for stub_w, stub_h, stub_x in (
        (w * 0.16, h * 1.00, -0.38),
        (w * 0.10, h * 0.42, -0.18),
        (w * 0.14, h * 0.70, 0.04),
        (w * 0.08, h * 0.28, 0.20),
        (w * 0.13, h * 0.55, 0.36),
    ):
        body.append(box((stub_w, d * 0.28, stub_h),
                        (cx + w * stub_x, d * 0.22, stub_h * 0.5)))


def chimney(body, cx, d, h):
    """Snapped stack, not a clean factory pipe."""
    body.append(box((0.58, 0.58, h * 0.62), (cx, d * 0.55, h * 0.31)))
    body.append(box((0.48, 0.48, h * 0.22),
                    (cx + 0.18, d * 0.50, h * 0.68),
                    rot=(0, 0, 0.55)))
    body.append(box((0.70, 0.22, 0.16), (cx - 0.05, d * 0.40, h * 0.12), rot=(0, 0, 0.4)))


def antenna(trim, cx, d, z):
    trim.append(box((0.08, 0.08, 1.6), (cx, d * 0.35, z + 0.8)))
    trim.append(box((0.55, 0.06, 0.06), (cx + 0.2, d * 0.35, z + 1.35)))


# ---------------------------------------------------------------------------
# Layouts
# ---------------------------------------------------------------------------

def build_near(m_body, m_trim, m_ember):
    global FIRE_MARKS
    FIRE_MARKS = []
    body, trim, ember = [], [], []
    # Packed street: buildings overlap their neighbours so the skyline is a
    # DISTRICT, not a row of isolated frames. A second row sits slightly
    # deeper and fills the gaps the front row leaves.
    rubble_skirt(body, -25.8, 25.8, depth=3.8, seed=907, height=1.55)
    rubble_front(body, -25.0, 25.0, seed=419)

    # (kind, cx, w, d, h, extra...) — packed, overlapping, mixed ruin.
    # Front row. cx spaced ~2.2-3.0 with overlap.
    gutted(body, trim, ember, cx=-23.6, w=3.6, d=3.4, h=6.2, storeys=3, bays=2, seed=11)
    collapsed(body, cx=-21.0, w=3.4, d=3.6, h=4.4)
    gutted(body, trim, ember, cx=-18.2, w=3.8, d=3.8, h=8.8, storeys=4, bays=3, seed=21)
    solid_block(body, trim, cx=-15.4, w=2.8, d=3.2, h=5.6, nicks=3, seed=31)
    gutted(body, trim, ember, cx=-12.6, w=4.6, d=4.2, h=13.2, storeys=6, bays=3, seed=41, embers=True)
    collapsed(body, cx=-9.6, w=2.8, d=3.2, h=3.6)
    sheared_tower(body, trim, ember, cx=-7.4, w=2.4, d=3.4, h=16.0, seed=51)
    collapsed(body, cx=-4.6, w=3.6, d=3.6, h=5.2)
    solid_block(body, trim, cx=-2.0, w=2.6, d=3.0, h=6.8, nicks=2, seed=71)
    collapsed(body, cx=0.2, w=3.0, d=3.4, h=4.4)
    gutted(body, trim, ember, cx=2.6, w=4.2, d=4.0, h=11.0, storeys=5, bays=3, seed=81, embers=True)
    sheared_tower(body, trim, ember, cx=5.4, w=2.2, d=3.2, h=14.2, seed=91)
    gutted(body, trim, ember, cx=7.8, w=3.4, d=3.6, h=8.0, storeys=4, bays=2, seed=101, embers=True)
    collapsed(body, cx=10.2, w=2.6, d=3.0, h=3.4)
    solid_block(body, trim, cx=12.4, w=3.2, d=3.4, h=7.2, nicks=3, seed=111)
    gutted(body, trim, ember, cx=15.2, w=4.0, d=3.8, h=9.6, storeys=4, bays=3, seed=121, embers=True)
    chimney(body, cx=16.6, d=3.8, h=12.4)
    collapsed(body, cx=18.6, w=3.2, d=3.4, h=4.8)
    sheared_tower(body, trim, ember, cx=21.0, w=2.4, d=3.2, h=12.6, seed=131)
    gutted(body, trim, ember, cx=23.6, w=3.4, d=3.4, h=7.0, storeys=3, bays=2, seed=141)
    antenna(trim, cx=8.0, d=3.6, z=8.0)

    # Back row — lower wreckage filling the canyons, pushed +Y.
    for i, (cx, w, h) in enumerate((
        (-20.4, 3.0, 4.8), (-14.0, 2.6, 5.4), (-8.8, 3.2, 4.2),
        (-1.4, 2.8, 5.0), (4.0, 3.4, 6.2), (9.4, 2.8, 4.6),
        (14.8, 3.6, 5.8), (20.4, 2.6, 4.4),
    )):
        if i % 3 == 0:
            collapsed(body, cx=cx, w=w, d=2.4, h=h * 0.7)
        else:
            solid_block(body, trim, cx=cx, w=w, d=2.6, h=h, nicks=2, seed=200 + i)

    return (
        join(body, "CityNear", m_body),
        join(trim, "trim_CityNear", m_trim),
        join(ember, "accent_CityNear", m_ember),
    )


def build_far(m_body, m_trim):
    body, trim = [], []
    rubble_skirt(body, -34.0, 34.0, depth=2.6, seed=811, height=1.35)

    # Packed haze row. Almost no canyons — a distant district, not a picket.
    # Heights still skew low so two or three broken towers stand over a lot
    # of wreckage rather than a wall of equal blocks.
    x = -33.2
    i = 0
    towers_at = {3, 9, 16, 24}
    while x < 33.2:
        if i in towers_at:
            w = 1.8 + hash01(i, 811) * 0.9
            h = 10.8 + hash01(i + 3, 811) * 2.2
            sheared_tower(body, trim, [], cx=x + w * 0.5, w=w, d=2.2, h=h, seed=200 + i)
        else:
            w = 1.4 + hash01(i, 811) * 2.4
            h = 3.6 + 6.4 * (hash01(i + 7, 811) ** 1.6)
            d = 1.6 + hash01(i + 5, 811) * 0.8
            if hash01(i + 13, 811) > 0.62:
                collapsed(body, cx=x + w * 0.5, w=w, d=d, h=max(2.8, h * 0.70))
            else:
                solid_block(body, trim, cx=x + w * 0.5, w=w, d=d, h=h,
                            nicks=2 + (1 if w > 2.8 else 0), seed=300 + i)
        x += w * (0.72 + hash01(i + 17, 811) * 0.18)
        i += 1

    return (
        join(body, "CityFar", m_body),
        join(trim, "trim_CityFar", m_trim),
    )


def bounds_of(objs):
    xs, ys, zs = [], [], []
    for o in objs:
        if o is None:
            continue
        for c in o.bound_box:
            w = o.matrix_world @ __import__("mathutils").Vector(c)
            xs.append(w.x)
            ys.append(w.y)
            zs.append(w.z)
    if not xs:
        return None
    return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))


def hide_except(keep):
    keep = {o.name for o in keep if o is not None}
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        hide = o.name not in keep and o.name != "ground"
        o.hide_set(hide)
        o.hide_render = hide
        o.select_set(not hide)


def export_glb(path, objs):
    hide_except(objs)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        if o is not None:
            o.select_set(True)
            o.hide_set(False)
            o.hide_render = False
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_extras=False,
    )
    print(f"exported {path}")


def setup_preview_cam():
    """Gameplay-ish: camera on -Y, slightly above the ground, looking at the front plane."""
    cam = bpy.data.objects.get("studio_cam")
    if cam is None:
        bpy.ops.object.camera_add()
        cam = bpy.context.active_object
        cam.name = "studio_cam"
    # Near strip is at y=0 facing -Y; gameplay distance to NearZ is ~41.
    # Pull in a bit so the skyline fills like the 16-tall layer does on device.
    cam.location = (0.0, -28.0, 2.4)
    cam.rotation_euler = (math.radians(86.5), 0.0, 0.0)
    bpy.context.scene.camera = cam
    # Warm CityRuins sky so the holes read against something.
    world = bpy.context.scene.world
    if world is None:
        world = bpy.data.worlds.new("World")
        bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.72, 0.66, 0.58, 1.0)
        bg.inputs[1].default_value = 1.0


def render_preview(path, res=(540, 900)):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = res[0]
    sc.render.resolution_y = res[1]
    sc.render.resolution_percentage = 100
    sc.render.filepath = path
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False
    bpy.ops.render.render(write_still=True)
    print(f"rendered {path}")


def build():
    clear_meshes()
    # Preview colours match CityRuins.asset so the MCP shot is honest.
    # Runtime replaces these with silhouetteFar / silhouetteNear / ember orange.
    m_far = mat("mat_CityFar", (0.73, 0.70, 0.66))
    m_far_trim = mat("mat_CityFarTrim", (0.55, 0.52, 0.48))
    m_near = mat("mat_CityNear", (0.34, 0.30, 0.27))
    m_near_trim = mat("mat_CityNearTrim", (0.22, 0.19, 0.17))
    m_ember = mat("mat_CityEmber", (0.88, 0.48, 0.18))

    near = build_near(m_near, m_near_trim, m_ember)
    far = build_far(m_far, m_far_trim)

    nb = bounds_of(near)
    fb = bounds_of(far)
    print("NEAR bounds x[{:.2f},{:.2f}] y[{:.2f},{:.2f}] z[{:.2f},{:.2f}]".format(*nb))
    print("FAR  bounds x[{:.2f},{:.2f}] y[{:.2f},{:.2f}] z[{:.2f},{:.2f}]".format(*fb))
    print("NEAR spanX={:.2f} height={:.2f}  FAR spanX={:.2f} height={:.2f}".format(
        nb[1] - nb[0], nb[5] - nb[4], fb[1] - fb[0], fb[5] - fb[4]))

    os.makedirs(OUT_DIR, exist_ok=True)
    print(f"FIRE_MARKS {len(FIRE_MARKS)}")
    export_glb(os.path.join(OUT_DIR, "backdrop_city_near.glb"),
               [o for o in near if o is not None] + FIRE_MARKS)
    export_glb(os.path.join(OUT_DIR, "backdrop_city_far.glb"), list(far))

    # Unhide everything for the look-dev shot — near in front, far pushed back.
    for o in near:
        if o:
            o.hide_set(False)
            o.hide_render = False
            o.location = (0.0, 0.0, 0.0)
    for o in far:
        if o:
            o.hide_set(False)
            o.hide_render = False
            o.location = (0.0, 8.0, 0.0)  # further from camera for the preview only

    setup_preview_cam()
    render_preview(PREVIEW)
    return {"near": nb, "far": fb}


if __name__ == "__main__":
    build()
