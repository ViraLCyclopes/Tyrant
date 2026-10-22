# blender -b --factory-startup --python export_model_run.py -- <project.json> <out dir>: import like Open in Blender, make a rig
# edit with Start / Apply (a bone moved, a neck bone turned, a tail bone scaled), run Export model for Tyrant… to model.glb and
# model.fbx, and write the rig edit Send would pass to unity_rig.json.
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
    from tyrant_blender import importer, project, rig

    args = sys.argv[sys.argv.index("--") + 1:]
    project_path, out = args[:2]
    os.makedirs(out, exist_ok=True)
    tyrant_blender.register()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = importer.import_project(project_path)
    data = project.load(project_path)

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
    arm.pose.bones[tail].scale = (1.3, 1.3, 1.3)
    bpy.context.view_layer.update()
    rig.apply(arm, data)

    with open(os.path.join(out, "unity_rig.json"), "w", encoding="utf-8") as f:
        json.dump(rig.unity_rig(arm), f)
    for fmt in ("glb", "fbx"):
        result = bpy.ops.tyrant.export_model(filepath=os.path.join(out, "model." + fmt), format=fmt)
        assert result == {"FINISHED"}, result
    print("EXPORTMODEL", out)
    ok = True
except Exception:
    traceback.print_exc()
sys.stdout.flush()
os._exit(0 if ok else 1)
