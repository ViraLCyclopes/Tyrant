"""
IK controls built from the game's FABRIK chains (the project's "ik"), as bones inside the Tyrant armature: a foot (hand)
control and a knee (elbow) pole per leg, a head control and an aim target for the head. Blender's IK solver is not FABRIK:
poses are close to the game's, not identical.

Per chain, e.g. the left foot: mch_grow_foot.L (follows Growth) → ctrl_foot.L (yours, flat on the ground contact point) →
mch_tip_foot.L (the chain's last joint as it rests: the IK target, and the joint copies its rotation). mch_end_foot.L, a child
of the joint before the last, reaches from that joint to the last one and carries the IK constraint: Blender's IK reaches
with a bone's tail, and the game's bones do not point at their child.
"""
import json
import math

import bpy
from mathutils import Matrix, Vector

TAG = "tyrant_ik"  # armature: JSON list of the chains built
SKIPPED = "tyrant_ik_skipped"  # armature: JSON list of the chains left out, with why
BONE_TAG = "tyrant_ik"  # bone: one of Tyrant's IK bones
CONTROLS = "Tyrant IK"
MECHANISM = "Tyrant IK (mechanism)"
PREFIX = "Tyrant "  # constraint names
IK_FK = "ik_fk"
AIM = "aim"
FORWARD = Vector((0.0, -1.0, 0.0))  # animals face -Y in Blender (Unity +Z → glTF +Z → Blender -Y)
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


# --- where the bones go (armature space, from the rest pose) ---------------------------------------------------------------

def _point(arm, joint, local):
    """A point given in a joint's own frame (glTF), in armature space: the importer keeps each bone's axes as the node's."""
    return arm.data.bones[joint].matrix_local @ Vector(local)


def _off_line(vector, line):
    return vector - line * vector.dot(line)


def _pole_point(arm, chain, joints, length):
    """In front of the knee (elbow): the game's pull on it, else the way the leg bends at rest, else forward."""
    bones = arm.data.bones
    root, knee, tip = (bones[n].head_local for n in (joints[0], joints[1], joints[-1]))
    line = (tip - root).normalized() if (tip - root).length > 1e-6 else Vector((0.0, 0.0, -1.0))
    pull = Vector()
    for force in chain.get("forces") or []:
        if force["joint"] == joints[1] and force["bone"] in bones:
            pull += (bones[force["bone"]].matrix_local.to_3x3() @ Vector(force["direction"])) * float(force["strength"])
    direction = _off_line(pull, line)
    if direction.length < 1e-4:
        direction = _off_line(knee - root, line)
    if direction.length < 1e-4:
        direction = _off_line(FORWARD, line)
    return knee + direction.normalized() * length


def _look_point(arm, joints):
    """A few neck lengths along the Head bone's axis that points most forward, and that axis for the Damped Track."""
    head = arm.data.bones[joints[-1]]
    m = head.matrix_local.to_3x3()
    axes = (m.col[0], -m.col[0], m.col[1], -m.col[1], m.col[2], -m.col[2])
    best = max(range(6), key=lambda i: axes[i].normalized().dot(FORWARD))
    neck = (head.head_local - arm.data.bones[joints[-2]].head_local).length
    return head.head_local + axes[best].normalized() * max(3.0 * neck, 0.5), TRACK_AXES[best]


def _plan(arm, chain):
    """Where one chain's bones go, or why it is skipped."""
    bones = arm.data.bones
    joints = [j["name"] for j in chain["joints"]]
    missing = next((n for n in joints if n not in bones), None)
    if missing:
        return None, f"{chain['name']} skipped: no bone '{missing}'"
    if len(joints) < 3:
        return None, f"{chain['name']} skipped: it has only {len(joints)} joints"
    if (bones[joints[-1]].head_local - bones[joints[-2]].head_local).length < 1e-5:
        return None, f"{chain['name']} skipped: '{joints[-1]}' sits on '{joints[-2]}'"
    length = sum((bones[a].head_local - bones[b].head_local).length for a, b in zip(joints, joints[1:]))
    plan = {"chain": chain, "joints": joints, "length": length, "size": max(length * 0.15, 0.05),
            "anchor": _point(arm, joints[-1], chain["endOffset"])}
    if chain["kind"] == "limb" and chain["controls"].get("pole"):
        plan["pole"] = _pole_point(arm, chain, joints, length)
    if chain["kind"] == "head" and chain["controls"].get("look"):
        plan["look"], plan["track"] = _look_point(arm, joints)
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


def _new(arm, name, head, tail, parent, roll=0.0):
    bone = arm.data.edit_bones.new(name)
    bone.head, bone.tail, bone.roll = head, tail, roll
    bone.parent = parent
    bone.use_deform = False
    bone[BONE_TAG] = True
    return bone


