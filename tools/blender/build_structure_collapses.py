"""
Build per-structure collapse GLBs from the LIVE models.

The outpost wreck was hand-keyed. Everything else is recovered from the
shipped GLB: join() left vertex islands, so Separate-by-loose-parts
gives the original boxes back. One solid (the three naturals) is sliced
on X/Z so it can still fold.

Does not touch outpost_collapse.glb — that clip is signed off.

Run:
  ~/blender/blender-5.1.2-linux-x64/blender --background --python \\
      tools/blender/build_structure_collapses.py
"""
from __future__ import annotations

import json
import math
import os
import struct
import sys

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODELS = os.path.join(ROOT, "Assets", "Models")

# Unique enemy models. Tank is a vehicle. Outpost is already authored.
SOURCES = [
    "barracks",
    "garrison_post",
    "fortress_tier",
    "fortress_tier_small",
    "fortress_tier_wide",
    "bunker",
    "mountain_bunker",
    "watch_tower",
    "ridge_watchtower",
    "tower_base",
    "tower_platform",
    "comms_tower",
    "cliff_outcrop",
]

FPS = 60
HOLD = 4
END = 34


def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.actions, bpy.data.armatures):
        for item in list(block):
            block.remove(item)


def import_glb(path):
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def origin_to_base(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    mw = obj.matrix_world
    verts = [mw @ v.co for v in obj.data.vertices]
    if not verts:
        return
    lowest = min(v.z for v in verts)
    cx = sum(v.x for v in verts) / len(verts)
    cy = sum(v.y for v in verts) / len(verts)
    cur = bpy.context.scene.cursor
    prev = cur.location.copy()
    cur.location = (cx, cy, lowest)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    cur.location = prev


def separate_loose(obj):
    prefix = "accent_" if obj.name.startswith("accent") else ""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    # Imported cubes often have unwelded faces; loose-parts then
    # yields one object per FACE and the wreck is gravel.
    bpy.ops.mesh.remove_doubles(threshold=0.0001)
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    parts = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    for i, p in enumerate(parts):
        if prefix and not p.name.startswith("accent"):
            p.name = f"{prefix}{p.name}"
    return parts


def bisect_grid(obj, nx=2, nz=2):
    """Slice a solid along X and Z so a natural rock can still fold."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    minx, maxx = min(v.x for v in bb), max(v.x for v in bb)
    minz, maxz = min(v.z for v in bb), max(v.z for v in bb)
    prefix = "accent_" if obj.name.startswith("accent") else ""

    def cut(axis, value):
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        plane = (1, 0, 0) if axis == "X" else (0, 0, 1)
        bpy.ops.mesh.bisect(
            plane_co=(value, 0, value) if axis == "Z" else (value, 0, 0),
            plane_no=plane,
            clear_inner=False,
            clear_outer=False,
            use_fill=True,
        )
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.separate(type="LOOSE")
        bpy.ops.object.mode_set(mode="OBJECT")

    for i in range(1, nx):
        cut("X", minx + (maxx - minx) * i / nx)
    # After first axis the selection is many objects; slice each remaining tall piece.
    pieces = [o for o in bpy.data.objects if o.type == "MESH"]
    for p in list(pieces):
        if p.name not in bpy.data.objects:
            continue
        bpy.ops.object.select_all(action="DESELECT")
        p.select_set(True)
        bpy.context.view_layer.objects.active = p
        bb2 = [p.matrix_world @ Vector(c) for c in p.bound_box]
        z0, z1 = min(v.z for v in bb2), max(v.z for v in bb2)
        if z1 - z0 < 0.12:
            continue
        for i in range(1, nz):
            if p.name not in bpy.data.objects:
                break
            bpy.ops.object.select_all(action="DESELECT")
            p.select_set(True)
            bpy.context.view_layer.objects.active = p
            cut("Z", z0 + (z1 - z0) * i / nz)
    out = [o for o in bpy.data.objects if o.type == "MESH"]
    for i, p in enumerate(out):
        if prefix and not p.name.startswith("accent"):
            p.name = f"{prefix}part_{i}"
    return out


def shatter_box(obj, cell=0.36):
    """Replace a leftover building-sized box with a grid of smaller
    boxes. Bisect on a joined mesh left one fat island (L8 garrison
    1.35×0.95×0.75) that still read as a hut. The outpost bar is a
    pile at ~0.50 of standing — this authors that, not a lean retune."""
    mw = obj.matrix_world.copy()
    bb = [mw @ Vector(c) for c in obj.bound_box]
    minx, maxx = min(v.x for v in bb), max(v.x for v in bb)
    miny, maxy = min(v.y for v in bb), max(v.y for v in bb)
    minz, maxz = min(v.z for v in bb), max(v.z for v in bb)
    dx, dy, dz = maxx - minx, maxy - miny, maxz - minz

    def n_for(length, size, lo, hi):
        return max(lo, min(hi, int(round(length / size))))

    nx = n_for(dx, cell, 2, 5)
    ny = n_for(dy, cell, 2, 4)
    nz = n_for(dz, 0.28, 2, 4)
    if dz > 0.60:
        nz = max(nz, 3)
    sx, sy, sz = dx / nx, dy / ny, dz / nz
    if min(sx, sy, sz) < 0.06:
        return [obj]
    prefix = "accent_" if obj.name.startswith("accent") else ""
    base = obj.name.split(".")[0]
    if prefix and not base.startswith("accent"):
        base = prefix + base
    mats = list(obj.data.materials)
    out = []
    for ix in range(nx):
        for iy in range(ny):
            for iz in range(nz):
                cx = minx + (ix + 0.5) * sx
                cy = miny + (iy + 0.5) * sy
                cz = minz + (iz + 0.5) * sz
                bpy.ops.mesh.primitive_cube_add(size=1.0, location=(cx, cy, cz))
                p = bpy.context.active_object
                p.name = f"{base}_rubble_{ix}_{iy}_{iz}"
                p.rotation_euler = (0.0, 0.0, 0.0)
                p.scale = (sx * 0.98, sy * 0.98, sz * 0.98)
                bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
                p.data.materials.clear()
                for m in mats:
                    p.data.materials.append(m)
                out.append(p)
    bpy.data.objects.remove(obj, do_unlink=True)
    return out


def split_mass(obj, nx=2, nz=2):
    """Bisect one leftover building-sized box. Unlike bisect_grid this
    does not harvest every mesh in the scene."""
    before = set(bpy.data.objects)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    minx, maxx = min(v.x for v in bb), max(v.x for v in bb)
    minz, maxz = min(v.z for v in bb), max(v.z for v in bb)
    prefix = "accent_" if obj.name.startswith("accent") else ""

    def cut_selected(axis, value):
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        plane = (1, 0, 0) if axis == "X" else (0, 0, 1)
        bpy.ops.mesh.bisect(
            plane_co=(value, 0, value) if axis == "Z" else (value, 0, 0),
            plane_no=plane,
            clear_inner=False,
            clear_outer=False,
            use_fill=True,
        )
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.separate(type="LOOSE")
        bpy.ops.object.mode_set(mode="OBJECT")

    for i in range(1, nx):
        cut_selected("X", minx + (maxx - minx) * i / nx)
    spawned = [o for o in bpy.data.objects if o.type == "MESH" and o not in before]
    spawned.append(obj)
    for p in list(spawned):
        if p.name not in bpy.data.objects:
            continue
        bb2 = [p.matrix_world @ Vector(c) for c in p.bound_box]
        z0, z1 = min(v.z for v in bb2), max(v.z for v in bb2)
        if z1 - z0 < 0.20:
            continue
        bpy.ops.object.select_all(action="DESELECT")
        p.select_set(True)
        bpy.context.view_layer.objects.active = p
        for i in range(1, nz):
            if p.name not in bpy.data.objects:
                break
            cut_selected("Z", z0 + (z1 - z0) * i / nz)
    out = [o for o in bpy.data.objects if o.type == "MESH" and (o is obj or o not in before)]
    for i, p in enumerate(out):
        if prefix and not p.name.startswith("accent"):
            p.name = f"{prefix}mass_{i}"
    return out


def key(obj, frame, loc, rot):
    obj.location = loc
    obj.rotation_euler = rot
    obj.keyframe_insert("location", frame=frame)
    obj.keyframe_insert("rotation_euler", frame=frame)


def _hash01(name, shift=0):
    h = 2166136261
    for c in name:
        h = ((h ^ ord(c)) * 16777619) & 0xFFFFFFFF
    return ((h >> shift) & 255) / 255.0


def pose_collapse(obj):
    """Drop every island onto the dirt and leave a low mound.

    L8's watch + garrison stayed at 97% of standing height because an
    86° lean around a piece's own base, with a 5cm drop, is still a
    tower of tilted boxes. A deck classified as a wall then stands
    ON EDGE. The signed-off outpost ends at 0.50 of 1.22 — that is
    the silhouette this pose is aiming at.
    """
    obj.rotation_mode = "XYZ"
    rest_loc = obj.location.copy()
    rest_rot = obj.rotation_euler.copy()
    h = max(rest_loc.z, 0.0)
    dims = obj.dimensions
    sx, sy, sz = max(dims.x, 1e-4), max(dims.y, 1e-4), max(dims.z, 1e-4)
    thin = min(sx, sy, sz)
    # Lie a wall down on its thin axis. A fat block already sits on
    # its shortest side — 80° of lean stands a longer side up and
    # the garrison reads as a building again.
    already_flat = sz <= thin * 1.20
    wall_x = (not already_flat) and sx <= thin * 1.20
    wall_y = (not already_flat) and sy <= thin * 1.20
    side = 1.0 if rest_loc.x >= -1e-4 else -1.0
    jx = _hash01(obj.name, 0) * 2.0 - 1.0
    jy = _hash01(obj.name, 8) * 2.0 - 1.0
    jz = _hash01(obj.name, 16)
    # High pieces come all the way down. Leave a couple of centimetres
    # so overlapping boxes read as a pile, not a stain.
    pad = 0.02 + 0.07 * jz
    drop = max(0.0, h - pad)
    if already_flat:
        # A few degrees only — sin(16°) × a 1m deck is a 28cm wall.
        lean = 3.0 + 3.0 * abs(jx)
        roll = 3.0 * jx
        twist = 10.0 * jy
    elif wall_y:
        lean = 82.0 + 6.0 * abs(jx)
        roll = 10.0 * jx
        twist = 18.0 * jy
    elif wall_x:
        lean = 10.0 * jx
        roll = 82.0 + 6.0 * abs(jy)
        twist = 16.0 * jy
    else:
        lean = 10.0 + 6.0 * jx
        roll = 8.0 * jy
        twist = 12.0 * jy
    # Fan off the original column so a tower is a debris field.
    sx = side * (0.10 + 0.28 * h) + 0.12 * jx
    sy = -0.05 * h + 0.14 * jy
    r = math.radians

    def at(frame, k):
        loc = rest_loc + Vector((sx * k, sy * k, -drop * k))
        rot = (
            rest_rot.x + r(lean) * k,
            rest_rot.y + r(roll) * k,
            rest_rot.z + r(twist) * k,
        )
        key(obj, frame, loc, rot)

    key(obj, 1, rest_loc, rest_rot)
    key(obj, HOLD, rest_loc, rest_rot)
    at(10, 0.18)
    at(18, 0.55)
    at(28, 0.92)
    at(END, 1.0)


def nla_collapse():
    for obj in bpy.data.objects:
        ad = obj.animation_data
        if ad is None or ad.action is None:
            continue
        action = ad.action
        while ad.nla_tracks:
            ad.nla_tracks.remove(ad.nla_tracks[0])
        track = ad.nla_tracks.new()
        track.name = "collapse"
        strip = track.strips.new("collapse", 1, action)
        strip.name = "collapse"
        ad.action = None


def export_glb(path):
    bpy.ops.object.select_all(action="DESELECT")
    root = bpy.data.objects.get("wreck_root")
    if root is None:
        return
    root.select_set(True)
    for o in bpy.data.objects:
        p = o
        while p.parent is not None:
            p = p.parent
        if p == root:
            o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=False,
        export_animations=True,
        export_nla_strips=True,
        export_force_sampling=True,
        export_cameras=False,
        export_lights=False,
    )


def merge_anims(path):
    data = open(path, "rb").read()
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    off = 12
    json_chunk = bin_chunk = None
    while off < length:
        clen, ctype = struct.unpack_from("<I4s", data, off)
        payload = data[off + 8 : off + 8 + clen]
        if ctype.startswith(b"JSON"):
            json_chunk = payload
        elif ctype.startswith(b"BIN"):
            bin_chunk = payload
        off += 8 + clen
    j = json.loads(json_chunk)
    anims = j.get("animations") or []
    if len(anims) <= 1:
        if anims:
            anims[0]["name"] = "collapse"
            js = json.dumps(j, separators=(",", ":")).encode("utf-8")
            pad = (4 - (len(js) % 4)) % 4
            js += b" " * pad
            bin_pad = (4 - (len(bin_chunk) % 4)) % 4
            bin_chunk = bin_chunk + (b"\x00" * bin_pad)
            total = 12 + 8 + len(js) + 8 + len(bin_chunk)
            out = bytearray()
            out += struct.pack("<4sII", b"glTF", 2, total)
            out += struct.pack("<I4s", len(js), b"JSON")
            out += js
            out += struct.pack("<I4s", len(bin_chunk), b"BIN\x00")
            out += bin_chunk
            open(path, "wb").write(out)
        return len(anims), len(anims[0]["channels"]) if anims else 0
    merged = {"name": "collapse", "channels": [], "samplers": []}
    for a in anims:
        base = len(merged["samplers"])
        merged["samplers"].extend(a.get("samplers", []))
        for ch in a.get("channels", []):
            c = dict(ch)
            c["sampler"] = ch["sampler"] + base
            merged["channels"].append(c)
    j["animations"] = [merged]
    js = json.dumps(j, separators=(",", ":")).encode("utf-8")
    pad = (4 - (len(js) % 4)) % 4
    js += b" " * pad
    bin_pad = (4 - (len(bin_chunk) % 4)) % 4
    bin_chunk = bin_chunk + (b"\x00" * bin_pad)
    total = 12 + 8 + len(js) + 8 + len(bin_chunk)
    out = bytearray()
    out += struct.pack("<4sII", b"glTF", 2, total)
    out += struct.pack("<I4s", len(js), b"JSON")
    out += js
    out += struct.pack("<I4s", len(bin_chunk), b"BIN\x00")
    out += bin_chunk
    open(path, "wb").write(out)
    return len(anims), len(merged["channels"])


def build_one(stem):
    src = os.path.join(MODELS, f"{stem}.glb")
    dst = os.path.join(MODELS, f"{stem}_collapse.glb")
    if not os.path.isfile(src):
        print(f"SKIP {stem}: no {src}")
        return False
    clear()
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = END
    bpy.context.scene.frame_set(1)

    meshes = import_glb(src)
    parts = []
    for m in list(meshes):
        if m.name not in bpy.data.objects:
            continue
        got = separate_loose(m)
        parts.extend(got if got else [m])
    if len(parts) < 4:
        solids = [p for p in parts if p.name in bpy.data.objects]
        parts = []
        for s in solids:
            parts.extend(bisect_grid(s, 2, 2))

    root = bpy.data.objects.new("wreck_root", None)
    bpy.context.collection.objects.link(root)
    root.location = (0, 0, 0)

    scored = []
    for p in parts:
        if p.name not in bpy.data.objects or p.type != "MESH":
            continue
        vol = max(p.dimensions.x, 0.001) * max(p.dimensions.y, 0.001) * max(p.dimensions.z, 0.001)
        scored.append((vol, p))
    scored.sort(key=lambda t: -t[0])
    keep_n = min(28, len(scored))
    for _, p in scored[keep_n:]:
        bpy.data.objects.remove(p, do_unlink=True)
    parts = [p for _, p in scored[:keep_n]]

    # A leftover 1m cube is still a building (L8 garrison, fortress
    # tiers). Author a rubble grid — do not retune lean/drop on a cube.
    cracked = []
    for p in list(parts):
        if p.name not in bpy.data.objects or p.type != "MESH":
            continue
        d = p.dimensions
        if min(d.x, d.y, d.z) > 0.35 and max(d.x, d.y, d.z) > 0.80:
            cracked.extend(shatter_box(p))
        else:
            cracked.append(p)
    parts = [p for p in cracked if p.name in bpy.data.objects and p.type == "MESH"]

    kept = []
    for p in parts:
        if p.name not in bpy.data.objects or p.type != "MESH":
            continue
        if max(p.dimensions) < 0.045:
            bpy.data.objects.remove(p, do_unlink=True)
            continue
        if p.parent:
            mw = p.matrix_world.copy()
            p.parent = None
            p.matrix_world = mw
        origin_to_base(p)
        mw = p.matrix_world.copy()
        p.parent = root
        p.matrix_world = mw
        # glTF import is QUATERNION. Euler keys on that mode do not
        # evaluate, so the clip only DROPS the boxes — L8's "collapsed
        # but still standing" wreck. Force XYZ before any key.
        p.rotation_mode = "XYZ"
        pose_collapse(p)
        kept.append(p)

    nla_collapse()
    export_glb(dst)
    n_anims, n_ch = merge_anims(dst)
    print(f"OK {stem}: {len(kept)} parts, {n_anims} clips -> {n_ch} channels, {os.path.getsize(dst)} bytes")
    return True


def main():
    only = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    stems = only or SOURCES
    ok = 0
    for stem in stems:
        if build_one(stem):
            ok += 1
    print(f"done {ok}/{len(stems)}")


if __name__ == "__main__":
    main()
