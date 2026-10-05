"""
Rig edits, like Cobra Tools' "Add Rig Edit Bones from Pose": Start, pose the game's bones (move, rotate, scale), Apply.
Tyrant records each changed bone's offset against the pose you started from, in its parent's space (what a JWE NODE bone
holds), bakes only that edit into the meshes and the rest pose, and puts your stance back on top. The offsets travel with
Send; the game composes them on its animations. Clear returns mesh and rest to the game's skeleton.

All maths is in each bone's own frame (Blender keeps a glTF node's axes as its bone's, as growth.py relies on). Blender's
rest pose cannot hold a bone's scale: the scale an edit gave a bone is kept aside (SIGMA) and put back into every sum.
"""
import json

import bpy
from mathutils import Matrix, Quaternion, Vector

RIG = "tyrant_rig"  # armature: {bone: [mx,my,mz, qw,qx,qy,qz, sx,sy,sz]} the offsets, parent-relative, Blender frames
GAME = "tyrant_rig_game"  # armature: {bone: 16 floats} each game bone's local in the game's own bind skeleton
START = "tyrant_rig_start"  # armature: the pose (true locals) a rig edit started from, while one is being made
SIGMA = "tyrant_rig_sigma"  # armature: {bone: [x,y,z]} the scale Blender's rest lost for that bone (product down the chain)
TOLERANCE = 1e-5
ONE = Vector((1.0, 1.0, 1.0))

# glTF (Y up) → Blender armature space (Z up): only bones without a parent bone are relative to it.
_GLTF_TO_BLENDER = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


class RigError(Exception):
    pass


# ---- storage -------------------------------------------------------------------------------------------------------

def _pack(offset):
    m, q, s = offset
    return [m.x, m.y, m.z, q.w, q.x, q.y, q.z, s.x, s.y, s.z]


def _unpack(v):
    return Vector(v[0:3]), Quaternion(v[3:7]), Vector(v[7:10])


def _flat(matrix):
    return [v for row in matrix for v in row]


def _unflat(values):
    return Matrix([values[0:4], values[4:8], values[8:12], values[12:16]])


def _load(arm, key):
    try:
        return json.loads(arm.get(key) or "{}")
    except (ValueError, TypeError):
        return {}


def offsets(arm):
    """{bone: (move, rotation, scale)} the armature's rig edit (Blender frames)."""
    try:
        return {k: _unpack(v) for k, v in _load(arm, RIG).items()}
    except (TypeError, IndexError):
        return {}


def _store(arm, table):
    arm[RIG] = json.dumps({k: _pack(v) for k, v in table.items()})


def _sigma(arm):
    return {k: Vector(v) for k, v in _load(arm, SIGMA).items()}


def editing(arm):
    return bool(arm.get(START))


# ---- the game's rule ------------------------------------------------------------------------------------------------

def _mul(a, b):
    return Vector((a.x * b.x, a.y * b.y, a.z * b.z))


def _div(a, b):
    return Vector((a.x / b.x, a.y / b.y, a.z / b.z))


def compose(offset, local):
    """position = m + q(s ⊙ p); rotation = q r; scale = s ⊙ sc (as the framework composes in game)."""
    m, q, s = offset
    p, r, sc = local
    return m + q @ _mul(s, p), q @ r, _mul(s, sc)


def compose_inverse(offset, local):
    m, q, s = offset
    p, r, sc = local
    qi = q.inverted()
    return _div(qi @ (p - m), s), qi @ r, _div(sc, s)


def _trs(matrix):
    return matrix.decompose()


def _matrix(trs):
    return Matrix.LocRotScale(trs[0], trs[1], trs[2])


def _identity(offset):
    m, q, s = offset
    return m.length < TOLERANCE and (s - ONE).length < TOLERANCE and abs(q.dot(Quaternion())) > 1 - TOLERANCE


# ---- the game's bones and their true locals ---------------------------------------------------------------------------

def _game_bones(arm):
    """The game's bones (Tyrant's IK bones left out), parents first."""
    from . import ik

    ours = {n for c in ik.built(arm) for n in c["bones"]} | {b.name for b in arm.data.bones if b.get(ik.BONE_TAG)}
    out = []

    def walk(bone):
        if bone.name in ours:
            return
        out.append(bone)
        for child in bone.children:
            walk(child)

    for bone in arm.data.bones:
        if bone.parent is None:
            walk(bone)
    return out


