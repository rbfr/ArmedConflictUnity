"""Forest backdrop — three world-scale strips (far / mid / near).

  Foreground was built 2026-08-17 and unwired 2026-08-18: the shrubs
  read as a dark blob-row and were never the ask. build_fore stays
  in this file; do not export it for Forest.

  Near and mid plant Kenney Nature Kit meshes (CC0, tools/blender/
  kenney_nature/). Far stays a cheap cone mass. Do not copy the kit
  GLBs into Assets/Models — the scene builder wires every file there.

  Same contract as the city.

  Blender Z-up. Origin at the base of the FRONT plane (y = 0).
  Facades face -Y (Unity +Z, toward the camera).
  Mass extends +Y (away from the camera).
  World units = game units. Do NOT Normalize — width is the span.
  Colour by mesh-name prefix: body (canopy) / trim_* (trunks, dark understorey).

  Near ~52 wide, trees up to ~14. Far ~70 wide, ridge+trees ~12.
  Many NARROW crowns. Few wide triangles is how Forest read as mountains.

Never bpy.ops.wm.read_factory_settings — it kills the MCP addon.
"""
from __future__ import annotations

import math
import os

import bpy
from mathutils import Vector

OUT_DIR = "/home/rob/UnityProjects/ArmedConflictSpike/Assets/Models"
KIT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       "kenney_nature", "glb")

# Kenney Nature Kit (CC0). Near/mid plant these; far stays a cheap mass.
# Keep this list in KIT_DIR — do not copy the GLBs into Assets/Models.
KENNEY_PINES = (
    "tree_pineTallA.glb", "tree_pineTallB.glb",
    "tree_pineTallC.glb", "tree_pineTallD.glb",
    "tree_pineDefaultA.glb", "tree_pineDefaultB.glb",
    "tree_pineRoundA.glb", "tree_pineRoundB.glb", "tree_pineRoundC.glb",
)
KENNEY_LEAFS = (
    "tree_oak.glb", "tree_thin.glb", "tree_tall.glb", "tree_simple.glb",
)
KENNEY_BIRCH = ("tree_thin.glb", "tree_tall.glb")
KENNEY_FILL = ("tree_pineSmallA.glb", "tree_pineSmallB.glb",
               "tree_pineRoundD.glb")

# filename -> (crown_obj or None, trunk_obj or None). Hidden templates.
# Cleared in build() because clear_meshes() deletes the objects.
_TEMPLATES = {}


def mat(name, color):
    m = bpy.data.materials.new(name=name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    if "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = 0.95
    spec = bsdf.inputs.get("Specular IOR Level") or bsdf.inputs.get("Specular")
    if spec is not None:
        spec.default_value = 0.03
    return m


def box(dims, loc, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.active_object
    obj.dimensions = dims
    if rot is not None:
        obj.rotation_euler = rot
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def join(objs, name, material, origin=(0.0, 0.0, 0.0), keep_material=False):
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
    if not keep_material:
        merged.data.materials.clear()
        merged.data.materials.append(material)
    bpy.context.scene.cursor.location = origin
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.shade_flat()
    return merged


def join_by_source(plants, prefix):
    """One mesh per Kenney material so the colormap survives export.

    The old 3-slot join (body/trim/accent) is what flattened the kit
    into our silhouette greens — that is why it did not look like Kenney.
    """
    groups = {}
    for o in plants:
        if o is None:
            continue
        key = o.data.materials[0].name if o.data.materials else "none"
        groups.setdefault(key, []).append(o)
    out = []
    for key, parts in groups.items():
        safe = "".join(c if c.isalnum() else "_" for c in key)
        out.append(join(parts, f"{prefix}_{safe}", None, keep_material=True))
    return [o for o in out if o]


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


def _obj_bounds(obj):
    xs, ys, zs = [], [], []
    for c in obj.bound_box:
        w = obj.matrix_world @ Vector(c)
        xs.append(w.x)
        ys.append(w.y)
        zs.append(w.z)
    return min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)


def _dup(obj):
    c = obj.copy()
    c.data = obj.data.copy()
    c.hide_set(False)
    c.hide_render = False
    bpy.context.collection.objects.link(c)
    return c


def _load_kenney(filename):
    """Import once, split woodBark* / leafs* into hidden templates."""
    if filename in _TEMPLATES:
        return _TEMPLATES[filename]
    path = os.path.join(KIT_DIR, filename)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    others = [o for o in new if o.type != "MESH"]
    if not meshes:
        raise RuntimeError(f"Kenney tree {filename} imported no mesh")
    mesh = meshes[0]
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    if mesh.parent is not None:
        bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in others:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="MATERIAL")
    bpy.ops.object.mode_set(mode="OBJECT")
    parts = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    crown = trunk = None
    for p in parts:
        matn = (p.data.materials[0].name if p.data.materials else "").lower()
        if "leaf" in matn:
            crown = p
        else:
            trunk = p
    if crown is None and trunk is None and parts:
        crown = parts[0]
    for p in parts:
        p.hide_set(True)
        p.hide_render = True
        p.name = f"_tmpl_{filename}_{p.name}"
    _TEMPLATES[filename] = (crown, trunk)
    return crown, trunk


