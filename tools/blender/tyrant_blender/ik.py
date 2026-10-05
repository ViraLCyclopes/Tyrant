"""
IK controls built from the game's FABRIK chains (the project's "ik"), as bones inside the Tyrant armature: a foot (hand)
control and a knee (elbow) pole per leg, a head control and an aim target for the head. Blender's IK solver is not FABRIK:
poses are close to the game's, not identical.

Per chain, e.g. the left foot: mch_grow_foot.L (follows Growth) → ctrl_foot.L (yours, flat on the ground contact point) →
mch_tip_foot.L (the chain's last joint as it stands: the IK target, and the joint copies its rotation). mch_end_foot.L, a
child of the joint before the last, reaches from that joint to the last one and carries the IK constraint: Blender's IK
reaches with a bone's tail, and the game's bones do not point at their child.

The controls are always built at rest (the bind pose: what Send exports and Clear Transform returns to), so the poles are
set up for it. A model opens in the game's prefab stance: the other bones keep it and the chains show it through their
controls (their joints stay at rest), so clearing the pose returns to the exact rest. Growth then moves the controls by
how far the chain ends move.
"""
import json
import math

import bpy
from mathutils import Matrix, Vector

TAG = "tyrant_ik"  # armature: JSON list of the chains built
SKIPPED = "tyrant_ik_skipped"  # armature: JSON list of the chains left out, with why
REFERENCE = "tyrant_ik_reference"  # armature: the pose bases (at Growth 1) the controls were built from
POINTS = "tyrant_ik_points"  # armature: where each control stood (at Growth 1) when that reference was taken
OLD_OPEN_POSE = "tyrant_open_pose"  # armature: left by the build that opened models in the game's prefab pose
OLD_CONTROLS = "tyrant_ik_old_pose"  # armature: these controls were built on that prefab pose (rebuild them at rest)
BONE_TAG = "tyrant_ik"  # bone: one of Tyrant's IK bones
CONTROLS = "Tyrant IK"
MECHANISM = "Tyrant IK (mechanism)"
PREFIX = "Tyrant "  # constraint names
IK_FK = "ik_fk"
AIM = "aim"
FORWARD = Vector((0.0, -1.0, 0.0))  # animals face -Y in Blender (Unity +Z → glTF +Z → Blender -Y)
POLE_DISTANCE = 0.4  # of the leg's length, in front of the knee: close enough to grab, clear of the mesh
UP = Matrix.Rotation(math.pi / 2, 4, "X")  # a bone pointing up (+Z): flat custom shapes lie on the ground
PALETTES = {"L": "THEME04", "R": "THEME01"}  # blue, red; heads yellow
TRACK_AXES = ("TRACK_X", "TRACK_NEGATIVE_X", "TRACK_Y", "TRACK_NEGATIVE_Y", "TRACK_Z", "TRACK_NEGATIVE_Z")


class IkError(Exception):
    pass


def built(arm):
    try:
        return json.loads(arm.get(TAG) or "[]")
    except ValueError:
        return []


def skipped(arm):
    try:
        return json.loads(arm.get(SKIPPED) or "[]")
    except ValueError:
        return []


def refresh(arm):
    """After a custom property changed (IK/FK, Aim): Blender re-runs the drivers only once the armature is tagged."""
    arm.update_tag()
    bpy.context.view_layer.update()


def flat(matrix):
    return [v for row in matrix for v in row]


def unflat(values):
    return Matrix([values[0:4], values[4:8], values[8:12], values[12:16]])


def _matrices(arm, key):
    try:
        return {name: unflat(values) for name, values in json.loads(arm.get(key) or "{}").items()}
    except (ValueError, TypeError, IndexError):
        return {}


def posed(basis):
    location, rotation, scale = basis.decompose()
    return rotation.angle > 1e-6 or location.length > 1e-6 or (scale - Vector((1.0, 1.0, 1.0))).length > 1e-6


