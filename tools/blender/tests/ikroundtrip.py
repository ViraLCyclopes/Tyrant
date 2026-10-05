# blender -b --factory-startup --python ikroundtrip.py -- <ik project.json> <out.glb>: open with IK controls, pose, send.
import os
import sys
import traceback

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ok = False
try:
    import bpy
    import tyrant_blender
    from mathutils import Vector
    from tyrant_blender import ik, importer, send

    project_path, out = sys.argv[sys.argv.index("--") + 1:][:2]
    tyrant_blender.register()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = importer.import_project(project_path)
    assert ik.built(arm), "no IK controls were built"
    control = arm.pose.bones["ctrl_foot.L"]
    control.location = control.location + Vector((0.0, 0.1, 0.1))  # posed with IK: Send must still export the rest pose
    arm.pose.bones["ctrl_head"][ik.AIM] = 1.0
    arm.tyrant_growth = 0.3
    send.export(arm, out)
    print("IKROUNDTRIP", out)
    ok = True
except Exception:
    traceback.print_exc()
sys.stdout.flush()
os._exit(0 if ok else 1)