def pick_kenney(names, seed):
    return names[int(hash01(seed, 811) * len(names)) % len(names)]


def plant_kenney(plants, filename, x, y, h, yaw=0.0):
    """Instance a Kenney tree at (x, y), scaled to height h.

    Materials stay on the mesh — the runtime keeps a textured atlas
    instead of retinting to silhouette green. That is the control shot
    for 'what does just Kenney look like'.
    """
    crown_t, trunk_t = _load_kenney(filename)
    parts = []
    if crown_t is not None:
        parts.append(_dup(crown_t))
    if trunk_t is not None:
        parts.append(_dup(trunk_t))
    if not parts:
        return
    zs = []
    for o in parts:
        b = _obj_bounds(o)
        zs.extend((b[4], b[5]))
    native = max(max(zs) - min(zs), 0.01)
    s = h / native
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.scale = (s, s, s)
        o.rotation_euler = (0.0, 0.0, yaw)
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    zs = []
    for o in parts:
        b = _obj_bounds(o)
        zs.extend((b[4], b[5]))
    z0 = min(zs)
    for o in parts:
        o.location.x += x
        o.location.y += y
        o.location.z += -z0
        plants.append(o)


def cone(x, y, z, radius, depth, lean=0.0):
    bpy.ops.mesh.primitive_cone_add(
        vertices=6, radius1=radius, radius2=0.0, depth=depth, location=(x, y, z))
    o = bpy.context.active_object
    o.rotation_euler = (0, 0, lean)
    bpy.ops.object.transform_apply(rotation=True)
    return o


def tree(body, trim, x, y, h, seed, lean=0.0, broadleaf=True):
    """A pine used to be trunk + ONE cone, which at 6° is a green triangle —
    and every crown went into the body material, so the whole stand was one
    flat green wall. Two fixes, both about silhouette and value:

    THREE OVERLAPPING SKIRTS instead of one cone, so the outline is notched
    and reads as a conifer rather than a wedge; and a third of the stand is
    built into the TRIM material (body lerped 48% to black), so the canopy
    carries two greens. A flat fill has no lighting to give depth — the
    variation has to be authored across the stand.

    Skirts are OFFSET in X (2026-08-18). Symmetric cones still read as a
    picket of identical triangles; a lean on the top skirt plus a side-shift
    on the middle one is what notches the skyline.

    Roughly one tree in five is a BROADLEAF. A single ico-sphere crown read
    as a hill, not a tree — two stacked, narrower blobs on a visible trunk.
    """
    dark = hash01(seed + 31, 307) > 0.66
    crowns = trim if dark else body
    tw = 0.09 + 0.06 * hash01(1, seed)

    if broadleaf and hash01(seed + 41, 307) > 0.80:
        # Broadleaf: taller bare trunk, TWO stacked crowns. One ico-sphere at
        # h*0.30 was a boulder; two smaller ones read as a tree at 6°.
        trim.append(box((tw * 1.3, tw * 1.3, h * 0.48), (x, y, h * 0.24)))
        ox = (hash01(seed + 7, 307) - 0.5) * h * 0.08
        bpy.ops.mesh.primitive_ico_sphere_add(
            subdivisions=1, radius=h * 0.20,
            location=(x + ox, y, h * 0.52))
        o = bpy.context.active_object
        o.scale = (1.15, 0.80, 0.80)
        bpy.ops.object.transform_apply(scale=True)
        crowns.append(o)
        bpy.ops.mesh.primitive_ico_sphere_add(
            subdivisions=1, radius=h * 0.14,
            location=(x - ox * 0.6, y, h * 0.74))
        o = bpy.context.active_object
        o.scale = (1.05, 0.75, 0.85)
        bpy.ops.object.transform_apply(scale=True)
        crowns.append(o)
        return

    trim.append(box((tw, tw, h * 0.24), (x, y, h * 0.12)))
    side = 1.0 if hash01(seed + 19, 307) > 0.5 else -1.0
    # A few SPIRES — tall and thin, the forest equivalent of the city's
    # chimneys. A stand of identical-width triangles is a picket.
    thin = hash01(seed + 11, 307) > 0.68
    rs = 0.58 if thin else 1.0
    for i, (r, d, z, ox) in enumerate((
            (0.175, 0.42, 0.43, 0.00),
            (0.138, 0.40, 0.62, side * 0.035),
            (0.095, 0.40, 0.82, side * 0.055))):
        crowns.append(cone(x + h * ox, y, h * z, h * r * rs, h * d,
                           lean if i == 2 else lean * 0.4))


