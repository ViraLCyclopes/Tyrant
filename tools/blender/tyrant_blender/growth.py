"""
The Growth slider (0 baby – 1 adult), as AnimalGrowthManager grows an animal: the growth shape keys from the
blend-shape curve, the infant → adult maps from the skin curve, and the bone proportions (GrowthData) — those relative to
the adult, so Growth 1 is exactly the rest pose that is edited and sent. The curves come sampled from Tyrant; without a
data dump they are straight lines and bones keep their proportions. Growth sets only the location and scale the game's
growth data owns, never rotations, so poses and IK survive it.
"""
import os

from mathutils import Matrix, Quaternion, Vector

from . import project

_cache = {}


def sample(table, t):
    """The table's value at t (0–1), linear between its evenly spaced samples; no table = t itself."""
    t = min(max(t, 0.0), 1.0)
    if not table:
        return t
    x = t * (len(table) - 1)
    i = min(int(x), len(table) - 2)
    return table[i] + (table[i + 1] - table[i]) * (x - i)


def _remap(a, b, c, d, x):
    return c + (x - a) * (d - c) / (b - a)


def blend_maturity(table, value, clamp):
    """The blend-shape maturity at this growth: past 0.5 the game squeezes the rest of growth into 0.5..clamp (per sex)."""
    if value < 0.5:
        return sample(table, value)
    return sample(table, _remap(0.5, 1.0, 0.5, clamp, value))


def sex_of(armature):
    return "female" if getattr(armature, "tyrant_sex", "MALE") == "FEMALE" else "male"


def shape_values(m, relative):
    """The game's weights for its shape keys 0 and 1 at blend maturity m, as 0–1 (the game's weights stay within 0–100)."""
    key1 = _remap(0.0, 0.5, 100.0, 0.0, m)
    if relative:
        key0 = _remap(0.5, 1.0, 100.0, 0.0, m) if m > 0.5 else _remap(0.0, 0.5, 0.0, 100.0, m)
    else:
        key0 = _remap(0.5, 1.0, 100.0, 0.0, m)
    clamp = lambda w: round(min(max(w, 0.0), 100.0) / 100.0, 6)  # noqa: E731
    return clamp(key0), clamp(key1)


def forget(path):
    _cache.pop(path, None)


def _project(armature):
    path = armature.get(project.TAG)
    if not path or not project.is_local(path) or not os.path.isfile(path):
        return None
    stamp = os.path.getmtime(path)
    cached = _cache.get(path)
    if cached is None or cached[0] != stamp:
        cached = _cache[path] = (stamp, project.load(path))
    return cached[1]


def deformed_meshes(armature, scene_objects):
    return [o for o in scene_objects if o.type == "MESH" and (
        o.parent == armature or any(m.type == "ARMATURE" and m.object == armature for m in o.modifiers))]


def _stage(bone, value):
    """[px,py,pz,sx,sy,sz] at this growth, lerped baby → adolescent → adult as ApplyLoadedBoneProportions."""
    stages = (bone["baby"], bone["adolescent"], bone["adult"])
    first = 0 if value < 0.5 else 1
    t = value * 2 if value < 0.5 else value * 2 - 1
    return [a + (b - a) * t for a, b in zip(stages[first], stages[first + 1])]


def _basis(rest, bone, value):
    """The pose basis that turns the rest (adult) local transform into this growth's, relative to the game's adult stage."""
    target = _stage(bone, value)
    adult = bone["adult"]
    p = Vector(rest["position"])
    q = Quaternion((rest["rotation"][3], rest["rotation"][0], rest["rotation"][1], rest["rotation"][2]))
    s = Vector(rest["scale"])
    p2, s2 = p.copy(), s.copy()
    if bone.get("translation"):
        p2 = p + Vector(target[0:3]) - Vector(adult[0:3])
    if bone.get("scale"):
        s2 = Vector([sv * (t / a if abs(a) > 1e-8 else 1.0) for sv, t, a in zip(s, target[3:6], adult[3:6])])
    local = Matrix.LocRotScale(p, q, s)
    grown = Matrix.LocRotScale(p2, q, s2)
    # Blender's glTF importer keeps each bone's axes as the node's (bone rest = axis conversion @ node world), so the pose
    # basis, which lives in the bone's own frame, is the change of the node's local transform as it is.
    return local.inverted() @ grown


def clear_pose(armature):
    for pose_bone in armature.pose.bones:
        pose_bone.matrix_basis = Matrix.Identity(4)


def set_growth(armature, value, scene_objects=None):
    """Shows the animal at this growth (0 baby – 1 adult)."""
    import bpy

    data = _project(armature)
    growth = (data or {}).get("growth") or {}
    sexes = (data or {}).get("sexes") or {}
    clamp = float((sexes.get(sex_of(armature)) or {}).get("growthClamp", 1.0))
    m = blend_maturity(growth.get("blend"), value, clamp)
    key0, key1 = shape_values(m, growth.get("relative", True))
    maturity = sample(growth.get("skin"), value)
    meshes = deformed_meshes(armature, scene_objects if scene_objects is not None else bpy.context.scene.objects)
    for mesh in meshes:
        keys = mesh.data.shape_keys.key_blocks if mesh.data.shape_keys else []
        for index, weight in ((1, key0), (2, key1)):  # block 0 is the Basis; 1 and 2 are the game's keys 0 and 1
            if len(keys) > index:
                keys[index].value = weight
        for slot in mesh.material_slots:
            node = slot.material.node_tree.nodes.get("Maturity") if slot.material and slot.material.node_tree else None
            if node is not None:
                node.outputs[0].default_value = maturity

    from . import ik  # late: ik uses growth too

    rest = {r["name"]: r for r in (data or {}).get("rest", [])}
    opened = ik.open_pose(armature)
    channels = {}
    for bone in growth.get("bones") or []:
        pose_bone = armature.pose.bones.get(bone["name"])
        if pose_bone is None or bone["name"] not in rest:
            continue
        # The growth change is relative to the prefab's transforms, which the model opens posed in: applied on top of that
        # pose, it lands where the game puts the bone even when the bind pose differs.
        change = Matrix.Identity(4) if value >= 1.0 else _basis(rest[bone["name"]], bone, value)
        basis = opened.get(bone["name"], Matrix.Identity(4)) @ change
        # Only the channels the game's growth owns: rotations are yours (poses, IK, animation).
        location, _rotation, scale = basis.decompose()
        if bone.get("translation"):
            pose_bone.location = location
        if bone.get("scale"):
            pose_bone.scale = scale
        channels[bone["name"]] = (location if bone.get("translation") else None, scale if bone.get("scale") else None)
    armature["tyrant_growth_shown"] = value
    ik.follow_growth(armature, channels)
