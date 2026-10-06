"""
The game's animations as Blender Actions. Tyrant decodes each clip into per-bone keys in the game's own terms (Unity space,
parent-relative); here each key becomes the bone's pose relative to the armature's rest, with the rig edit composed on it as
the game composes it, so a reshaped model plays as in game. The channels the game's growth owns are left to the Growth
slider, as the growth job overrides them in game. Every chain with IK controls is switched to FK in the Action, so the clip
plays exactly; Move to IK controls puts the feet and head on the controls for editing.
"""
import json
import os

import bpy
from mathutils import Matrix, Quaternion, Vector

from . import rig

CLIP_ID = "tyrant_clip_id"  # action: the game's clip id ("Carch|LocWalk")
RATE = "tyrant_frame_rate"
LOOPS = "tyrant_loops"
TRAVELS = "tyrant_travels"
IN_PLACE = "Tyrant in place"  # the travelling bone's constraint that hides the travel
NOTES = "tyrant_notes"  # action: what could not be used of its clip (JSON list)
REPORT = "tyrant_anim_report"  # armature: what the last animations loaded said (errors and notes, JSON list)
ONE = Vector((1.0, 1.0, 1.0))
_Y_UP = rig._GLTF_TO_BLENDER  # glTF (Y up) → Blender (Z up): +90 degrees about X


class AnimError(Exception):
    pass


# ---- reading the clip file ------------------------------------------------------------------------------------------

def _gltf(p, q, s):
    """A Unity-space local as glTF space (the X mirror), as mathutils values."""
    return Vector((-p[0], p[1], p[2])), Quaternion((q[3], q[0], -q[1], -q[2])), Vector(s)


def _channel(keys, kind):
    if not keys:
        return None
    if kind == "rotation":
        return [(k["time"], [k["x"], k["y"], k["z"], k["w"]]) for k in keys]
    return [(k["time"], [k["x"], k["y"], k["z"]]) for k in keys]


def _at(keys, t):
    """A channel's value at time t: linear between its keys (quaternions normalised), held past the ends."""
    if t <= keys[0][0]:
        return keys[0][1]
    if t >= keys[-1][0]:
        return keys[-1][1]
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t0 <= t <= t1:
            u = 0.0 if t1 == t0 else (t - t0) / (t1 - t0)
            if len(v0) == 4 and sum(a * b for a, b in zip(v0, v1)) < 0:
                v1 = [-v for v in v1]
            value = [a + (b - a) * u for a, b in zip(v0, v1)]
            if len(value) == 4:
                length = sum(v * v for v in value) ** 0.5 or 1.0
                value = [v / length for v in value]
            return value
    return keys[-1][1]


# ---- the armature's frames ------------------------------------------------------------------------------------------

def _growth_channels(data):
    """{bone: (translation owned, scale owned)} from the project's growth data."""
    out = {}
    for bone in ((data or {}).get("growth") or {}).get("bones") or []:
        out[bone["name"]] = (bool(bone.get("translation")), bool(bone.get("scale")))
    return out


def _rest_local(arm, name):
    bone = arm.data.bones[name]
    return bone.matrix_local if bone.parent is None else bone.parent.matrix_local.inverted() @ bone.matrix_local


def fcurves(arm, action):
    """The action's F-curves for this armature (Blender 5's slotted actions)."""
    from bpy_extras import anim_utils

    slot = arm.animation_data.action_slot if arm.animation_data and arm.animation_data.action == action else None
    if slot is None:
        slot = action.slots[0] if len(action.slots) else None
    if slot is None:
        return []
    bag = anim_utils.action_get_channelbag_for_slot(action, slot)
    return list(bag.fcurves) if bag else []


def _keys(action, arm, path, index, group, frames, values):
    curve = action.fcurve_ensure_for_datablock(arm, path, index=index, group_name=group)
    points = curve.keyframe_points
    if len(points):
        points.clear()
    points.add(len(frames))
    co = []
    for f, v in zip(frames, values):
        co += [f, v]
    points.foreach_set("co", co)
    for point in points:
        point.interpolation = "LINEAR"
    curve.update()


# ---- loading ---------------------------------------------------------------------------------------------------------

