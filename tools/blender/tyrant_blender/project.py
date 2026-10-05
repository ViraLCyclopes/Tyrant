"""tyrant-blender.json: read, write, and find the Tyrant armature in a scene."""
import json
import os

FILE_NAME = "tyrant-blender.json"
VERSION = 1
TAG = "tyrant_project"


class ProjectError(Exception):
    pass


_AGAIN = "Open it again from Tyrant (Open in Blender → Start fresh)."


def is_local(path):
    """False for network paths: a .blend from someone else must not make Blender reach out to a share."""
    return isinstance(path, str) and not path.startswith(("\\\\", "//"))


def _numbers(value, count):
    return isinstance(value, list) and len(value) == count and all(isinstance(v, (int, float)) for v in value)


def _valid(data):
    source = data.get("source")
    if not isinstance(source, dict) or not isinstance(source.get("species"), str):
        return False
    if not isinstance(data.get("tyrant"), str) or not isinstance(data.get("workspace"), str):
        return False
    materials = data.get("materials")
    if not isinstance(materials, dict) or not all(isinstance(m, dict) and isinstance(m.get("maps", {}), dict) for m in materials.values()):
        return False
    rest = data.get("rest")
    if not isinstance(rest, list) or not all(isinstance(r, dict) and isinstance(r.get("name"), str) and _numbers(r.get("position"), 3)
                                             and _numbers(r.get("rotation"), 4) and _numbers(r.get("scale"), 3) for r in rest):
        return False
    sexes = data.get("sexes")
    if sexes is not None and not (isinstance(sexes, dict) and all(
            isinstance(sexes.get(k), dict) and isinstance(sexes[k].get("growthClamp"), (int, float)) for k in ("male", "female"))):
        return False
    keys = data.get("growthKeys")
    if keys is not None and not (isinstance(keys, list) and all(isinstance(k, str) for k in keys)):
        return False
    if data.get("ik") is not None and not _valid_ik(data["ik"]):
        return False
    growth = data.get("growth")
    if growth is None:
        return True
    if not isinstance(growth, dict) or not isinstance(growth.get("blend"), list) or not isinstance(growth.get("skin"), list):
        return False
    return all(isinstance(b, dict) and isinstance(b.get("name"), str) and all(_numbers(b.get(k), 6) for k in ("baby", "adolescent", "adult"))
               for b in growth.get("bones") or [])


def _valid_ik(ik):
    chains = ik.get("chains") if isinstance(ik, dict) else None
    if not isinstance(chains, list):
        return False
    for chain in chains:
        if not isinstance(chain, dict) or chain.get("kind") not in ("limb", "head") or not isinstance(chain.get("name"), str):
            return False
        joints = chain.get("joints")
        if not isinstance(joints, list) or not all(isinstance(j, dict) and isinstance(j.get("name"), str) for j in joints):
            return False
        controls = chain.get("controls")
        if not _numbers(chain.get("endOffset"), 3) or not isinstance(controls, dict) or not isinstance(controls.get("target"), str):
            return False
        for force in chain.get("forces") or []:
            if not (isinstance(force, dict) and isinstance(force.get("joint"), str) and isinstance(force.get("bone"), str)
                    and _numbers(force.get("direction"), 3) and isinstance(force.get("strength"), (int, float))):
                return False
    return True


def load(path):
    if not is_local(path):
        raise ProjectError(f"{path} is on a network share; Tyrant only opens projects on this PC. {_AGAIN}")
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError) as ex:
        raise ProjectError(f"{path} could not be read ({ex}). {_AGAIN}") from ex
    if not isinstance(data, dict) or data.get("version") != VERSION or not _valid(data):
        raise ProjectError(f"This project was made by another Tyrant version or was changed by hand. {_AGAIN}")
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