def _edit_bones(arm, plan, root):
    """Creates one chain's bones (edit mode); returns their names by role."""
    edit = arm.data.edit_bones
    chain, joints = plan["chain"], plan["joints"]
    up = Vector((0.0, 0.0, plan["size"]))
    made = {}

    def control(key, grow_key, name, at, scale=1.0):
        stem = name[len("ctrl_"):] if name.startswith("ctrl_") else name
        grow = _new(arm, f"mch_grow_{stem}", at, at + up * scale, edit[root])
        bone = _new(arm, name, at, at + up * scale, grow)
        made[key], made[grow_key] = bone.name, grow.name
        return bone, stem

    target, stem = control("target", "grow", chain["controls"]["target"], plan["anchor"])
    last, before = edit[joints[-1]], edit[joints[-2]]
    made["tip"] = _new(arm, f"mch_tip_{stem}", last.head.copy(), last.tail.copy(), target, last.roll).name
    made["end"] = _new(arm, f"mch_end_{stem}", before.head.copy(), last.head.copy(), before).name
    if "pole" in plan:
        control("pole", "pole_grow", chain["controls"]["pole"], plan["pole"], 0.5)
    if "look" in plan:
        control("look", "look_grow", chain["controls"]["look"], plan["look"], 0.5)
    return made


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


def _solve_pole_angle(ik_constraint, arm, joints):
    """The pole angle that leaves the chain where it rests. Blender's rule depends on where each bone's tail points, which
    the game's bones do not follow, so it is found by trying: a 5° scan, then a golden-section search."""
    rest = {n: arm.data.bones[n].matrix_local.copy() for n in joints}

    def drift(angle):
        ik_constraint.pole_angle = angle
        bpy.context.view_layer.update()
        return max(abs(a - b) for n in joints for ra, rb in zip(arm.pose.bones[n].matrix, rest[n]) for a, b in zip(ra, rb))

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


def _pose_setup(arm, plan, made):
    pose = arm.pose.bones
    chain, joints = plan["chain"], plan["joints"]
    ctrl = pose[made["target"]]
    ctrl[IK_FK] = 1.0
    ctrl.id_properties_ui(IK_FK).update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0,
                                        description="1: the control moves the chain (IK). 0: rotate the bones yourself (FK)")
    end = pose[made["end"]]
    end.lock_ik_x = end.lock_ik_y = end.lock_ik_z = True  # rides on its joint; only the joints above it turn
    solver = end.constraints.new("IK")
    solver.name = PREFIX + "IK"
    solver.target, solver.subtarget = arm, made["tip"]
    solver.use_tail = True
    solver.chain_count = len(joints)  # the helper and every joint but the last
    _drive(arm, solver, made["target"], IK_FK, float(chain.get("influence", 1.0)))
    if made.get("pole"):
        solver.pole_target, solver.pole_subtarget = arm, made["pole"]
        _solve_pole_angle(solver, arm, joints[:-1])
    if chain["kind"] == "limb" or chain.get("matchHeadRotation"):
        rotation = pose[joints[-1]].constraints.new("COPY_ROTATION")
        rotation.name = PREFIX + "rotation"
        rotation.target, rotation.subtarget = arm, made["tip"]
        _drive(arm, rotation, made["target"], IK_FK, 1.0)
    if made.get("look"):
        ctrl[AIM] = 0.0
        ctrl.id_properties_ui(AIM).update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0,
                                          description="How much the head turns toward its aim target (ctrl_look)")
        aim = pose[joints[-1]].constraints.new("DAMPED_TRACK")
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


def _root(arm, joints):
    bone = arm.data.bones[joints[0]]
    while bone.parent is not None:
        bone = bone.parent
    return bone.name


def _record(plan, made):
    joints = plan["joints"]
    follow = [[made["grow"], joints[-1]]]
    if made.get("pole"):
        follow.append([made["pole_grow"], joints[1]])
    if made.get("look"):
        follow.append([made["look_grow"], joints[-1]])
    chain = plan["chain"]
    return {"name": chain["name"], "kind": chain["kind"], "side": chain.get("side"), "joints": joints,
            "target": made["target"], "pole": made.get("pole"), "look": made.get("look"), "tip": made["tip"], "end": made["end"],
            "bones": list(made.values()), "follow": follow}


def _posed(basis):
    location, rotation, scale = basis.decompose()
    return rotation.angle > 1e-6 or location.length > 1e-6 or (scale - Vector((1.0, 1.0, 1.0))).length > 1e-6


