"""The Tyrant panel's Rig edit section: Start, Apply, Cancel, Clear, and what the game does with the edited bones."""
import bpy

from . import growth, project, rig, send


def _data(armature):
    try:
        data = growth._project(armature)
    except (project.ProjectError, KeyError, TypeError, ValueError) as ex:
        raise rig.RigError(str(ex)) from ex
    if data is None:
        raise rig.RigError("This model's Tyrant project is gone or on a network share. Open it again from Tyrant.")
    return data


def _armature(context):
    armature = project.armature_of(context)
    if armature is None:
        raise rig.RigError(send.why_not_sendable(context) or "Open the model from Tyrant first.")
    return armature


def _names(names):
    shown = ", ".join(names[:6])
    return shown + (f" and {len(names) - 6} more" if len(names) > 6 else "")


def warnings(armature, data):
    """Lines about the edited bones the game moves itself: by its animations, or by its growth."""
    info = (data or {}).get("rigInfo") or {}
    edited = set(rig.offsets(armature))
    lines = []
    clip = [n for n in info.get("clipMoved") or [] if n in edited]
    if clip:
        lines.append(f"{_names(clip)}: moved by the game's animations; the edit changes that motion.")
    grown = [n for n in (info.get("growthMoved") or []) + (info.get("growthScaled") or []) if n in edited]
    grown = list(dict.fromkeys(grown))
    if grown:
        if info.get("growthSupported"):
            lines.append(f"{_names(grown)}: positioned by the game's growth; the edit is applied again after it.")
        else:
            lines.append(f"{_names(grown)}: positioned by the game's growth, which puts them back; Send refuses edits on them.")
    return lines


class _RigOperator(bpy.types.Operator):
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        try:
            armature = _armature(context)
            message = self.run(context, armature, _data(armature))
        except (rig.RigError, project.ProjectError) as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        if message:
            self.report({"INFO"}, message)
        for area in context.screen.areas if context.screen else []:
            area.tag_redraw()
        return {"FINISHED"}


class TYRANT_OT_rig_start(_RigOperator):
    """Start a rig edit: then move, rotate or scale the game's bones in Pose Mode and press Apply rig edit"""

    bl_idname = "tyrant.rig_start"
    bl_label = "Start rig edit"

    def run(self, context, armature, data):
        rig.start(armature, data)
        return "Pose the bones (move, rotate, scale), then Apply rig edit."


class TYRANT_OT_rig_apply(_RigOperator):
    """Make the bones you changed since Start the model's skeleton (the mesh follows) and record the edit for the game"""

    bl_idname = "tyrant.rig_apply"
    bl_label = "Apply rig edit"

    def run(self, context, armature, data):
        rig.apply(armature, data)
        return f"Rig edit: {len(rig.offsets(armature))} bone(s)."


class TYRANT_OT_rig_cancel(_RigOperator):
    """Put the pose back as it was at Start, recording nothing"""

    bl_idname = "tyrant.rig_cancel"
    bl_label = "Cancel"

    def run(self, context, armature, data):
        rig.cancel(armature, data)
        return None


class TYRANT_OT_rig_clear(_RigOperator):
    """Return the skeleton and the mesh to the game's (the rig edit is removed; Send then clears it in the mod)"""

    bl_idname = "tyrant.rig_clear"
    bl_label = "Clear rig edit"

    def run(self, context, armature, data):
        rig.clear(armature, data)
        return "The rig edit is cleared."


def draw(layout, context, armature, data, say):
    box = layout.box()
    box.label(text="Rig edit", icon="BONE_DATA")
    edited = sorted(rig.offsets(armature))
    if rig.editing(armature):
        say(box, context, "Pose the bones (move, rotate, scale), then Apply rig edit.", "INFO")
        row = box.row(align=True)
        row.operator("tyrant.rig_apply", icon="CHECKMARK")
        row.operator("tyrant.rig_cancel", icon="X")
    else:
        row = box.row(align=True)
        row.operator("tyrant.rig_start", icon="POSE_HLT")
        if edited:
            row.operator("tyrant.rig_clear", icon="X")
    if armature.get(rig.PROBLEM):
        say(box, context, armature[rig.PROBLEM], "ERROR")
    if edited:
        say(box, context, f"Edited: {_names(edited)}", "BONE_DATA")
        for line in warnings(armature, data):
            say(box, context, line, "ERROR")


CLASSES = (TYRANT_OT_rig_start, TYRANT_OT_rig_apply, TYRANT_OT_rig_cancel, TYRANT_OT_rig_clear)
