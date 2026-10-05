"""Imports a Tyrant project's model.glb looking like the game."""
import os

import bpy

from . import growth, materials, meshops, project


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
    if armature is None:
        raise project.ProjectError("model.glb has no armature; open it again from Tyrant (Open in Blender → Start fresh).")
    armature[project.TAG] = path
    armature.data.display_type = "OCTAHEDRAL"
    for obj in created:
        if obj.type != "MESH":
            continue
        obj[project.TAG] = path
        meshops.merge_split_vertices(obj)
        meshops.mark_uv_seams(obj)

    if hasattr(materials, "apply_all"):
        materials.apply_all(created, data, folder)
    if hasattr(type(armature), "tyrant_sex"):
        armature.tyrant_sex = "FEMALE" if data.get("sex") == "female" else "MALE"  # swaps the maps and the growth limit
    if hasattr(growth, "set_growth"):
        growth.set_growth(armature, 1.0)
    return armature