def _true_locals(arm, sigma, source="pose"):
    """{bone: 4x4} each game bone's local in the true (scaled) hierarchy, of the pose shown or of the rest pose."""
    bpy.context.view_layer.update()
    worlds, out = {}, {}
    for bone in _game_bones(arm):
        shown = arm.pose.bones[bone.name].matrix if source == "pose" else bone.matrix_local
        world = shown @ Matrix.Diagonal(sigma.get(bone.name, ONE)).to_4x4()
        worlds[bone.name] = world
        parent = bone.parent.name if bone.parent is not None and bone.parent.name in worlds else None
        out[bone.name] = world if parent is None else worlds[parent].inverted() @ world
    return out


def _pose_to(arm, locals_, sigma):
    """Poses every game bone so its true local is the given one (under the current rest and lost scales)."""
    bones = arm.data.bones
    worlds, shown = {}, {}
    for bone in _game_bones(arm):
        parent = bone.parent.name if bone.parent is not None and bone.parent.name in worlds else None
        world = locals_[bone.name] if parent is None else worlds[parent] @ locals_[bone.name]
        worlds[bone.name] = world
        shown[bone.name] = world @ Matrix.Diagonal(sigma.get(bone.name, ONE)).to_4x4().inverted()
    for bone in _game_bones(arm):
        parent = bone.parent
        if parent is None or parent.name not in shown:
            frame = bone.matrix_local
        else:
            frame = shown[parent.name] @ bones[parent.name].matrix_local.inverted() @ bone.matrix_local
        arm.pose.bones[bone.name].matrix_basis = frame.inverted() @ shown[bone.name]
    bpy.context.view_layer.update()
    return worlds


# ---- baking a pose into meshes and the rest pose -------------------------------------------------------------------

def _bake_meshes(arm):
    """Linear blend skinning of the pose shown into every mesh the armature deforms: its vertices and every shape key, as
    Blender's Armature modifier would place them (weights over the deforming bones, normalised by their sum)."""
    import numpy as np

    from . import growth

    bpy.context.view_layer.update()
    names = [b.name for b in arm.data.bones if b.use_deform]
    index = {n: i for i, n in enumerate(names)}
    skin = np.array([np.array(arm.pose.bones[n].matrix @ arm.data.bones[n].matrix_local.inverted()) for n in names]) if names else np.zeros((0, 4, 4))
    for obj in growth.deformed_meshes(arm, bpy.context.scene.objects):
        mesh = obj.data
        n = len(mesh.vertices)
        if n == 0:
            continue
        to_arm = np.array(arm.matrix_world.inverted() @ obj.matrix_world)
        from_arm = np.linalg.inv(to_arm)
        group_bone = {g.index: index[g.name] for g in obj.vertex_groups if g.name in index}
        rows, bones, weights = [], [], []
        for v in mesh.vertices:
            for g in v.groups:
                b = group_bone.get(g.group)
                if b is not None and g.weight > 0:
                    rows.append(v.index)
                    bones.append(b)
                    weights.append(g.weight)
        per_vertex = np.zeros((n, 4, 4))
        total = np.zeros(n)
        if rows:
            rows_a, bones_a, weights_a = np.array(rows), np.array(bones), np.array(weights)
            np.add.at(per_vertex, rows_a, weights_a[:, None, None] * skin[bones_a])
            np.add.at(total, rows_a, weights_a)
        unweighted = total <= 0
        per_vertex[unweighted] = np.eye(4)
        total[unweighted] = 1.0
        per_vertex /= total[:, None, None]
        whole = from_arm[None, :, :] @ per_vertex @ to_arm[None, :, :]

        def move(co):
            points = np.concatenate([co.reshape(n, 3), np.ones((n, 1))], axis=1)
            return np.einsum("nij,nj->ni", whole, points)[:, :3].reshape(-1)

        keys = mesh.shape_keys.key_blocks if mesh.shape_keys else []
        for key in keys:
            co = np.empty(n * 3)
            key.data.foreach_get("co", co)
            key.data.foreach_set("co", move(co))
        co = np.empty(n * 3)
        mesh.vertices.foreach_get("co", co)
        mesh.vertices.foreach_set("co", move(co))
        mesh.update()