def _smooth(t, seed):
    i = int(t)
    f = t - i
    f = f * f * (3.0 - 2.0 * f)
    a, b = hash01(i, seed), hash01(i + 1, seed)
    return a + (b - a) * f


def ridge(body, x0, x1, height, depth, seed, cycles=6.0, cols=80):
    """The land mass behind the trees. This used to be a row of overlapping
    BOXES, and a box seen from 6° above is a rectangle with a dead-flat top —
    the pale crates and the "bucket on a pole" showing through the gaps in the
    stand were this, not the trees. A continuous noise profile instead: same
    silhouette job, no straight edges to give it away."""
    verts, faces = [], []
    for k in range(cols):
        t = k / (cols - 1)
        n = (0.55 * _smooth(t * cycles, seed)
             + 0.30 * _smooth(t * cycles * 2.3 + 7.0, seed + 1)
             + 0.15 * _smooth(t * cycles * 4.7 + 13.0, seed + 2))
        h = height * (0.45 + 0.75 * n)
        x = x0 + (x1 - x0) * t
        base = len(verts)
        verts.extend(((x, 0.0, -0.30), (x, 0.0, h), (x, depth, h), (x, depth, -0.30)))
        if k == 0:
            continue
        p = base - 4
        faces.append((p, base, base + 1, p + 1))
        faces.append((p + 1, base + 1, base + 2, p + 2))
        faces.append((p + 2, base + 2, base + 3, p + 3))
    mesh = bpy.data.meshes.new(f"hill{seed}")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(f"hill{seed}", mesh)
    bpy.context.collection.objects.link(obj)
    body.append(obj)


def snag(trim, x, y, h, seed):
    """A dead standing trunk with two stub branches. Every crown in the stand
    is a filled shape; a bare snag is the one silhouette that reads as a GAP,
    and a treeline needs those or it is a hedge."""
    lean = (hash01(seed + 3, 307) - 0.5) * 0.22
    tw = 0.10 + 0.05 * hash01(seed, 307)
    trim.append(box((tw, tw, h), (x, y, h * 0.5), rot=(0, lean, 0)))
    for k in range(2):
        side = 1.0 if k == 0 else -1.0
        bl = 0.55 + 0.65 * hash01(seed + 11 * k, 307)
        bz = h * (0.50 + 0.30 * hash01(seed + 7 * k, 307))
        trim.append(box((bl, tw * 0.7, tw * 0.7),
                        (x + side * bl * 0.45, y, bz),
                        rot=(0, side * 0.45, 0)))


def birch(body, accent, x, y, h, seed):
    """The one thing in a dark wood that reads as DETAIL at 6°: a pale vertical.
    The trunk goes in `accent_`, which the runtime paints body-lerped-70%-to-
    white — the third value slot.

    An ico-sphere crown made these lollipops (a hexagon on a stick). The
    stroke is the whole read — a long pale trunk and a small box cap."""
    tw = 0.14 + 0.04 * hash01(seed, 307)
    accent.append(box((tw, tw, h * 0.90), (x, y, h * 0.45)))
    body.append(cone(x, y, h * 0.96, h * 0.09, h * 0.18))


