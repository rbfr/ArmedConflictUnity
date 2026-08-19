"""Mountains / Winter / Desert backdrop strips. Same contract as city.

  Blender Z-up. Origin at the front plane (y = 0).
  Facades face -Y (Unity +Z). Mass extends +Y.
  Do NOT Normalize. Colour by prefix: body / trim_* / accent_* (snow).

Never bpy.ops.wm.read_factory_settings.
"""
from __future__ import annotations

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
    merged.data.name = name  # else a joined result exports its mesh as "Cube"
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


def value_noise(x, seed):
    i = int(x) if x >= 0 else int(x) - 1
    f = x - i
    u = f * f * (3.0 - 2.0 * f)
    return (1.0 - u) * (hash01(i, seed) * 2.0 - 1.0) + u * (hash01(i + 1, seed) * 2.0 - 1.0)


def ridged_fbm(x, seed, octaves):
    s, amp, freq = 0.0, 0.5, 1.0
    for o in range(octaves):
        n = 1.0 - abs(value_noise(x * freq, seed + o * 131))
        s += n * n * amp
        freq *= 2.13
        amp *= 0.52
    return s


def fbm(x, seed, octaves):
    s, amp, freq = 0.0, 0.5, 1.0
    for o in range(octaves):
        s += value_noise(x * freq, seed + o * 131) * amp
        freq *= 2.13
        amp *= 0.5
    return s


def normalize_profile(p, floor):
    lo, hi = min(p), max(p)
    span = max(hi - lo, 1e-6)
    return [floor + (1.0 - floor) * (v - lo) / span for v in p]


def make_ridge_mesh(name, width, height, depth, cycles, floor, octaves, seed,
                    y0=0.0, cols=96, kind="ridge"):
    """Extrude a ridged silhouette into a solid. Isolated cones read as
    a picket of white pyramids (L13). A continuous range is the old
    winter silhouette, with thickness.

    kind=range: fewer, broader massifs. Mountains at cycles=10 / octaves=4
    was a saw of horns; snow on those horns is the iceberg-tooth skyline
    on L1."""
    if kind == "dune":
        raw = []
        for i in range(cols):
            x = (i / (cols - 1)) * cycles
            warp = fbm(x, seed, 2)
            raw.append(fbm(x + 0.35 * warp, seed, octaves))
    elif kind == "range":
        raw = []
        for i in range(cols):
            x = (i / (cols - 1)) * cycles
            # Two octaves of ridge = massifs with shoulders. High-octave
            # ridge is what made every local max a horn.
            big = ridged_fbm(x, seed, octaves)
            rumble = 0.10 * fbm(x * 2.4, seed + 17, 2)
            raw.append(big + rumble)
    else:
        raw = [ridged_fbm((i / (cols - 1)) * cycles, seed, octaves) for i in range(cols)]
    prof = normalize_profile(raw, floor)
    verts = []
    faces = []
    for i, h in enumerate(prof):
        x = -width * 0.5 + width * i / (cols - 1)
        z = max(height * h, 0.08)
        yf, yb = y0, y0 + depth
        base = len(verts)
        verts.extend(((x, yf, -height * 0.10), (x, yf, z),
                      (x, yb, z), (x, yb, -height * 0.10)))
        if i == 0:
            continue
        prev = base - 4
        faces.append((prev, base, base + 1, prev + 1))
        faces.append((prev + 1, base + 1, base + 2, prev + 2))
        faces.append((prev + 2, base + 2, base + 3, prev + 3))
        faces.append((prev + 3, base + 3, base, prev))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_flat()
    return obj, prof