def _apply_as_rest(arm):
    view_layer = bpy.context.view_layer
    active, mode = view_layer.objects.active, arm.mode
    selected = [o for o in view_layer.objects if o.select_get()]
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for obj in view_layer.objects:
        obj.select_set(obj == arm)
    view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    try:
        bpy.ops.pose.armature_apply(selected=False)
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
        for obj in view_layer.objects:
            obj.select_set(obj in selected)
        view_layer.objects.active = active
        if mode == "POSE" and active == arm:
            bpy.ops.object.mode_set(mode="POSE")


def _disconnect(arm):
    """Connected bones cannot move in Pose Mode: a rig edit moves them, so they are freed (their places do not change)."""
    if not any(b.use_connect for b in arm.data.bones):
        return
    view_layer = bpy.context.view_layer
    active, mode = view_layer.objects.active, arm.mode
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        for bone in arm.data.edit_bones:
            bone.use_connect = False
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
        view_layer.objects.active = active
        if mode == "POSE" and active == arm:
            bpy.ops.object.mode_set(mode="POSE")


def _reshape(arm, data, new, stance_game):
    """Makes the edited skeleton (the game's with the new offsets) the rest pose, meshes following, and shows the stance
    (the game's stance locals) with the new offsets on top. Growth must be at adult and the IK controls off."""
    from . import growth

    game = {k: _unflat(v) for k, v in _load(arm, GAME).items()}
    sigma = _sigma(arm)
    targets = {}
    for bone in _game_bones(arm):
        g = game.get(bone.name)
        if g is None:
            continue
        targets[bone.name] = _matrix(compose(new[bone.name], _trs(g))) if bone.name in new else g
    missing = [b.name for b in _game_bones(arm) if b.name not in targets]
    if missing:
        raise RigError(f"{missing[0]} is not one of the game's bones Tyrant knows: Clear rig edit is not possible here; open the model again from Tyrant (Start fresh).")
    worlds = _pose_to(arm, targets, sigma)
    _bake_meshes(arm)
    _apply_as_rest(arm)
    new_sigma = {}
    for name, world in worlds.items():
        scale = world.to_scale()
        if (scale - ONE).length > TOLERANCE:
            new_sigma[name] = list(scale)
    arm[SIGMA] = json.dumps(new_sigma)
    _store(arm, new)
    stance = {name: (_matrix(compose(new[name], stance_game[name])) if name in new else _matrix(stance_game[name])) for name in stance_game}
    _pose_to(arm, stance, {k: Vector(v) for k, v in new_sigma.items()})
    growth.remember_base(arm, data)


def _ready(arm, data):
    """Growth at adult and the IK controls out of the way; returns what to put back."""
    from . import growth, ik

    shown = getattr(arm, "tyrant_growth", 1.0)
    growth.set_growth(arm, 1.0)
    had_ik = bool(ik.built(arm))
    if had_ik:
        ik.remove_controls(arm)
    _disconnect(arm)
    if not arm.get(GAME):
        rest = _true_locals(arm, _sigma(arm), source="rest")
        arm[GAME] = json.dumps({k: _flat(v) for k, v in rest.items()})
    return shown, had_ik


def _restore(arm, data, shown, had_ik):
    from . import growth, ik

    if had_ik:
        try:
            ik.add_controls(arm, data)
        except ik.IkError as ex:
            arm[ik.SKIPPED] = json.dumps([str(ex)])
    growth.set_growth(arm, shown)


# ---- the operations ------------------------------------------------------------------------------------------------

def start(arm, data=None):
    """Remembers the pose shown (at adult, IK controls off) as where the rig edit starts."""
    if editing(arm):
        raise RigError("A rig edit is already started: Apply it or Cancel it first.")
    shown, had_ik = _ready(arm, data)
    reference = _true_locals(arm, _sigma(arm))
    arm[START] = json.dumps({"locals": {k: _flat(v) for k, v in reference.items()}, "growth": shown, "ik": had_ik})


def _started(arm):
    state = _load(arm, START)
    if not state:
        raise RigError("No rig edit is started: press Start rig edit first.")
    return state


def apply(arm, data):
    """Records the bones changed since Start as offsets (added to the earlier ones) and makes them the rest pose."""
    state = _started(arm)
    reference = {k: _unflat(v) for k, v in state["locals"].items()}
    now = _true_locals(arm, _sigma(arm))
    old = offsets(arm)
    new = {}
    for name, start_local in reference.items():
        if name not in now:
            continue
        delta = now[name] @ start_local.inverted()
        before = _matrix(old[name]) if name in old else Matrix.Identity(4)
        offset = _trs(delta @ before)
        if not _identity(offset):
            new[name] = offset
    stance_game = {name: (compose_inverse(old[name], _trs(local)) if name in old else _trs(local)) for name, local in reference.items()}
    _reshape(arm, data, new, stance_game)
    del arm[START]
    _restore(arm, data, state.get("growth", 1.0), state.get("ik", False))