def load(arm, data, clip):
    """Creates (or replaces) the Action of one clip file's content and returns it (it is not made to play)."""
    name = clip.get("name") or clip.get("id") or "Animation"
    for old in [a for a in bpy.data.actions if a.get(CLIP_ID) == clip.get("id")]:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action[CLIP_ID] = clip.get("id") or name
    rate = float(clip.get("frameRate") or 30.0)
    action[RATE] = rate
    action[LOOPS] = bool(clip.get("loops"))
    action[TRAVELS] = bool(clip.get("travels"))
    length = float(clip.get("length") or 0.0)

    previous = arm.animation_data.action if arm.animation_data else None
    arm.animation_data_create()
    arm.animation_data.action = action  # the keys are made for this armature's slot

    table = rig.offsets(arm)
    sigma = rig._sigma(arm)
    roots = rig._root_names(arm)
    growth = _growth_channels(data)
    prefab = {r["name"]: r for r in (data or {}).get("rest") or [] if isinstance(r, dict) and r.get("name")}
    bones = arm.data.bones
    missing = []
    for track in clip.get("bones") or []:
        name_ = track["bone"]
        bone = bones.get(name_)
        pose_bone = arm.pose.bones.get(name_)
        if bone is None or pose_bone is None:
            missing.append(name_)
            continue
        channels = {k: _channel(track.get(k), k) for k in ("position", "rotation", "scale")}
        times = sorted({t for keys in channels.values() if keys for t, _ in keys})
        if not times:
            continue
        root = name_ in roots
        rest_p, rest_q, rest_s = _rest_local(arm, name_).decompose()
        rest = rig.compose_inverse(table[name_], (rest_p, rest_q, rest_s)) if name_ in table else (rest_p, rest_q, rest_s)
        # Channels the clip does not animate keep the prefab's own value in game (glTF terms, from Tyrant); without it, the
        # game's bind local (a root's is in Blender's Z-up space).
        if name_ in prefab:
            r = prefab[name_]
            rest_gltf = (Vector(r["position"]), Quaternion((r["rotation"][3], r["rotation"][0], r["rotation"][1], r["rotation"][2])), Vector(r["scale"]))
        else:
            rest_gltf = rig._trs(_Y_UP.inverted() @ rig._matrix(rest)) if root else rest
        frame_of = bone.matrix_local if bone.parent is None else bone.parent.matrix_local.inverted() @ bone.matrix_local
        to_basis = frame_of.inverted()
        parent_sigma = Matrix.Diagonal(sigma.get(bone.parent.name, ONE) if bone.parent is not None else ONE).to_4x4()
        own_sigma = Matrix.Diagonal(sigma.get(name_, ONE)).to_4x4().inverted()
        locations, rotations, scales = [], [], []
        last = None
        for t in times:
            p = _at(channels["position"], t) if channels["position"] else None
            q = _at(channels["rotation"], t) if channels["rotation"] else None
            s = _at(channels["scale"], t) if channels["scale"] else None
            gp, gq, gs = _gltf(p or [0, 0, 0], q or [0, 0, 0, 1], s or [1, 1, 1])
            local = (gp if p else rest_gltf[0], gq if q else rest_gltf[1], gs if s else rest_gltf[2])
            if root:
                # A root bone's local is in the armature's space, which Blender turned Z up: the whole local is turned (a
                # rig offset, between parent and bone, is conjugated instead; see rig._from_gltf_frame).
                local = rig._trs(_Y_UP @ rig._matrix(local))
            if name_ in table:
                local = rig.compose(table[name_], local)
            basis = to_basis @ parent_sigma @ rig._matrix(local) @ own_sigma
            location, rotation, scale = basis.decompose()
            if last is not None and rotation.dot(last) < 0:
                rotation = -rotation
            last = rotation
            locations.append(location)
            rotations.append(rotation)
            scales.append(scale)
        frames = [1.0 + t * rate for t in times]
        owns_position, owns_scale = growth.get(name_, (False, False))
        if pose_bone.rotation_mode != "QUATERNION":
            pose_bone.rotation_mode = "QUATERNION"
        if not owns_position:
            for i in range(3):
                _keys(action, arm, f'pose.bones["{name_}"].location', i, name_, frames, [v[i] for v in locations])
        for i in range(4):
            _keys(action, arm, f'pose.bones["{name_}"].rotation_quaternion', i, name_, frames, [v[i] for v in rotations])
        if not owns_scale:
            for i in range(3):
                _keys(action, arm, f'pose.bones["{name_}"].scale', i, name_, frames, [v[i] for v in scales])

    from . import ik

    for chain in ik.built(arm):
        control = arm.pose.bones.get(chain["target"])
        if control is not None and ik.IK_FK in control:
            control[ik.IK_FK] = 0.0
            control.keyframe_insert(f'["{ik.IK_FK}"]', frame=1, group=chain["name"])
    notes = [f"{action.name}: {', '.join(missing[:6])}{' and more' if len(missing) > 6 else ''} "
             f"{'is' if len(missing) == 1 else 'are'} not on this model, so {'it does' if len(missing) == 1 else 'they do'} not move."] if missing else []
    notes += [f"{action.name}: {note}" for note in clip.get("skipped") or []]
    action[NOTES] = json.dumps(notes)
    action.use_frame_range = True
    action.frame_start = 1
    action.frame_end = max(1.0, 1.0 + round(length * rate))
    if previous is not None and previous != action:
        arm.animation_data.action = previous
    return action


def load_files(arm, data, files, errors=()):
    """
    Loads Tyrant's clip files (written next to the project) as Actions; the first one plays. What Tyrant could not read
    (errors) and what each clip could not use go into the panel's report.
    """
    actions = []
    for file in files:
        with open(file, encoding="utf-8") as f:
            actions.append(load(arm, data, json.load(f)))
    if actions:
        play(arm, actions[0])
    lines = list(errors)
    for action in actions:
        lines += json.loads(action.get(NOTES) or "[]")
    arm[REPORT] = json.dumps(lines)
    return actions