def clear_scale_noise(arm, names=None):
    """The prefab's own transforms carry tiny non-uniform scales (under 1%) that Blender's IK cannot follow: they go."""
    for pose_bone in arm.pose.bones:
        if names is not None and pose_bone.name not in names:
            continue
        off = [abs(v - 1.0) for v in pose_bone.scale]
        if max(off) < 0.01 and max(off) > 1e-7:
            pose_bone.scale = (1.0, 1.0, 1.0)


# --- where the bones go (armature space, in the pose shown) -----------------------------------------------------------------

def _off_line(vector, line):
    return vector - line * vector.dot(line)


def _pole_point(chain, joints, length, pose):
    """In front of the knee (elbow): the game's pull on it, else the way the leg bends now, else forward."""
    root, knee, tip = (pose[n].translation for n in (joints[0], joints[1], joints[-1]))
    line = (tip - root).normalized() if (tip - root).length > 1e-6 else Vector((0.0, 0.0, -1.0))
    pull = Vector()
    for force in chain.get("forces") or []:
        if force["joint"] == joints[1] and force["bone"] in pose:
            pull += (pose[force["bone"]].to_3x3() @ Vector(force["direction"])).normalized() * float(force["strength"])
    direction = _off_line(pull, line)
    if direction.length < 1e-4:
        direction = _off_line(knee - root, line)
    if direction.length < 1e-4:
        direction = _off_line(FORWARD, line)
    return knee + direction.normalized() * length * POLE_DISTANCE


def _look_point(joints, pose):
    """A few neck lengths along the head bone's axis that points most forward, and that axis for the Damped Track."""
    m = pose[joints[-1]].to_3x3()
    axes = (m.col[0], -m.col[0], m.col[1], -m.col[1], m.col[2], -m.col[2])
    best = max(range(6), key=lambda i: axes[i].normalized().dot(FORWARD))
    head = pose[joints[-1]].translation
    neck = (head - pose[joints[-2]].translation).length
    return head + axes[best].normalized() * max(3.0 * neck, 0.5), TRACK_AXES[best]


def _plan(arm, chain, pose):
    """Where one chain's bones go, or why it is skipped."""
    bones = arm.data.bones
    joints = [j["name"] for j in chain["joints"]]
    missing = next((n for n in joints if n not in bones), None)
    if missing:
        return None, f"{chain['name']} skipped: no bone '{missing}'"
    game_tip = joints[-1]
    # The game's last joint may sit on the one before it (Stegosaurus' hands, the end offset gives the reach): the joint
    # before carries the end then.
    while len(joints) > 2 and (pose[joints[-1]].translation - pose[joints[-2]].translation).length < 1e-5:
        joints = joints[:-1]
    if len(joints) < 3:
        return None, f"{chain['name']} skipped: it has only {len(joints)} joints"
    length = sum((pose[a].translation - pose[b].translation).length for a, b in zip(joints, joints[1:]))
    plan = {"chain": chain, "joints": joints, "length": length, "size": max(length * 0.15, 0.05),
            "anchor": pose[game_tip] @ Vector(chain["endOffset"])}
    if chain["kind"] == "limb" and chain["controls"].get("pole"):
        plan["pole"] = _pole_point(chain, joints, length, pose)
    if chain["kind"] == "head" and chain["controls"].get("look"):
        plan["look"], plan["track"] = _look_point(joints, pose)
    return plan, None


# --- building ----------------------------------------------------------------------------------------------------------

