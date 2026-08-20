"""6° lineup of mid-ground props. Camera sees Blender −Y, Z up."""
from __future__ import annotations

import math
import os

import bpy
from mathutils import Vector

OUT = "/home/rob/UnityProjects/ArmedConflictSpike/Builds/prop_lineup.png"
MODELS = "/home/rob/UnityProjects/ArmedConflictSpike/Assets/Models"
NAMES = (
    "prop_wreck_car",
    "prop_dead_tree",
    "prop_cactus",
    "prop_pine",
    "prop_stump",
    "prop_boulder",
    "prop_rubble",
    "prop_lamp",
    "prop_barrel_cactus",
    "prop_snow_pine",
    "prop_skiff",
    "prop_piling",
)


def clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


clear()
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
ground = bpy.context.active_object
mat = bpy.data.materials.new("ground")
mat.use_nodes = True
mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (
    0.61, 0.49, 0.33, 1
)
ground.data.materials.append(mat)

spacing = 3.0
start = -spacing * (len(NAMES) - 1) / 2
for i, name in enumerate(NAMES):
    path = os.path.join(MODELS, f"{name}.glb")
    bpy.ops.import_scene.gltf(filepath=path)
    imported = [o for o in bpy.context.selected_objects]
    x = start + i * spacing
    for o in imported:
        o.location.x += x

# Game camera is ~6° above the ground, looking at the play plane.
dist = 26.0
elev = math.radians(6.0)
cam_loc = Vector((0.0, dist, dist * math.tan(elev) + 0.9))
bpy.ops.object.camera_add(location=cam_loc)
cam = bpy.context.active_object
cam.data.lens_unit = "FOV"
cam.data.angle = math.radians(72)
direction = Vector((0.0, 0.0, 1.2)) - cam_loc
cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
bpy.context.scene.camera = cam

light_data = bpy.data.lights.new("sun", "SUN")
light_data.energy = 3.0
light = bpy.data.objects.new("sun", light_data)
bpy.context.scene.collection.objects.link(light)
light.rotation_euler = (math.radians(50), 0, math.radians(30))

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 2200
scene.render.resolution_y = 480
scene.render.filepath = OUT
scene.render.film_transparent = False
world = bpy.data.worlds.new("sky")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.70, 0.85, 1)
scene.world = world
bpy.ops.render.render(write_still=True)
print(f"wrote {OUT}")
