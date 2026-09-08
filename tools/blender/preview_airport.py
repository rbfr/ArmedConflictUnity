"""6° lineup of the airfield kit on desert ground."""
from __future__ import annotations

import math
import os

import bpy
from mathutils import Vector

OUT = "/home/rob/UnityProjects/ArmedConflictSpike/Builds/airport_kit.png"
MODELS = "/home/rob/UnityProjects/ArmedConflictSpike/Assets/Models"


def mat(name, color):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    return m


for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)

bpy.ops.mesh.primitive_plane_add(size=50, location=(0, 0, 0))
ground = bpy.context.active_object
ground.data.materials.append(mat("ground", (0.80, 0.65, 0.42)))


def load(name, loc, scale=1.0):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(MODELS, name))
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        o.location = (o.location.x + loc[0], o.location.y + loc[1], o.location.z + loc[2])
        o.scale = (o.scale.x * scale, o.scale.y * scale, o.scale.z * scale)


# Hangar at enemy-ish x, worldScale 2.5
load("hangar.glb", (4.0, 0.0, 0.0), 2.5)
load("prop_runway.glb", (0.0, -2.4, 0.0), 1.0)
load("prop_wreck_fighter.glb", (-2.0, -1.6, 0.0), 1.0)
load("prop_wreck_transport.glb", (6.5, -3.2, 0.0), 1.0)
load("prop_control_tower.glb", (-6.0, -4.5, 0.0), 1.0)

# Unit-height stand-ins (2.70)
unit = mat("unit", (0.36, 0.42, 0.22))
for x in (-7.5, -6.6, -5.7):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, 0.2, 1.35))
    o = bpy.context.active_object
    o.dimensions = (0.45, 0.40, 2.70)
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(unit)

dist = 16.0
elev = math.radians(6.0)
look = Vector((0.5, -0.4, 0.9))
cam_loc = look + Vector((0.0, -dist, dist * math.tan(elev)))
bpy.ops.object.camera_add(location=cam_loc)
cam = bpy.context.active_object
cam.data.lens_unit = "FOV"
cam.data.angle = math.radians(55)
cam.rotation_euler = (look - cam_loc).to_track_quat("-Z", "Y").to_euler()
bpy.context.scene.camera = cam

sun = bpy.data.lights.new("sun", "SUN")
sun.energy = 3.0
sun_o = bpy.data.objects.new("sun", sun)
bpy.context.scene.collection.objects.link(sun_o)
sun_o.rotation_euler = (math.radians(50), 0, math.radians(25))

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1080
scene.render.resolution_y = 600
scene.render.filepath = OUT
world = bpy.data.worlds.new("sky")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.72, 1)
scene.world = world
bpy.ops.render.render(write_still=True)
print(f"wrote {OUT}")