def _shape(kind):
    """A custom shape mesh (circle, sphere, box, cross) of size 1, shared by every Tyrant rig; never linked to a scene."""
    import bmesh

    name = f"Tyrant IK {kind}"
    obj = bpy.data.objects.get(name)
    if obj is not None and obj.type == "MESH":
        return obj
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    if kind == "circle":  # lies in the bone's X-Z plane: flat on the ground for a control that points up
        bmesh.ops.create_circle(bm, cap_ends=False, segments=24, radius=1.0)
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0.0, 0.0, 0.0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
    elif kind == "sphere":
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.5)
    elif kind == "box":
        bmesh.ops.create_cube(bm, size=1.0)
    else:
        for axis in ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)):
            bm.edges.new((bm.verts.new(Vector(axis)), bm.verts.new(-Vector(axis))))
    bm.to_mesh(mesh)
    bm.free()
    return bpy.data.objects.new(name, mesh)


def _new(arm, name, parent, to_rest, shown, length):
    """A bone that shows as `shown` (armature space) while its parent is posed as now; to_rest maps the pose shown into the
    bone's rest space (its parent's rest @ the parent's pose⁻¹)."""
    bone = arm.data.edit_bones.new(name)
    head = shown.translation
    bone.head = to_rest @ head
    bone.tail = to_rest @ (head + shown.col[1].xyz.normalized() * length)
    bone.align_roll(to_rest.to_3x3() @ shown.col[2].xyz.normalized())
    bone.parent = parent
    bone.use_deform = False
    bone[BONE_TAG] = True
    return bone


def _edit_bones(arm, plan, root, rest, pose):
    """Creates one chain's bones (edit mode); returns their names by role and the matrices they show (armature space)."""
    edit = arm.data.edit_bones
    chain, joints, size = plan["chain"], plan["joints"], plan["size"]
    # Every bone of ours hangs from the root, directly or through ours (which are not posed): one map into rest space.
    root_map = rest[root] @ pose[root].inverted()
    made, shown = {}, {}

    def control(key, grow_key, name, at, length):
        stem = name[len("ctrl_"):] if name.startswith("ctrl_") else name
        matrix = Matrix.Translation(at) @ UP
        grow = _new(arm, f"mch_grow_{stem}", edit[root], root_map, matrix, length)
        bone = _new(arm, name, grow, root_map, matrix, length)
        made[key], made[grow_key] = bone.name, grow.name
        shown[key] = shown[grow_key] = matrix
        return bone, stem

    target, stem = control("target", "grow", chain["controls"]["target"], plan["anchor"], size)
    tip = Matrix.LocRotScale(pose[joints[-1]].translation, pose[joints[-1]].to_quaternion(), None)
    made["tip"] = _new(arm, f"mch_tip_{stem}", target, root_map, tip, max(edit[joints[-1]].length, 0.01)).name
    shown["tip"] = tip
    start, end = pose[joints[-2]].translation, pose[joints[-1]].translation
    helper = Matrix.Translation(start) @ (end - start).to_track_quat("Y", "Z").to_matrix().to_4x4()
    made["end"] = _new(arm, f"mch_end_{stem}", edit[joints[-2]], rest[joints[-2]] @ pose[joints[-2]].inverted(), helper, (end - start).length).name
    if "pole" in plan:
        control("pole", "pole_grow", chain["controls"]["pole"], plan["pole"], size * 0.5)
    if "look" in plan:
        control("look", "look_grow", chain["controls"]["look"], plan["look"], size * 0.5)
    return made, shown


def _drive(arm, constraint, bone, key, scale):
    """The constraint's influence follows bone[key] (a simple expression: it runs without Auto Run Python Scripts)."""
    driver = constraint.driver_add("influence").driver
    driver.type = "SCRIPTED"
    variable = driver.variables.new()
    variable.name = "v"
    variable.type = "SINGLE_PROP"
    variable.targets[0].id_type = "OBJECT"
    variable.targets[0].id = arm
    variable.targets[0].data_path = f'pose.bones["{bone}"]["{key}"]'
    driver.expression = "v" if abs(scale - 1.0) < 1e-6 else f"v * {scale:.6g}"


