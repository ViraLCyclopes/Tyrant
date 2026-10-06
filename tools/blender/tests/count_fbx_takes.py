# Imports an FBX into an empty scene and prints how many animations (Actions) came in: "TYRANT-TAKES <n> <names>".
import os
import sys

import bpy

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path, ignore_leaf_bones=True, automatic_bone_orientation=False)
names = sorted(a.name for a in bpy.data.actions)
print(f"TYRANT-TAKES {len(names)} {'|'.join(names)}", flush=True)
os._exit(0)
