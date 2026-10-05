"""Imports a Tyrant project's model.glb looking like the game."""
import json
import os

import bpy

from . import growth, ik, materials, meshops, project


def import_project(path):
    """Imports the project into the current scene and returns its armature."""
    data = project.load(path)
    folder = os.path.dirname(path)
    source = data["source"]
    name = " ".join(p for p in ("Tyrant ·", source["species"], source.get("skin") or "") if p).strip()
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[collection.name]

    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(folder, "model.glb"), disable_bone_shape=True, bone_heuristic="BLENDER",
                              import_shading="NORMALS", merge_vertices=False, import_scene_as_collection=False)
    created = [o for o in bpy.data.objects if o not in before]
    armature = next((o for o in created if o.type == "ARMATURE"), None)
    is_object = source.get("kind") == "object"  # a fence, a building: meshes only, for reference or a new object
    if armature is None and not is_object:
        raise project.ProjectError("model.glb has no armature; open it again from Tyrant (Open in Blender → Start fresh).")
    if armature is not None:
        armature[project.TAG] = path
        armature.data.display_type = "OCTAHEDRAL"
        ik.remember_open_pose(armature)  # the prefab's pose, not the bind pose: Reset pose goes back to it
    for obj in created:
        if obj.type != "MESH":
            continue
        obj[project.TAG] = path
        meshops.merge_split_vertices(obj)
        meshops.mark_uv_seams(obj)

    if hasattr(materials, "apply_all"):
        materials.apply_all(created, data, folder)
    if armature is None:
        return next((o for o in created if o.type == "MESH"), None)
    if hasattr(type(armature), "tyrant_sex"):
        armature.tyrant_sex = "FEMALE" if data.get("sex") == "female" else "MALE"  # swaps the maps and the growth limit
    if hasattr(growth, "set_growth"):
        growth.set_growth(armature, 1.0)
    if data.get("ikOnOpen", True) and (data.get("ik") or {}).get("chains"):
        try:
            ik.add_controls(armature, data)
        except ik.IkError as ex:  # the model still opens; the Tyrant panel says why there are no controls
            armature[ik.SKIPPED] = json.dumps([str(ex)])
        except Exception as ex:  # noqa: BLE001 - a fault in building the controls must not stop the model opening
            armature[ik.SKIPPED] = json.dumps([f"The IK controls could not be built ({type(ex).__name__}: {ex}); the model "
                                               "opened without them. Try Add IK controls in this panel."])
    return armature