def _solve_pole_angle(ik_constraint, arm, joints, pose):
    """The pole angle that leaves the chain as it stands. Blender's rule depends on where each bone's tail points and on
    the pose above the chain, so it is found by trying: a 5° scan, then a golden-section search."""

    def drift(angle):
        ik_constraint.pole_angle = angle
        bpy.context.view_layer.update()
        return max(abs(a - b) for n in joints for ra, rb in zip(arm.pose.bones[n].matrix, pose[n]) for a, b in zip(ra, rb))

    best = min(range(-180, 180, 5), key=lambda d: drift(math.radians(d)))
    lo, hi = math.radians(best - 5), math.radians(best + 5)
    ratio = (5 ** 0.5 - 1) / 2
    for _ in range(40):
        a, b = hi - ratio * (hi - lo), lo + ratio * (hi - lo)
        if drift(a) < drift(b):
            hi = b
        else:
            lo = a
    ik_constraint.pole_angle = (lo + hi) / 2


def _pose_setup(arm, plan, made, pose):
    bones = arm.pose.bones
    chain, joints = plan["chain"], plan["joints"]
    ctrl = bones[made["target"]]
    ctrl[IK_FK] = 1.0
    ctrl.id_properties_ui(IK_FK).update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0,
                                        description="1: the control moves the chain (IK). 0: rotate the bones yourself (FK)")
    end = bones[made["end"]]
    end.lock_ik_x = end.lock_ik_y = end.lock_ik_z = True  # rides on its joint; only the joints above it turn
    solver = end.constraints.new("IK")
    solver.name = PREFIX + "IK"
    solver.target, solver.subtarget = arm, made["tip"]
    solver.use_tail = True
    solver.chain_count = len(joints)  # the helper and every joint but the last
    _drive(arm, solver, made["target"], IK_FK, float(chain.get("influence", 1.0)))
    if made.get("pole"):
        solver.pole_target, solver.pole_subtarget = arm, made["pole"]
        _solve_pole_angle(solver, arm, joints[:-1], pose)
    if chain["kind"] == "limb" or chain.get("matchHeadRotation"):
        rotation = bones[joints[-1]].constraints.new("COPY_ROTATION")
        rotation.name = PREFIX + "rotation"
        rotation.target, rotation.subtarget = arm, made["tip"]
        _drive(arm, rotation, made["target"], IK_FK, 1.0)
    if made.get("look"):
        ctrl[AIM] = 0.0
        ctrl.id_properties_ui(AIM).update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0,
                                          description="How much the head turns toward its aim target (ctrl_look)")
        aim = bones[joints[-1]].constraints.new("DAMPED_TRACK")
        aim.name = PREFIX + "aim"
        aim.target, aim.subtarget = arm, made["look"]
        aim.track_axis = plan["track"]
        _drive(arm, aim, made["target"], AIM, 1.0)


def _style(arm, plan, made):
    data = arm.data
    controls = data.collections.get(CONTROLS) or data.collections.new(CONTROLS)
    mechanism = data.collections.get(MECHANISM) or data.collections.new(MECHANISM)
    mechanism.is_visible = False
    palette = PALETTES.get(plan["chain"].get("side"), "THEME09")
    shapes = {"target": "box" if plan["chain"]["kind"] == "head" else "circle", "pole": "sphere", "look": "cross"}
    for key, name in made.items():
        bone = data.bones[name]
        for other in list(bone.collections):
            other.unassign(bone)
        if key in shapes:
            controls.assign(bone)
            pose_bone = arm.pose.bones[name]
            pose_bone.custom_shape = _shape(shapes[key])
            pose_bone.color.palette = palette
            bone.show_wire = True
        else:
            mechanism.assign(bone)
            bone.hide = True


def _ancestors(arm, names):
    out = set()
    for name in names:
        bone = arm.data.bones[name]
        while bone is not None and bone.name not in out:
            out.add(bone.name)
            bone = bone.parent
    return out