def make_snow_mesh(name, width, height, depth, prof, snow_line, seed, y0=0.0,
                   min_run=1):
    """Cap only the crests. A white cone for every peak is the L13 bug.

    min_run drops one- and two-column spikes: those read as icebergs, not
    snow on a mountain. A column only snows if it sits in a run at least
    that long."""
    cols = len(prof)
    above = [h >= snow_line for h in prof]
    if min_run > 1:
        keep = [False] * cols
        i = 0
        while i < cols:
            if not above[i]:
                i += 1
                continue
            j = i
            while j < cols and above[j]:
                j += 1
            if j - i >= min_run:
                peak = max(prof[i:j])
                # Only the crest of the run, not the whole shoulder.
                # Snowing every column above snow_line made L7 a white mesa.
                for k in range(i, j):
                    if prof[k] >= peak - 0.22:
                        keep[k] = True
            i = j
        above = keep
    verts, faces = [], []
    for i, h in enumerate(prof):
        if not above[i]:
            continue
        x = -width * 0.5 + width * i / (cols - 1)
        # Drape from the local crest. Filling down to a global snow_line
        # is a ruler — the white mesa / iceberg-hat on L7.
        z1 = height * h + 0.06
        z0 = height * max(h - 0.14, snow_line * 0.85)
        yf, yb = y0 - 0.04, y0 + depth * 0.55
        base = len(verts)
        verts.extend(((x, yf, z0), (x, yf, z1), (x, yb, z1), (x, yb, z0)))
        if i > 0 and above[i - 1] and base >= 4:
            prev = base - 4
            faces.append((prev, base, base + 1, prev + 1))
            faces.append((prev + 1, base + 1, base + 2, prev + 2))
            faces.append((prev + 2, base + 2, base + 3, prev + 3))
    if len(verts) < 8:
        return None
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def face_dress(body, width, prof, height, seed, y=0.15, count=18):
    """Rocky slabs on the camera face so the 6° view is not a flat card."""
    cols = len(prof)
    for k in range(count):
        t = (k + 0.4) / count
        i = int(t * (cols - 1))
        h = prof[i] * height
        if h < 1.2:
            continue
        w = 0.55 + 0.85 * hash01(k, seed)
        hh = 0.45 + 0.90 * hash01(k + 3, seed)
        x = -width * 0.5 + width * t
        z = 0.2 + (h - hh) * (0.15 + 0.5 * hash01(k + 7, seed))
        body.append(box((w, 0.35, hh), (x, y, z),
                        rot=(0.08, 0, (hash01(k + 11, seed) - 0.5) * 0.35)))


def build_mountains(snow_from=0.62, prefix="Mountains", far_seed=401, near_seed=523,
                    far_cycles=10.0, near_cycles=5.5, far_kind="ridge",
                    far_octaves=4, far_floor=0.30, snow_min_run=1,
                    far_foothill=False):
    """Winter used to be this with a lower snow line and nothing else — same
    widths, same cycles, same SEEDS, so it was Mountains in a white coat and
    read as "the same background". The seeds and cycle counts are arguments
    now; a biome that shares this generator must not share its profile."""
    far_w, far_h, far_d = 78.0, 17.0, 5.5
    near_w, near_h, near_d = 58.0, 7.6, 3.4
    far_obj, far_prof = make_ridge_mesh(
        f"{prefix}Far", far_w, far_h, far_d, cycles=far_cycles, floor=far_floor,
        octaves=far_octaves, seed=far_seed, y0=1.4, kind=far_kind)
    snow = make_snow_mesh(
        f"accent_{prefix}Far", far_w, far_h, far_d, far_prof,
        snow_line=snow_from, seed=far_seed, y0=1.4, min_run=snow_min_run)
    near_obj, near_prof = make_ridge_mesh(
        f"{prefix}Near", near_w, near_h, near_d, cycles=near_cycles, floor=0.34,
        octaves=3, seed=near_seed, y0=0.35)
    # Near foothills get no snow — a cap on a short hill says it is a peak.

    # A THIRD RANGE, not surface detail. `face_dress` slabs were joined into
    # the body and so took its material — invisible. Split out as `trim_` they
    # were visible and WORSE: at 6° with flat unlit shading a slab on a face has
    # no lighting to make it read as rock, so it reads as a dark rectangle
    # stuck to a hill. Depth here is bought with OVERLAPPING SILHOUETTES at
    # different values, which is the only cue that survives a flat fill.
    # Shorter, busier and standing in FRONT of the near range (smaller y).
    foothill, _ = make_ridge_mesh(
        f"trim_{prefix}Near", near_w * 0.98, near_h * 0.52, 2.0,
        cycles=near_cycles * 1.9, floor=0.30, octaves=3, seed=near_seed + 7,
        y0=0.05)

    m_far = mat(f"mat_{prefix}Far", (0.62, 0.70, 0.78))
    m_snow = mat(f"mat_{prefix}Snow", (0.92, 0.95, 0.98))
    m_near = mat(f"mat_{prefix}Near", (0.36, 0.42, 0.50))
    m_rock = mat(f"mat_{prefix}Rock", (0.19, 0.22, 0.26))
    far_parts = [
        join([far_obj], f"{prefix}Far", m_far),
        join([snow], f"accent_{prefix}Far", m_snow) if snow else None,
    ]
    if far_foothill:
        # Darker range in FRONT of the pale far one. Hides leftover
        # horns and buys a third silhouette step on the far plane.
        far_hill, _ = make_ridge_mesh(
            f"trim_{prefix}Far", far_w * 0.90, far_h * 0.42, 3.2,
            cycles=far_cycles * 1.35, floor=0.34, octaves=3,
            seed=far_seed + 11, y0=0.45, kind=far_kind)
        far_parts.append(join([far_hill], f"trim_{prefix}Far", m_rock))
    far = tuple(far_parts)
    near = (
        join([near_obj], f"{prefix}Near", m_near),
        join([foothill], f"trim_{prefix}Near", m_rock),
    )
    return far, near


