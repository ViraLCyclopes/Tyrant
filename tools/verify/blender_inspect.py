# Blender (5.2) headless check: import a .glb, print rig / shape-key facts, render a side view to PNG.
# Usage: blender -b --factory-startup -P tools/verify/blender_inspect.py -- <model.glb> <out.png>
import math
import sys

import bpy
from mathutils import Vector

glb, png = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH" and o.data.vertices and o.vertex_groups]
armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
print("CHECK armatures", [(a.name, len(a.data.bones)) for a in armatures])
for m in meshes:
    keys = [k.name for k in m.data.shape_keys.key_blocks] if m.data.shape_keys else []
    print("CHECK mesh", m.name, "verts", len(m.data.vertices), "faces", len(m.data.polygons),
          "modifiers", [md.type for md in m.modifiers], "vertex_groups", len(m.vertex_groups), "shape_keys", keys)

points = [m.matrix_world @ Vector(c) for m in meshes for c in m.bound_box]
low = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
high = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
print("CHECK bounds", tuple(round(v, 3) for v in low), tuple(round(v, 3) for v in high))

center, size = (low + high) / 2, max(high - low)
camera = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
bpy.context.scene.collection.objects.link(camera)
camera.location = center + Vector((size * 2.2, 0, size * 0.3))
camera.rotation_euler = (math.radians(85), 0, math.radians(90))
bpy.context.scene.camera = camera
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.rotation_euler = (math.radians(45), math.radians(20), math.radians(30))
bpy.context.scene.collection.objects.link(sun)
world = bpy.data.worlds.new("world")
world.color = (0.3, 0.3, 0.32)
bpy.context.scene.world = world
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x, scene.render.resolution_y = 900, 500
scene.render.filepath = png
bpy.ops.render.render(write_still=True)
print("CHECK rendered", png)