def _record(plan, made, shown, pose):
    joints = plan["joints"]
    follow = [[made["grow"], joints[-1], made["tip"]]]  # the foot follows by the heel itself (its spot shrinks with the leg)
    if made.get("pole"):
        follow.append([made["pole_grow"], joints[1], made["pole"]])
    if made.get("look"):
        follow.append([made["look_grow"], joints[-1], made["look"]])
    chain = plan["chain"]
    # The matrices the controls showed and the joints they hang from, when built: Snap keeps those relations.
    shapes = {key: flat(shown[key]) for key in ("target", "pole", "look") if key in shown}
    shapes["tipJoint"] = flat(pose[joints[-1]])
    shapes["knee"] = flat(pose[joints[1]])
    return {"name": chain["name"], "kind": chain["kind"], "side": chain.get("side"), "joints": joints,
            "target": made["target"], "pole": made.get("pole"), "look": made.get("look"), "tip": made["tip"], "end": made["end"],
            "bones": list(made.values()), "follow": follow, "shown": shapes}


def add_controls(arm, data):
    """Builds the controls of every chain in the project's "ik" that fits this armature and returns the skipped chains'
    reasons. Built from the pose shown (at Growth 1, which is put back after), so no bone moves."""
    from . import growth

    if built(arm):
        raise IkError("Already has IK controls (Remove IK controls first).")
    chains = ((data or {}).get("ik") or {}).get("chains") or []
    if not chains:
        raise IkError("This model has no IK chains in the game.")
    view_layer = bpy.context.view_layer
    if view_layer.objects.get(arm.name) is None:
        raise IkError(f"{arm.name} is in a collection excluded from the view layer; tick it in the Outliner first.")

    shown_growth = getattr(arm, "tyrant_growth", 1.0)
    growth.set_growth(arm, 1.0)
    shown_pose = {b.name: b.matrix_basis.copy() for b in arm.pose.bones}
    for pose_bone in arm.pose.bones:  # built at rest: the poles are set up for the pose Clear Transform returns to
        pose_bone.matrix_basis = Matrix.Identity(4)
    view_layer.update()
    pose = {b.name: b.matrix.copy() for b in arm.pose.bones}
    rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    plans, reasons = [], []
    for chain in chains:
        plan, why = _plan(arm, chain, pose)
        if why:
            reasons.append(why)
        else:
            plans.append(plan)
    if not plans:
        for name, basis in shown_pose.items():
            arm.pose.bones[name].matrix_basis = basis
        growth.set_growth(arm, shown_growth)
        game = [r["name"] for r in (data or {}).get("rest") or [] if isinstance(r, dict) and r.get("name")]
        missing = [n for n in game if n not in arm.data.bones]
        if game and len(missing) > len(game) / 2:
            raise IkError(f"This armature's bones no longer have the game's names (e.g. '{missing[0]}' is missing): Blender's "
                          "Armature > Names > Auto-Name renames them, and Send needs the game's names too. Undo the rename "
                          "(Ctrl+Z) or open the model again with Start fresh in Tyrant.")
        raise IkError("No IK chain fits this armature: " + "; ".join(reasons))

    active, mode = view_layer.objects.active, arm.mode
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    view_layer.objects.active = arm
    root = arm.data.bones[plans[0]["joints"][0]]
    while root.parent is not None:
        root = root.parent
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        made = [_edit_bones(arm, plan, root.name, rest, pose) for plan in plans]
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    for plan, (names, _shown) in zip(plans, made):
        _pose_setup(arm, plan, names, pose)
        _style(arm, plan, names)
    arm[TAG] = json.dumps([_record(plan, names, shown, pose) for plan, (names, shown) in zip(plans, made)])
    arm[SKIPPED] = json.dumps(reasons)
    arm.pop(OLD_OPEN_POSE, None)
    # The pose shown before comes back: the other bones as they were, the chains through their controls.
    for name, basis in shown_pose.items():
        arm.pose.bones[name].matrix_basis = basis
    if any(posed(basis) for basis in shown_pose.values()):
        snap_controls(arm)
        capture_reference(arm)  # while the chains still show the stance: Growth moves the controls with it
        release_chains(arm, data)
    else:
        capture_reference(arm)
    growth.set_growth(arm, shown_growth)
    if mode == "POSE":
        bpy.ops.object.mode_set(mode="POSE")
    view_layer.objects.active = active
    return reasons