def load_pending(arm, data, path):
    """The project's animation files (asked for at Open in Blender) not yet loaded as Actions, and what Tyrant reported."""
    folder_ = os.path.dirname(path)
    loaded = {a.get(CLIP_ID) for a in tyrant_actions()}
    pending = []
    for relative in (data or {}).get("animationFiles") or []:
        file = os.path.join(folder_, relative)
        if not os.path.isfile(file):
            continue
        with open(file, encoding="utf-8") as f:
            clip_id = json.load(f).get("id")
        if clip_id not in loaded:
            pending.append(file)
    errors = (data or {}).get("animationErrors") or []
    if pending or errors:
        load_files(arm, data, pending, errors)


def report_failure(arm, what, ex):
    arm[REPORT] = json.dumps([f"{what} could not be loaded ({type(ex).__name__}: {ex})."])


def play(arm, action):
    """Makes the Action play on the armature; the scene's frame range follows it (and its frame rate, the first time)."""
    arm.animation_data_create()
    arm.animation_data.action = action
    scene = bpy.context.scene
    scene.frame_start = int(action.frame_start)
    scene.frame_end = int(action.frame_end)
    rate = action.get(RATE)
    if rate and not scene.get("tyrant_fps_set"):
        scene.render.fps = int(round(rate))
        scene.render.fps_base = 1.0
        scene["tyrant_fps_set"] = True
    scene.frame_set(scene.frame_start)


def tyrant_actions():
    return [a for a in bpy.data.actions if a.get(CLIP_ID)]


# ---- in place ---------------------------------------------------------------------------------------------------------

def travel_bone(arm):
    bone = arm.data.bones.get("MainBone")
    if bone is not None:
        return bone
    roots = [b for b in arm.data.bones if b.parent is None]
    return roots[0].children[0] if roots and roots[0].children else (roots[0] if roots else None)


def set_in_place(arm, on):
    """Holds the travelling bone at its rest place horizontally (its keys are not changed); off shows the travel again."""
    bone = travel_bone(arm)
    if bone is None:
        return
    pose_bone = arm.pose.bones[bone.name]
    constraint = pose_bone.constraints.get(IN_PLACE)
    if constraint is None:
        constraint = pose_bone.constraints.new("LIMIT_LOCATION")
        constraint.name = IN_PLACE
        constraint.owner_space = "POSE"
        constraint.use_transform_limit = False
    head = bone.head_local
    constraint.use_min_x = constraint.use_max_x = constraint.use_min_y = constraint.use_max_y = True
    constraint.min_x = constraint.max_x = head.x
    constraint.min_y = constraint.max_y = head.y
    constraint.mute = not on


# ---- IK controls ------------------------------------------------------------------------------------------------------

def move_to_ik(arm, scene):
    """Keys the IK controls to follow the playing Action on every frame and switches its chains to IK."""
    from . import ik

    action = arm.animation_data.action if arm.animation_data else None
    if action is None:
        raise AnimError("No animation is playing: pick one in the Animations box first.")
    chains = ik.built(arm)
    if not chains:
        raise AnimError("This model has no IK controls: Add IK controls first.")
    pose = arm.pose.bones
    start, end = int(action.frame_range[0]), int(action.frame_range[1])
    original = scene.frame_current
    for chain in chains:
        control = pose.get(chain["target"])
        if control is not None:
            control[ik.IK_FK] = 0.0
            control.keyframe_insert(f'["{ik.IK_FK}"]', frame=start, group=chain["name"])
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        ik.snap_controls(arm)
        for chain in chains:
            for key in ("target", "pole", "look"):
                name = chain.get(key)
                control = pose.get(name) if name else None
                if control is None:
                    continue
                control.keyframe_insert("location", frame=frame, group=chain["name"])
                control.keyframe_insert(ik._rotation_path(control), frame=frame, group=chain["name"])
    for chain in chains:
        control = pose.get(chain["target"])
        if control is not None:
            control[ik.IK_FK] = 1.0
            control.keyframe_insert(f'["{ik.IK_FK}"]', frame=start, group=chain["name"])
    scene.frame_set(original)


# ---- the list -------------------------------------------------------------------------------------------------------

def ensure_list(arm, data, path):
    """The project's animation list; a project written before Tyrant listed them asks Tyrant for it."""
    from . import growth, send

    if (data or {}).get("animations") is not None:
        return data["animations"]
    if not send.trusted_tyrant(data.get("tyrant")):
        raise AnimError("Reopen this model from Tyrant to get its animations.")
    answer = send.run([data["tyrant"], "blender", "animation-list", "-w", data["workspace"], path], timeout=600)
    if not answer.get("ok"):
        raise AnimError("; ".join(answer.get("errors") or ["Tyrant did not answer."]))
    growth.forget(path)
    from . import project

    return project.load(path).get("animations") or []


def folder(path):
    return os.path.join(os.path.dirname(path), "animations")
