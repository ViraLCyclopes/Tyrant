# blender -b --factory-startup --python rig_export.py -- <project.json> <out dir>: import like Open in Blender, export the
# model unedited (plain.glb with Send's export, plain.default.fbx with File > Export FBX's defaults), then make a rig edit
# with Start / Apply rig edit (the top bone moved up, a neck bone turned, a tail bone moved) and export it the same two ways
# (edited.glb, edited.default.fbx), with the rig edit Send would pass in unity_rig.json.
import json
import math
import os
import sys
import traceback

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ok = False
try:
    import bpy
    from mathutils import Quaternion, Vector
    import tyrant_blender
    from tyrant_blender import growth, importer, project, rig, send

    args = sys.argv[sys.argv.index("--") + 1:]
    project_path, out = args[:2]
    os.makedirs(out, exist_ok=True)
    tyrant_blender.register()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = importer.import_project(project_path)
    data = project.load(project_path)

    def fbx_defaults(path):
        # As a modder exports by hand: adult, every option as Blender has it.
        arm.tyrant_growth = 1.0
        growth.set_growth(arm, 1.0)
        growth.hide_size(arm)
        bpy.context.view_layer.update()
        bpy.ops.export_scene.fbx(filepath=path)

    def export(stem):
        send.export(arm, os.path.join(out, stem + ".glb"))
        fbx_defaults(os.path.join(out, stem + ".default.fbx"))

    export("plain")

    names = [b.name for b in arm.data.bones]
    top = "MainBone" if "MainBone" in names else "Main"
    neck = "Neck.001" if "Neck.001" in names else next(n for n in names if n.lower().startswith("neck"))
    tails = [b for b in arm.data.bones if b.name.lower().startswith("tail") and len(b.children_recursive) >= 2]
    tail = tails[len(tails) // 2].name

    rig.start(arm, data)
    bpy.context.view_layer.update()
    main = arm.pose.bones[top]
    m = arm.matrix_world @ main.matrix
    m.translation = m.translation + Vector((0.0, 0.0, 0.1))
    main.matrix = arm.matrix_world.inverted() @ m
    bpy.context.view_layer.update()
    turned = arm.pose.bones[neck]
    turned.rotation_mode = "QUATERNION"
    turned.rotation_quaternion = Quaternion((1.0, 0.0, 0.0), math.radians(10.0)) @ turned.rotation_quaternion
    arm.pose.bones[tail].location += Vector((0.0, 0.05, 0.0))  # along the bone
    bpy.context.view_layer.update()
    rig.apply(arm, data)

    with open(os.path.join(out, "unity_rig.json"), "w", encoding="utf-8") as f:
        json.dump(rig.unity_rig(arm), f)
    export("edited")
    print("RIGEXPORT", out)
    ok = True
except Exception:
    traceback.print_exc()
sys.stdout.flush()
os._exit(0 if ok else 1)