def release_chains(arm, data):
    """The chains' own pose lives in their controls: their joints go back to rest (Growth's channels stay), so clearing the
    pose returns to the exact rest the poles were set up for."""
    bones = ((data or {}).get("growth") or {}).get("bones") or []
    located = {b["name"] for b in bones if b.get("translation")}
    scaled = {b["name"] for b in bones if b.get("scale")}
    for name in {n for c in built(arm) for n in c["joints"]}:
        pose_bone = arm.pose.bones[name]
        pose_bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
        pose_bone.rotation_euler = (0.0, 0.0, 0.0)
        pose_bone.rotation_axis_angle = (0.0, 0.0, 1.0, 0.0)
        if name not in located:
            pose_bone.location = (0.0, 0.0, 0.0)
        if name not in scaled:
            pose_bone.scale = (1.0, 1.0, 1.0)


def _follows(chain):
    """(mechanism bone, joint it rides on, bone whose spot it follows) for each control of a chain: the chain end follows by
    its tip bone, the pole and the aim by themselves."""
    controls = [chain["tip"]] + [chain[k] for k in ("pole", "look") if chain.get(k)]
    return [(f[0], f[1], f[2] if len(f) > 2 else c) for f, c in zip(chain["follow"], controls)]


def capture_reference(arm):
    """The bases (at Growth 1) of the bones the controls hang from, as they stand now, and where each control stands:
    Growth's follow is measured from them."""
    from . import growth

    shown = getattr(arm, "tyrant_growth", 1.0)
    growth.set_growth(arm, 1.0)
    bpy.context.view_layer.update()
    chains = built(arm)
    joints = {n for c in chains for n in c["joints"]}
    arm[REFERENCE] = json.dumps({n: flat(arm.pose.bones[n].matrix_basis) for n in _ancestors(arm, joints)})
    arm[POINTS] = json.dumps({grow: list(arm.pose.bones[control].head) for c in chains for grow, _joint, control in _follows(c)
                              if arm.pose.bones.get(control) is not None})
    growth.set_growth(arm, shown)


# --- following Growth, snapping ---------------------------------------------------------------------------------------------

def _depth(bone):
    depth = 0
    while bone.parent is not None:
        bone, depth = bone.parent, depth + 1
    return depth


def _reference_matrices(arm, channels):
    """The pose matrices (armature space) of the bones the controls hang from, as built, with Growth's own channels
    (name → (location or None, scale or None)) put in; channels None = as built."""
    reference = _matrices(arm, REFERENCE)
    bones = arm.data.bones
    out = {}
    for name in sorted((n for n in reference if n in bones), key=lambda n: _depth(bones[n])):
        bone, basis = bones[name], reference[name]
        if channels and name in channels:
            location, rotation, scale = basis.decompose()
            new_location, new_scale = channels[name]
            basis = Matrix.LocRotScale(new_location if new_location is not None else location, rotation,
                                       new_scale if new_scale is not None else scale)
        local = bone.matrix_local @ basis
        parent = bone.parent
        out[name] = local if parent is None else out[parent.name] @ parent.matrix_local.inverted() @ local
    return out