def build_desert():
    far_obj, _ = make_ridge_mesh(
        "DesertFar", 80.0, 9.0, 6.0, cycles=3.5, floor=0.30,
        octaves=3, seed=601, y0=1.6, kind="dune")
    near_obj, _ = make_ridge_mesh(
        "DesertNear", 60.0, 5.8, 3.8, cycles=2.2, floor=0.30,
        octaves=3, seed=733, y0=0.4, kind="dune")
    m_far = mat("mat_DesertFar", (0.86, 0.74, 0.56))
    m_near = mat("mat_DesertNear", (0.64, 0.49, 0.31))
    far = (join([far_obj], "DesertFar", m_far),)
    near = (join([near_obj], "DesertNear", m_near),)
    return far, near


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
    objs = [o for o in objs if o is not None]
    hide_except(objs)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
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


def dump(tag, far, near):
    fb = bounds_of(far)
    nb = bounds_of(near)
    print(f"{tag} FAR  spanX={fb[1]-fb[0]:.2f} h={fb[5]-fb[4]:.2f}")
    print(f"{tag} NEAR spanX={nb[1]-nb[0]:.2f} h={nb[5]-nb[4]:.2f}")


def build_one(kind):
    clear_meshes()
    os.makedirs(OUT_DIR, exist_ok=True)
    if kind == "mountains":
        # L1 far range was a saw of horns with a white triangle on each
        # — icebergs, not a mountain. Broad massifs, snow only on wide
        # crests, a darker far foothill so the pale skyline is not a
        # picket. Winter keeps the old arguments.
        far, near = build_mountains(
            0.74, "Mountains",
            far_cycles=4.4, far_kind="range", far_octaves=2, far_floor=0.38,
            snow_min_run=7, far_foothill=True)
        dump("MOUNTAINS", far, near)
        export_glb(os.path.join(OUT_DIR, "backdrop_mountains_far.glb"), far)
        export_glb(os.path.join(OUT_DIR, "backdrop_mountains_near.glb"), near)
    elif kind == "winter":
        # 0.42 whited out the far range. 0.60 still did on L7: silhouetteFar
        # is already pale, so snow on every horn read as "white mountain
        # things". Same range treatment as L1 — broad massifs, snow only
        # on wide crests, a darker far foothill so the white is caps, not
        # the whole skyline. Seeds stay Winter's so it is not Mountains.
        # snow_from=2: no cap mesh. A snow solid at 6° is a white object
        # (iceberg, mesa, chimney) no matter the drape. Winter already
        # has a white ground and snowfall — the range just needs to be
        # a pale mountain, not a second snow field in the sky.
        far, near = build_mountains(
            2.0, "Winter", far_seed=911, near_seed=947,
            far_cycles=4.6, near_cycles=4.0,
            far_kind="range", far_octaves=2, far_floor=0.36,
            snow_min_run=8, far_foothill=True)
        dump("WINTER", far, near)
        export_glb(os.path.join(OUT_DIR, "backdrop_winter_far.glb"), far)
        export_glb(os.path.join(OUT_DIR, "backdrop_winter_near.glb"), near)
    elif kind == "desert":
        far, near = build_desert()
        dump("DESERT", far, near)
        export_glb(os.path.join(OUT_DIR, "backdrop_desert_far.glb"), far)
        export_glb(os.path.join(OUT_DIR, "backdrop_desert_near.glb"), near)
    else:
        raise SystemExit(f"unknown {kind}")


def main():
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    kinds = only or ["mountains", "winter", "desert"]
    for k in kinds:
        build_one(k)


if __name__ == "__main__":
    main()
