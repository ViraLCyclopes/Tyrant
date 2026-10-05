# blender -b --factory-startup --python roundtrip.py -- <project.json> <out.glb>: import like Open in Blender, export like Send.
import os
import sys
import traceback

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ok = False
try:
    import bpy
    import tyrant_blender
    from tyrant_blender import importer, send

    project_path, out = sys.argv[sys.argv.index("--") + 1:][:2]
    tyrant_blender.register()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = importer.import_project(project_path)
    arm.tyrant_growth = 0.3  # Send must put it back to adult and export the rest pose
    send.export(arm, out)
    print("ROUNDTRIP", out)
    ok = True
except Exception:
    traceback.print_exc()
sys.stdout.flush()
os._exit(0 if ok else 1)
