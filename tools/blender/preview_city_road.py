"""6° shot of the city road on CityRuins ground, with unit-height stand-ins."""
from __future__ import annotations

import math
import os

import bpy
from mathutils import Vector

OUT = "/home/rob/UnityProjects/ArmedConflictSpike/Builds/city_road.png"
ROAD = "/home/rob/UnityProjects/ArmedConflictSpike/Assets/Models/prop_city_road.glb"


def mat(name, color):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m


for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)

bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
ground = bpy.context.active_object
ground.data.materials.append(mat("ground", (0.42, 0.40, 0.385)))

bpy.ops.import_scene.gltf(filepath=ROAD)

# Camera sees Blender −Y. Units sit on the play plane, in front of the
# road's near kerb (kerb at y ≈ −1.3).
unit = mat("unit", (0.36, 0.42, 0.22))
for x in (-5.5, -4.4, -3.4):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, -2.4, 1.35))
    o = bpy.context.active_object
    o.dimensions = (0.45, 0.40, 2.70)
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(unit)

dist = 10.0
elev = math.radians(6.0)
# Aiming-like: look at the player-side of the road, from −Y.
look = Vector((-4.0, -1.2, 0.8))
cam_loc = look + Vector((0.0, -dist, dist * math.tan(elev)))
bpy.ops.object.camera_add(location=cam_loc)
cam = bpy.context.active_object
cam.data.lens_unit = "FOV"
cam.data.angle = math.radians(50)
cam.rotation_euler = (look - cam_loc).to_track_quat("-Z", "Y").to_euler()
bpy.context.scene.camera = cam

sun = bpy.data.lights.new("sun", "SUN")
sun.energy = 3.0
sun_o = bpy.data.objects.new("sun", sun)
bpy.context.scene.collection.objects.link(sun_o)
sun_o.rotation_euler = (math.radians(50), 0, math.radians(25))

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 540
scene.render.resolution_y = 1202
scene.render.filepath = OUT
world = bpy.data.worlds.new("sky")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.72, 1)
scene.world = world
bpy.ops.render.render(write_still=True)
print(f"wrote {OUT}")

# Wider / scout-ish: the whole boulevard, still 6°.
look2 = Vector((-1.0, -0.4, 0.9))
cam_loc2 = look2 + Vector((0.0, -16.0, 16.0 * math.tan(elev)))
cam.location = cam_loc2
cam.data.angle = math.radians(55)
cam.rotation_euler = (look2 - cam_loc2).to_track_quat("-Z", "Y").to_euler()
wide = OUT.replace("city_road.png", "city_road_wide.png")
scene.render.filepath = wide
bpy.ops.render.render(write_still=True)
print(f"wrote {wide}")