def fern(trim, x, y, s, seed, wide=1.30, lean=0.0):
    bpy.ops.mesh.primitive_cone_add(
        vertices=5, radius1=s, radius2=0.0, depth=s * 1.10, location=(x, y, s * 0.55))
    o = bpy.context.active_object
    o.scale = (wide, 0.70, 1.00)
    if lean:
        o.rotation_euler = (0.0, lean, 0.0)
    bpy.ops.object.transform_apply(scale=True, rotation=True)
    trim.append(o)


def bush(body, x, y, s, seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=s, location=(x, y, s * 0.55))
    o = bpy.context.active_object
    o.scale = (1.15, 0.75, 0.70)
    bpy.ops.object.transform_apply(scale=True)
    body.append(o)


# Authored groves — (center x, half-width, peak height). Noise across the
# strip still produced a hedge: the tops landed in one band. The city
# strip is interesting because its towers are PLACED. Same job here.
# Camera at x=0 (Aiming) sees the gap between the -7 and +5 groves, so
# the mid plane shows through the notch the player actually looks at.
NEAR_GROVES = (
    (-19.0, 5.0, 13.0),
    (-7.0, 4.2, 20.0),
    (5.5, 5.5, 16.5),
    (17.5, 4.8, 14.0),
)


def grove_at(x, groves):
    """Weight 0..1 and the grove's peak height. 0 = clearing."""
    best, peak = 0.0, 8.0
    for cx, hw, ph in groves:
        d = abs(x - cx) / hw
        if d >= 1.0:
            continue
        w = 1.0 - d * d
        if w > best:
            best, peak = w, ph
    return best, peak


def grove_envelope(t, seed):
    """Mid-plane only. Near uses authored groves; mid stays a dense wood
    so a near-gap reveals trees, not sky. Phase-shifted vs the near
    groves so a mid peak sits behind a near dip."""
    return (0.48 * _smooth(t * 2.8, seed)
            + 0.32 * _smooth(t * 5.6 + 4.0, seed + 1)
            + 0.20 * _smooth(t * 10.0 + 9.0, seed + 2))


def build_near(m_body, m_trim, m_accent):
    land, plants = [], []
    x0, x1 = -26.0, 26.0
    # Low ridge only so the trees are not floating. No cone trees, no
    # box snags — this pass is the Kenney kit on its own.
    ridge(land, x0, x1, height=1.4, depth=2.6, seed=401)
    x = x0 + 0.4
    i = 0
    while x < x1 - 0.4:
        env, peak = grove_at(x, NEAR_GROVES)
        if env < 0.18:
            x += 2.2 + hash01(i + 13, 307) * 0.8
            i += 1
            continue
        h = peak * (0.62 + 0.38 * env)
        y = 0.30 + hash01(i + 5, 307) * 1.25
        yaw = hash01(i + 9, 307) * 6.283
        kind = hash01(i + 51, 307)
        if kind > 0.78:
            plant_kenney(plants, pick_kenney(KENNEY_LEAFS, 500 + i),
                         x, y, h * 0.78, yaw=yaw)
        else:
            plant_kenney(plants, pick_kenney(KENNEY_PINES, 500 + i),
                         x, y, h, yaw=yaw)
        if env > 0.50 and hash01(i + 17, 307) > 0.40:
            plant_kenney(plants, pick_kenney(KENNEY_FILL, 700 + i),
                         x + 0.7, y + 0.9, h * 0.55, yaw=yaw + 1.1)
        x += 1.85 + hash01(i + 13, 307) * 0.85
        i += 1
    out = []
    if land:
        out.append(join(land, "ForestNearLand", m_body))
    out.extend(join_by_source(plants, "ForestNear"))
    return out