def follow_growth(arm, channels):
    """Moves each control's mechanism parent by how far its anchor (the chain end, the knee, the head) moves at this growth
    from where it was built, so an unmoved control sits on the chain end at any growth and your pose on it stays relative."""
    chains = built(arm)
    if not chains:
        return
    now, then = _reference_matrices(arm, channels), _reference_matrices(arm, None)
    try:
        points = {grow: Vector(at) for grow, at in json.loads(arm.get(POINTS) or "{}").items()}
    except (ValueError, TypeError):
        points = {}
    bones = arm.data.bones
    for chain in chains:
        for grow, joint, _control in _follows(chain):
            pose_bone, bone = arm.pose.bones.get(grow), bones.get(grow)
            if pose_bone is None or bone.parent is None or joint not in now or bone.parent.name not in now:
                continue
            parent = bone.parent
            local = parent.matrix_local.inverted() @ bone.matrix_local
            frame_then, frame_now = then[parent.name] @ local, now[parent.name] @ local
            # The control moves as its spot on the joint does: from where it stood when the reference was taken.
            point = points.get(grow, frame_then.translation)
            moved = now[joint] @ (then[joint].inverted() @ point)
            at = frame_then.translation + (moved - point)
            pose_bone.location = frame_now.to_3x3().inverted() @ (at - frame_now.translation)


def _unscaled(matrix):
    location, rotation, _scale = matrix.decompose()
    return Matrix.LocRotScale(location, rotation, None)


def snap_controls(arm, names=None):
    """Puts the controls where the current FK pose has the chain ends (and knees, and the head's aim), so switching to IK
    does not jump. names: chain names (None = every chain)."""
    chains = [c for c in built(arm) if names is None or c["name"] in names]
    pose = arm.pose.bones
    saved = {c["target"]: pose[c["target"]].get(IK_FK, 1.0) for c in chains}
    for c in chains:
        pose[c["target"]][IK_FK] = 0.0
    refresh(arm)
    fk = {n: pose[n].matrix.copy() for c in chains for n in c["joints"]}
    for c in chains:
        was = {key: unflat(values) for key, values in c["shown"].items()}
        tip, knee = c["joints"][-1], c["joints"][1]
        pose[c["target"]].matrix = _unscaled(fk[tip] @ was["tipJoint"].inverted() @ was["target"])
        if c.get("pole"):
            pose[c["pole"]].matrix = _unscaled(fk[knee] @ was["knee"].inverted() @ was["pole"])
        if c.get("look"):
            pose[c["look"]].matrix = _unscaled(fk[tip] @ was["tipJoint"].inverted() @ was["look"])
        bpy.context.view_layer.update()
    for c in chains:
        pose[c["target"]][IK_FK] = saved[c["target"]]
    refresh(arm)


# --- removing, baking, resetting -------------------------------------------------------------------------------------------

def _keep_what_the_chains_show(arm):
    """Before the IK goes: each chain on IK keeps the pose it shows, written into its joints' own channels."""
    pose = arm.pose.bones
    chains = [c for c in built(arm) if pose.get(c["target"]) is not None and pose[c["target"]].get(IK_FK, 0.0) > 0.0]
    bones = arm.data.bones
    joints = sorted({n for c in chains for n in c["joints"] if n in pose}, key=lambda n: _depth(bones[n]))
    bpy.context.view_layer.update()
    seen = {n: pose[n].matrix.copy() for n in joints}
    for name in joints:
        parent = bones[name].parent
        if parent is None:
            frame = bones[name].matrix_local
        else:
            above = seen[parent.name] if parent.name in seen else pose[parent.name].matrix
            frame = above @ parent.matrix_local.inverted() @ bones[name].matrix_local
        pose[name].matrix_basis = frame.inverted() @ seen[name]