def cancel(arm, data=None):
    """Puts the pose back as it was at Start; nothing is recorded."""
    state = _started(arm)
    _pose_to(arm, {k: _unflat(v) for k, v in state["locals"].items()}, _sigma(arm))
    del arm[START]
    _restore(arm, data, state.get("growth", 1.0), state.get("ik", False))


def clear(arm, data):
    """Returns mesh and rest pose to the game's skeleton; the stance shown stays (without the edit)."""
    if editing(arm):
        raise RigError("Apply or Cancel the rig edit first.")
    old = offsets(arm)
    if not old:
        raise RigError("This armature has no rig edit.")
    shown, had_ik = _ready(arm, data)
    now = _true_locals(arm, _sigma(arm))
    stance_game = {name: (compose_inverse(old[name], _trs(local)) if name in old else _trs(local)) for name, local in now.items()}
    _reshape(arm, data, {}, stance_game)
    _restore(arm, data, shown, had_ik)


def adopt(arm, data):
    """On import: a model that already wears a rig edit. Baked (the model was made for it): the rest pose is the edited
    skeleton, so only the offsets and the game's skeleton are worked out. Not baked (the game's mesh): the edit is applied."""
    table = from_unity((data or {}).get("rig") or {})
    if not table:
        return
    if (data or {}).get("rigBaked"):
        _store(arm, table)
        sigma = {}
        for bone in _game_bones(arm):
            parent = sigma.get(bone.parent.name, ONE) if bone.parent is not None else ONE
            own = table[bone.name][2] if bone.name in table else ONE
            total = _mul(parent, own)
            if (total - ONE).length > TOLERANCE:
                sigma[bone.name] = total
        arm[SIGMA] = json.dumps({k: list(v) for k, v in sigma.items()})
        rest = _true_locals(arm, sigma, source="rest")
        game = {k: (_matrix(compose_inverse(table[k], _trs(v))) if k in table else v) for k, v in rest.items()}
        arm[GAME] = json.dumps({k: _flat(v) for k, v in game.items()})
        return
    shown, had_ik = _ready(arm, data)
    now = _true_locals(arm, _sigma(arm))
    _reshape(arm, data, {k: v for k, v in table.items() if k in now}, {k: _trs(v) for k, v in now.items()})
    _restore(arm, data, shown, had_ik)


# ---- Unity --------------------------------------------------------------------------------------------------------

def _root_names(arm):
    return {b.name for b in _game_bones(arm) if b.parent is None}


def _to_gltf_frame(offset):
    """A root bone's offset is in Blender's armature space (Z up); the game's is in glTF's (Y up)."""
    conjugated = _GLTF_TO_BLENDER.inverted() @ _matrix(offset) @ _GLTF_TO_BLENDER
    return _trs(conjugated)


def _from_gltf_frame(offset):
    return _trs(_GLTF_TO_BLENDER @ _matrix(offset) @ _GLTF_TO_BLENDER.inverted())


def to_unity(table, roots=()):
    """mod.json's "rig" (Unity space): glTF mirrors Unity's X axis."""
    out = {}
    for name, offset in table.items():
        m, q, s = _to_gltf_frame(offset) if name in roots else offset
        out[name] = {"move": [-m.x, m.y, m.z], "rotate": [q.x, -q.y, -q.z, q.w], "scale": [s.x, s.y, s.z]}
    return out


def from_unity(rig, roots=()):
    out = {}
    for name, v in (rig or {}).items():
        m = v.get("move", [0, 0, 0])
        r = v.get("rotate", [0, 0, 0, 1])
        s = v.get("scale", [1, 1, 1])
        offset = (Vector((-m[0], m[1], m[2])), Quaternion((r[3], r[0], -r[1], -r[2])), Vector(s))
        out[name] = _from_gltf_frame(offset) if name in roots else offset
    return out


def unity_rig(arm):
    """The armature's rig edit as mod.json writes it (what Send passes)."""
    return to_unity(offsets(arm), _root_names(arm))