def build_fore(m_body, m_trim):
    """FOREGROUND, at Backdrop.ForeZ = +3 — between the camera and the play
    plane. This is a FRAMING device, not scenery: it is painted near-black and
    carries no detail, because anything legible down here competes with the
    units instead of framing them.

    Two rules it must obey, both about not eating the game:
      * NOTHING may cross the middle of the frame at unit height. The arc is
        the whole read of this game and a foreground that hides a shell at
        apex is worse than an empty sky.
      * The band is CONTINUOUS in x. The camera pans the length of the
        battlefield and a gap would swing an empty edge through the shot.
    So: a low fringe everywhere, and sparse tall trunks at wide intervals that
    frame an edge when the camera happens to sit between them.
    """
    body, trim = [], []
    x0, x1 = -34.0, 34.0

    # SIZES ARE TINY ON PURPOSE. At ForeZ this sits ~5 units from a camera that
    # frames the play plane at ~8, so everything here is magnified against the
    # units and a value authored to look right in Blender comes out several
    # times too big in the game. The first attempt used 0.42-0.80 fronds and
    # they filled the bottom third of the screen as dark pyramids.
    x = x0
    i = 0
    while x < x1:
        # MEASURED, not derived. At ForeZ this layer is magnified ~7x against
        # the play plane, far more than the naive (camZ - ForeZ) ratio predicts,
        # because the camera's projection is tilted and off-centre — the usual
        # estimate is simply wrong here. 0.16-0.32 fronds still came out as a
        # row of dark pyramids 200px tall against a 120px soldier. These numbers
        # were read back off a device screenshot: a frond wants to finish around
        # a third of a soldier's height, which is world height ~0.12.
        # DENSE AND IRREGULAR. Evenly spaced fronds at 0.20-0.36 read as a row
        # of little dark tents with ground showing between them — the spacing
        # was legible, which is exactly what undergrowth must not be. Fronds
        # overlap now, and vary in width, lean and depth so the band has a
        # broken edge instead of a repeating one.
        s = 0.030 + 0.075 * hash01(i, 613)
        if hash01(i + 29, 613) > 0.90:
            s *= 1.9
        fern(body if hash01(i + 5, 613) > 0.4 else trim,
             x, 0.55 * hash01(i + 19, 613), s, 620 + i,
             wide=0.95 + 0.85 * hash01(i + 23, 613),
             lean=(hash01(i + 31, 613) - 0.5) * 0.55)
        # Sparingly: an ico-sphere is ~240 exported verts against a frond's ~30,
        # and at this density the bushes alone were 60k of the layer's 56k.
        if hash01(i + 11, 613) > 0.88:
            bush(trim, x + 0.06, 0.30 * hash01(i + 7, 613),
                 0.025 + 0.030 * hash01(i + 3, 613), 640 + i)
        x += 0.07 + 0.07 * hash01(i + 13, 613)
        i += 1

    # NO TRUNKS. Sparse full-height trunks were built here first and are the
    # reason this comment exists: at ForeZ one is ~110px wide and runs the WHOLE
    # height of the screen, so wherever the camera happened to stop it cut the
    # frame in half and crossed the band the shells arc through. A world-fixed
    # foreground cannot promise to put one at a screen EDGE — the camera pans
    # the length of the battlefield and the strip does not move with it — so
    # "trunks at the edges" is not a thing this layer can do. It frames with a
    # continuous undergrowth fringe instead. Anything taller belongs in the
    # near strip, behind the units, where it cannot occlude the arc.

    return (
        join(body, "ForestFore", m_body),
        join(trim, "trim_ForestFore", m_trim),
    )


def build_mid(m_body, m_trim, m_accent):
    """The THIRD plane, at Backdrop.MidZ. Two strips cap the wood at four depth
    steps and it read as two cut-outs however much detail went on either one.

    This plane stays DENSE on purpose. Near now leaves clearings; if mid also
    dipped in the same place the gap would be sky, not a paler wood. Height
    still varies so a near-dip reveals a skyline, not a flat wall."""
    land, plants = [], []
    x0, x1 = -30.0, 30.0
    ridge(land, x0, x1, height=4.2, depth=3.6, seed=137, cycles=7.0)
    x = x0
    i = 0
    while x < x1:
        t = (x - x0) / (x1 - x0)
        env = grove_envelope(t + 0.18, 173)
        h = 8.0 + 7.5 * (0.30 + 0.70 * env)
        if env > 0.68 and hash01(i + 43, 173) > 0.45:
            h *= 1.18
        y = 0.60 + hash01(i + 5, 173) * 1.60
        yaw = hash01(i + 9, 173) * 6.283
        kind = hash01(i + 51, 173)
        if kind > 0.82:
            plant_kenney(plants, pick_kenney(KENNEY_LEAFS, 300 + i),
                         x, y, h * 0.80, yaw=yaw)
        else:
            plant_kenney(plants, pick_kenney(KENNEY_PINES, 300 + i),
                         x, y, h, yaw=yaw)
        x += 1.25 + hash01(i + 13, 173) * 0.60
        i += 1
    out = []
    if land:
        out.append(join(land, "ForestMidLand", m_body))
    out.extend(join_by_source(plants, "ForestMid"))
    return out


