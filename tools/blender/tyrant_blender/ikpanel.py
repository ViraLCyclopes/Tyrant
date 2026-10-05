"""The Tyrant panel's IK section: Add / Remove IK controls, IK/FK and Aim per chain, Snap, Bake, Reset pose."""
import bpy

from . import growth, ik, project, send

REOPEN = "Reopen this model from Tyrant to get its IK data."


def _data(armature):
    try:
        data = growth._project(armature)
    except (project.ProjectError, KeyError, TypeError, ValueError) as ex:
        raise ik.IkError(str(ex)) from ex
    if data is None:
        raise ik.IkError("This model's Tyrant project is gone or on a network share. Open it again from Tyrant.")
    return data


def ensure_ik_data(armature):
    """The project with its "ik": a project written before Tyrant read IK chains asks Tyrant to add them."""
    data = _data(armature)
    if "ik" in data:
        return data
    path = armature[project.TAG]
    if not send.trusted_tyrant(data.get("tyrant")):
        raise ik.IkError(REOPEN)
    answer = send.run([data["tyrant"], "blender", "ik-data", "-w", data["workspace"], path], timeout=300)
    if not answer.get("ok"):
        raise ik.IkError(f"{REOPEN} ({'; '.join(answer.get('errors') or ['Tyrant did not answer'])})")
    growth.forget(path)
    return _data(armature)


def _armature(context):
    armature = project.armature_of(context)
    if armature is None:
        raise ik.IkError(send.why_not_sendable(context) or "Open the model from Tyrant first.")
    return armature


def _selected_chains(context, armature):
    """The chains whose control, pole or aim is selected (pose mode), else None (every chain)."""
    names = set()
    for pose_bone in context.selected_pose_bones or []:
        if pose_bone.id_data != armature:
            continue
        for chain in ik.built(armature):
            if pose_bone.name in (chain["target"], chain.get("pole"), chain.get("look")):
                names.add(chain["name"])
    return sorted(names) or None


class _IkOperator(bpy.types.Operator):
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        try:
            message = self.run(context, _armature(context))
        except (ik.IkError, project.ProjectError) as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        if message:
            self.report({"INFO"}, message)
        for area in context.screen.areas if context.screen else []:
            area.tag_redraw()
        return {"FINISHED"}


class TYRANT_OT_ik_add(_IkOperator):
    """Build IK controls from the game's IK chains (foot and hand controls with knee and elbow poles, a head control and aim)"""

    bl_idname = "tyrant.ik_add"
    bl_label = "Add IK controls"

    def run(self, context, armature):
        skipped = ik.add_controls(armature, ensure_ik_data(armature))
        return f"IK controls added ({len(ik.built(armature))} chains" + (f", {len(skipped)} skipped)" if skipped else ")")


class TYRANT_OT_ik_remove(_IkOperator):
    """Delete Tyrant's IK controls; the game's bones and your mesh stay as they are"""

    bl_idname = "tyrant.ik_remove"
    bl_label = "Remove IK controls"

    def run(self, context, armature):
        ik.remove_controls(armature)
        return "IK controls removed."


class TYRANT_OT_ik_snap(_IkOperator):
    """Move the controls (of the selected ones, else all) to where the current pose has the feet, knees and head, so switching to IK does not jump"""

    bl_idname = "tyrant.ik_snap"
    bl_label = "Snap controls to pose"

    def run(self, context, armature):
        ik.snap_controls(armature, _selected_chains(context, armature))
        return None


class TYRANT_OT_ik_bake(_IkOperator):
    """Key the IK pose onto the game's bones and switch those chains to FK (for animation export and the game)"""

    bl_idname = "tyrant.ik_bake"
    bl_label = "Bake IK to bones"

    frame_range: bpy.props.BoolProperty(name="Frame range", description="Every frame of the scene's range, not just this one")

    def run(self, context, armature):
        frames = ik.bake(armature, context.scene, self.frame_range)
        return f"Baked {len(frames)} frame(s)."


class TYRANT_OT_ik_reset_pose(_IkOperator):
    """Clear the pose of every bone and control: the rest pose (Growth stays)"""

    bl_idname = "tyrant.ik_reset_pose"
    bl_label = "Reset pose"

    def run(self, context, armature):
        ik.reset_pose(armature)
        return None


def old_controls_hint(armature):
    """Controls built by the earlier build, which opened models in the game's prefab pose instead of the bind pose."""
    if ik.built(armature) and (ik.OLD_OPEN_POSE in armature or armature.get(ik.OLD_CONTROLS)):
        return ("These IK controls were built on the game's prefab pose by an earlier Tyrant: Remove IK controls, Reset pose, "
                "then Add IK controls to build them at rest.")
    return None


def draw(layout, context, armature, data, say):
    box = layout.box()
    box.label(text="IK controls", icon="CON_KINEMATIC")
    chains = ik.built(armature)
    if not chains:
        known = "ik" in data
        row = box.row()
        row.enabled = not known or bool((data.get("ik") or {}).get("chains"))
        row.operator("tyrant.ik_add", icon="ADD")
        if known and not (data.get("ik") or {}).get("chains"):
            say(box, context, "This model has no IK chains in the game.", "INFO")
    else:
        old = old_controls_hint(armature)
        if old:
            say(box, context, old, "ERROR")
        for chain in chains:
            control = armature.pose.bones.get(chain["target"])
            if control is None:
                continue
            box.prop(control, f'["{ik.IK_FK}"]', text=f"{chain['name']}: IK", slider=True)
            if chain.get("look"):
                box.prop(control, f'["{ik.AIM}"]', text=f"{chain['name']}: Aim", slider=True)
        column = box.column(align=True)
        column.operator("tyrant.ik_snap", icon="SNAP_ON")
        row = column.row(align=True)
        row.operator("tyrant.ik_bake", text="Bake this frame").frame_range = False
        row.operator("tyrant.ik_bake", text="Bake frame range").frame_range = True
        column.operator("tyrant.ik_remove", icon="X")
        say(box, context, "Blender's IK is close to the game's FABRIK, not identical.", "INFO")
    box.operator("tyrant.ik_reset_pose", icon="LOOP_BACK")
    for line in ik.skipped(armature):
        say(box, context, line, "ERROR")


CLASSES = (TYRANT_OT_ik_add, TYRANT_OT_ik_remove, TYRANT_OT_ik_snap, TYRANT_OT_ik_bake, TYRANT_OT_ik_reset_pose)