def add_controls(arm, data):
    """Builds the controls of every chain in the project's "ik" that fits this armature and returns the skipped chains'
    reasons. Built from the rest pose at Growth 1; the pose and Growth are put back after, so no bone moves."""
    from . import growth

    if built(arm):
        raise IkError("Already has IK controls (Remove IK controls first).")
    chains = ((data or {}).get("ik") or {}).get("chains") or []
    if not chains:
        raise IkError("This model has no IK chains in the game.")
    view_layer = bpy.context.view_layer
    if view_layer.objects.get(arm.name) is None:
        raise IkError(f"{arm.name} is in a collection excluded from the view layer; tick it in the Outliner first.")
    plans, reasons = [], []
    for chain in chains:
        plan, why = _plan(arm, chain)
        if why:
            reasons.append(why)
        else:
            plans.append(plan)
    if not plans:
        raise IkError("No IK chain fits this armature: " + "; ".join(reasons))

    shown = getattr(arm, "tyrant_growth", 1.0)
    pose = {b.name: b.matrix_basis.copy() for b in arm.pose.bones}
    growth.set_growth(arm, 1.0)
    for pose_bone in arm.pose.bones:
        pose_bone.matrix_basis = Matrix.Identity(4)
    active, mode = view_layer.objects.active, arm.mode
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    view_layer.objects.active = arm
    root = _root(arm, plans[0]["joints"])
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        made = [_edit_bones(arm, plan, root) for plan in plans]
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    for plan, names in zip(plans, made):
        _pose_setup(arm, plan, names)
        _style(arm, plan, names)
    arm[TAG] = json.dumps([_record(plan, names) for plan, names in zip(plans, made)])
    arm[SKIPPED] = json.dumps(reasons)
    for name, basis in pose.items():
        arm.pose.bones[name].matrix_basis = basis
    growth.set_growth(arm, shown)
    if any(_posed(basis) for basis in pose.values()):
        snap_controls(arm)
    if mode == "POSE":
        bpy.ops.object.mode_set(mode="POSE")
    view_layer.objects.active = active
    return reasons


# --- following Growth, snapping ---------------------------------------------------------------------------------------------

def _growth_matrices(arm, bases):
    """Every bone's pose matrix (armature space) with only Growth's bases applied (bone name → basis)."""
    out = {}

    def visit(bone):
        local = bone.matrix_local @ bases.get(bone.name, Matrix.Identity(4))
        out[bone.name] = local if bone.parent is None else out[bone.parent.name] @ bone.parent.matrix_local.inverted() @ local
        for child in bone.children:
            visit(child)

    for bone in arm.data.bones:
        if bone.parent is None:
            visit(bone)
    return out


def follow_growth(arm, bases):
    """Moves each control's mechanism parent by how far its anchor (the chain end, the knee, the head) moves at this growth,
    so an unmoved control sits on the chain end at any growth and your pose on it stays relative."""
    chains = built(arm)
    if not chains:
        return
    m = _growth_matrices(arm, bases)
    bones = arm.data.bones
    for chain in chains:
        for grow, joint in chain["follow"]:
            pose_bone, bone, anchor = arm.pose.bones.get(grow), bones.get(grow), bones.get(joint)
            if pose_bone is None or anchor is None:
                continue
            moved = m[joint] @ (anchor.matrix_local.inverted() @ bone.head_local)
            parent = bone.parent
            frame = m[parent.name] @ parent.matrix_local.inverted() @ bone.matrix_local if parent is not None else bone.matrix_local
            pose_bone.location = frame.to_3x3().inverted() @ (moved - frame.translation)


def _unscaled(matrix):
    location, rotation, _scale = matrix.decompose()
    return Matrix.LocRotScale(location, rotation, None)


def snap_controls(arm, names=None):
    """Puts the controls where the current FK pose has the chain ends (and knees, and the head's aim), so switching to IK
    does not jump. names: chain names (None = every chain)."""
    chains = [c for c in built(arm) if names is None or c["name"] in names]
    pose, bones = arm.pose.bones, arm.data.bones
    saved = {c["target"]: pose[c["target"]].get(IK_FK, 1.0) for c in chains}
    for c in chains:
        pose[c["target"]][IK_FK] = 0.0
    refresh(arm)
    fk = {n: pose[n].matrix.copy() for c in chains for n in c["joints"]}
    for c in chains:
        tip, knee = c["joints"][-1], c["joints"][1]
        pose[c["target"]].matrix = _unscaled(fk[tip] @ bones[c["tip"]].matrix_local.inverted() @ bones[c["target"]].matrix_local)
        if c.get("pole"):
            pose[c["pole"]].matrix = _unscaled(fk[knee] @ bones[knee].matrix_local.inverted() @ bones[c["pole"]].matrix_local)
        if c.get("look"):
            pose[c["look"]].matrix = _unscaled(fk[tip] @ bones[tip].matrix_local.inverted() @ bones[c["look"]].matrix_local)
        bpy.context.view_layer.update()
    for c in chains:
        pose[c["target"]][IK_FK] = saved[c["target"]]
    refresh(arm)