def build_far(m_body, m_trim, m_accent):
    land, plants = [], []
    x0, x1 = -35.0, 35.0
    ridge(land, x0, x1, height=7.2, depth=5.0, seed=89)
    ridge(land, x0 + 1.5, x1 - 1.5, height=4.0, depth=3.2, seed=97)
    x = x0
    i = 0
    while x < x1:
        h = 6.2 + 3.4 * hash01(i, 211)
        if hash01(i + 47, 211) > 0.89:
            h *= 1.38
        y = 1.4 + hash01(i + 5, 211) * 2.2
        plant_kenney(plants, pick_kenney(KENNEY_PINES, 800 + i),
                     x, y, h, yaw=hash01(i + 9, 211) * 6.283)
        x += 1.20 + hash01(i + 13, 211) * 0.55
        i += 1
    out = []
    if land:
        out.append(join(land, "ForestFarLand", m_body))
    out.extend(join_by_source(plants, "ForestFar"))
    return out


def bounds_of(objs):
    xs, ys, zs = [], [], []
    for o in objs:
        if o is None:
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
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
        export_image_format="AUTO",
    )
    print(f"exported {path}")


def build():
    clear_meshes()
    _TEMPLATES.clear()
    # Preview colours match Forest.asset. Runtime retones to silhouetteFar/Near,
    # and the MID plane to the halfway lerp of the two — mirrored here so a
    # Blender render and the game agree. `silhouetteFar` was lightened-to-
    # (0.42,0.565,0.455) on 2026-08-17; this constant had gone stale against it.
    m_far = mat("mat_ForestFar", (0.42, 0.565, 0.455))
    m_far_trim = mat("mat_ForestFarTrim", (0.28, 0.22, 0.16))
    m_near = mat("mat_ForestNear", (0.25, 0.43, 0.27))
    m_near_trim = mat("mat_ForestNearTrim", (0.18, 0.12, 0.08))
    m_near_accent = mat("mat_ForestNearAccent", (0.77, 0.83, 0.78))

    near = build_near(m_near, m_near_trim, m_near_accent)
    m_far_accent = mat("mat_ForestFarAccent", (0.87, 0.90, 0.88))
    far = build_far(m_far, m_far_trim, m_far_accent)

    m_mid = mat("mat_ForestMid", (0.334, 0.496, 0.361))
    m_mid_trim = mat("mat_ForestMidTrim", (0.174, 0.258, 0.188))
    m_mid_accent = mat("mat_ForestMidAccent", (0.827, 0.869, 0.834))
    mid = build_mid(m_mid, m_mid_trim, m_mid_accent)

    # Foreground is NOT exported. Rob 2026-08-18: the shrubs do not look
    # great and were never the ask. build_fore stays in this file so the
    # measurements (7x magnification, no trunks, 0.03-0.10 fronds) are not
    # rediscovered if a later biome actually wants a fringe.

    nb = bounds_of(near)
    fb = bounds_of(far)
    mb = bounds_of(mid)
    print("NEAR bounds x[{:.2f},{:.2f}] y[{:.2f},{:.2f}] z[{:.2f},{:.2f}]".format(*nb))
    print("MID  bounds x[{:.2f},{:.2f}] y[{:.2f},{:.2f}] z[{:.2f},{:.2f}]".format(*mb))
    print("FAR  bounds x[{:.2f},{:.2f}] y[{:.2f},{:.2f}] z[{:.2f},{:.2f}]".format(*fb))
    print("NEAR spanX={:.2f} height={:.2f}  FAR spanX={:.2f} height={:.2f}".format(
        nb[1] - nb[0], nb[5] - nb[4], fb[1] - fb[0], fb[5] - fb[4]))

    os.makedirs(OUT_DIR, exist_ok=True)
    export_glb(os.path.join(OUT_DIR, "backdrop_forest_near.glb"), list(near))
    export_glb(os.path.join(OUT_DIR, "backdrop_forest_mid.glb"), list(mid))
    export_glb(os.path.join(OUT_DIR, "backdrop_forest_far.glb"), list(far))
    return {"near": nb, "mid": mb, "far": fb}


if __name__ == "__main__":
    build()