def remove_controls(arm):
    """Deletes Tyrant's control and mechanism bones, their constraints, drivers and collections: the armature is as before."""
    names = {n for c in built(arm) for n in c["bones"]} | {b.name for b in arm.data.bones if b.get(BONE_TAG)}
    if not names:
        raise IkError("This armature has no IK controls.")
    _keep_what_the_chains_show(arm)
    if arm.animation_data is not None:
        for curve in list(arm.animation_data.drivers):
            if f'constraints["{PREFIX}' in curve.data_path:
                arm.animation_data.drivers.remove(curve)
    for pose_bone in arm.pose.bones:
        for constraint in list(pose_bone.constraints):
            if constraint.name.startswith(PREFIX):
                pose_bone.constraints.remove(constraint)
    view_layer = bpy.context.view_layer
    active, mode = view_layer.objects.active, arm.mode
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        for bone in [b for b in arm.data.edit_bones if b.name in names]:
            arm.data.edit_bones.remove(bone)
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    for name in (CONTROLS, MECHANISM):
        collection = arm.data.collections.get(name)
        if collection is not None:
            arm.data.collections.remove(collection)
    for key in (TAG, SKIPPED, REFERENCE, POINTS, OLD_CONTROLS):
        if key in arm:
            del arm[key]
    if mode == "POSE":
        bpy.ops.object.mode_set(mode="POSE")
    view_layer.objects.active = active


def _rotation_path(pose_bone):
    return {"QUATERNION": "rotation_quaternion", "AXIS_ANGLE": "rotation_axis_angle"}.get(pose_bone.rotation_mode, "rotation_euler")


def bake(arm, scene, frame_range):
    """Keys the IK result onto the game bones' rotations (this frame, or the scene's frame range) and switches those chains
    to FK there, so the pose or animation plays without the controls. Returns the frames baked."""
    pose = arm.pose.bones
    chains = [c for c in built(arm) if pose.get(c["target"]) is not None and pose[c["target"]].get(IK_FK, 0.0) > 0.0]
    if not chains:
        raise IkError("No chain is on IK; nothing to bake.")
    frames = list(range(scene.frame_start, scene.frame_end + 1)) if frame_range else [scene.frame_current]
    original = scene.frame_current
    joints = sorted({n for c in chains for n in c["joints"]}, key=lambda n: _depth(arm.data.bones[n]))
    parents = {n: arm.data.bones[n].parent.name for n in joints if arm.data.bones[n].parent is not None}
    visual = {}
    for frame in frames:
        scene.frame_set(frame)
        visual[frame] = {n: pose[n].matrix.copy() for n in set(joints) | set(parents.values())}
    for chain in chains:
        control = pose[chain["target"]]
        # The aim is baked too (it is in the head's visual rotation): left on, it would turn the head a second time.
        for key in (IK_FK, AIM) if chain.get("look") and AIM in control else (IK_FK,):
            control[key] = 0.0
            for frame in frames:
                control.keyframe_insert(f'["{key}"]', frame=frame, group=chain["name"])
    bones = arm.data.bones
    for frame in frames:
        scene.frame_set(frame)
        seen = visual[frame]
        for name in joints:
            parent = parents.get(name)
            frame_of = bones[name].matrix_local if parent is None else seen[parent] @ bones[parent].matrix_local.inverted() @ bones[name].matrix_local
            pose[name].matrix_basis = frame_of.inverted() @ seen[name]
            pose[name].keyframe_insert(_rotation_path(pose[name]), frame=frame, group=name)
    scene.frame_set(original)
    return frames


def reset_pose(arm):
    """Clears the pose of every game bone and control (the rest pose, as Clear Transform); Growth's own channels and the
    mechanism bones stay."""
    from . import growth

    if arm.pop(OLD_OPEN_POSE, None) is not None and built(arm):
        arm[OLD_CONTROLS] = True  # the controls still stand where the prefab pose had the chains: they need rebuilding
    mechanism = {n for c in built(arm) for n in c["bones"] if n.startswith("mch_")}
    for pose_bone in arm.pose.bones:
        if pose_bone.name not in mechanism:
            pose_bone.matrix_basis = Matrix.Identity(4)
    arm.pop(growth.BASE, None)  # Growth now works from the rest pose, not the prefab stance
    if built(arm):
        capture_reference(arm)
    growth.set_growth(arm, getattr(arm, "tyrant_growth", 1.0))
