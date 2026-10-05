# blender -b --factory-startup --python paintsend.py -- <project.json> <out.glb>: import like Open in Blender, paint the
# diffuse without saving, export like Send and write the changed images; prints "PAINTSEND <json [[slot, png], ...]>".
import json
import os
import sys
import traceback

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ok = False
try:
    import bpy
    import numpy as np
    import tyrant_blender
    from tyrant_blender import images, importer, send

    project_path, out = sys.argv[sys.argv.index("--") + 1:][:2]
    tyrant_blender.register()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = importer.import_project(project_path)
    meshes = send.sendable(arm)[1:]
    image = meshes[0].active_material.node_tree.nodes["diffuse"].image
    image.pixels.foreach_set(np.full(image.size[0] * image.size[1] * 4, 0.5, dtype=np.float32))
    send.export(arm, out)
    changed, _ = images.collect(meshes, os.path.dirname(project_path))
    print("PAINTSEND " + json.dumps([[slot, png] for slot, png, _ in changed]))
    ok = True
except Exception:
    traceback.print_exc()
sys.stdout.flush()
os._exit(0 if ok else 1)
