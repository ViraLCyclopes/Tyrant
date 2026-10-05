"""tyrant-blender.json: read, write, and find the Tyrant armature in a scene."""
import json
import os

FILE_NAME = "tyrant-blender.json"
VERSION = 1
TAG = "tyrant_project"


class ProjectError(Exception):
    pass


def load(path):
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError) as ex:
        raise ProjectError(f"{path} could not be read ({ex}). Open it again from Tyrant (Open in Blender → Start fresh).") from ex
    if not isinstance(data, dict) or data.get("version") != VERSION or not all(k in data for k in ("source", "materials", "rest")):
        raise ProjectError("This project was made by another Tyrant version; open it again from Tyrant (Open in Blender → Start fresh).")
    return data


def save(path, data):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
    os.replace(tmp, path)


def tagged_armatures(scene):
    return [o for o in scene.objects if o.type == "ARMATURE" and o.get(TAG)]


def armature_of(context):
    """The active object's Tyrant armature (itself, its parent, or its Armature modifier's), else the only one in the scene."""
    obj = context.active_object
    if obj is not None:
        if obj.type == "ARMATURE" and obj.get(TAG):
            return obj
        if obj.type == "MESH":
            for mod in obj.modifiers:
                if mod.type == "ARMATURE" and mod.object is not None and mod.object.get(TAG):
                    return mod.object
            if obj.parent is not None and obj.parent.type == "ARMATURE" and obj.parent.get(TAG):
                return obj.parent
    found = tagged_armatures(context.scene)
    return found[0] if len(found) == 1 else None
