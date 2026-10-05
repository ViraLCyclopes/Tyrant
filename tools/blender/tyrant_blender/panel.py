"""The Tyrant panel (3D view sidebar → Tyrant): where the model came from and goes, the Growth slider, Send to Tyrant, the report."""
import json
import os
import subprocess

import bpy

from . import growth, project, send, ui

REPORT = "tyrant_report"
NEW_MOD = "__new__"
SPECIES_MODEL = "__species__"

# Blender's dynamic enum items must stay referenced while the dialog is open.
_destinations = {"mods": [], "species": ""}
_mod_items = []
_target_items = []


def _redraw():
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type == "VIEW_3D":
                area.tag_redraw()


def project_data(armature):
    """(project, None) or (None, why it cannot be read): the panel and the operators never show a traceback."""
    try:
        data = growth._project(armature)
    except (project.ProjectError, KeyError, TypeError, ValueError) as ex:
        return None, str(ex) if isinstance(ex, project.ProjectError) else f"This project cannot be read ({ex}). Open it again from Tyrant."
    if data is None:
        return None, "This model's Tyrant project is gone or on a network share. Open it again from Tyrant."
    return data, None


def is_busy(armature):
    try:
        return bool(json.loads(armature.get(REPORT) or "{}").get("busy"))
    except ValueError:
        return False


def clear_stale_busy(*_args):
    """A file saved while Tyrant was building would say "building" forever: on load it becomes "send again"."""
    for obj in bpy.data.objects:
        if obj.type == "ARMATURE" and is_busy(obj):
            obj[REPORT] = json.dumps({"ok": False, "errors": ["The last send did not finish (Blender was closed meanwhile); send again."]})


def _start_send(context, armature, destination=None, new_mod_name=None):
    path = armature[project.TAG]
    data = project.load(path)
    if not send.trusted_tyrant(data.get("tyrant")):
        raise send.SendError(f"This project names {data.get('tyrant')!r} as Tyrant; open it again from Tyrant on this PC.")
    glb = os.path.join(os.path.dirname(path), "send.glb")
    send.export(armature, glb)
    if bpy.data.filepath:
        bpy.ops.wm.save_mainfile()
    else:
        bpy.ops.wm.save_as_mainfile(filepath=ui.project_blend(path, data))
    ui.record_blend(path, bpy.data.filepath)
    armature[REPORT] = json.dumps({"busy": True})
    name = armature.name

    def done(result):
        target = bpy.data.objects.get(name)
        if target is not None:
            target[REPORT] = json.dumps(result)
        _redraw()
        return None

    send.run_async(send.command(data, path, glb, destination, new_mod_name), done)


class TYRANT_OT_send(bpy.types.Operator):
    """Export the Tyrant armature and the meshes it deforms, and add them to the mod in Tyrant"""

    bl_idname = "tyrant.send"
    bl_label = "Send to Tyrant"

    def execute(self, context):
        reason = send.why_not_sendable(context)
        if reason:
            self.report({"ERROR"}, reason)
            return {"CANCELLED"}
        armature = project.armature_of(context)
        if is_busy(armature):
            self.report({"ERROR"}, "Tyrant is still building the last send; wait for its report in the Tyrant panel.")
            return {"CANCELLED"}
        data, error = project_data(armature)
        if error:
            self.report({"ERROR"}, error)
            return {"CANCELLED"}
        try:
            if not data.get("destination"):
                bpy.ops.tyrant.choose_destination("INVOKE_DEFAULT", armature=armature.name)
                return {"FINISHED"}
            _start_send(context, armature)
        except (project.ProjectError, send.SendError) as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        return {"FINISHED"}


def _mods(_self, _context):
    _mod_items[:] = [(m["id"], m["name"], m["id"]) for m in _destinations["mods"]] + [(NEW_MOD, "New mod…", "Create a mod")]
    return _mod_items


def _targets(self, _context):
    mod = next((m for m in _destinations["mods"] if m["id"] == self.mod), None)
    _target_items[:] = [(SPECIES_MODEL, f"{_destinations['species']} model", "Replace the species' model")] + [
        (s["id"], f"Skin: {s['name']}", "Give this skin its own model") for s in (mod["skins"] if mod else [])]
    return _target_items


class TYRANT_OT_choose_destination(bpy.types.Operator):
    """Choose the mod (and the species model or one of its skins) this model goes to"""

    bl_idname = "tyrant.choose_destination"
    bl_label = "Send to Tyrant: where to?"

    armature: bpy.props.StringProperty(options={"HIDDEN"})
    mod: bpy.props.EnumProperty(name="Mod", items=_mods)
    new_id: bpy.props.StringProperty(name="New mod id", description="Folder name: 3–64 lowercase letters, digits or '-'")
    new_name: bpy.props.StringProperty(name="New mod name")
    target: bpy.props.EnumProperty(name="Model for", items=_targets)

    def invoke(self, context, event):
        armature = bpy.data.objects.get(self.armature)
        data, error = project_data(armature) if armature is not None else (None, "The armature is gone.")
        if error:
            self.report({"ERROR"}, error)
            return {"CANCELLED"}
        if not send.trusted_tyrant(data["tyrant"]):
            self.report({"ERROR"}, f"This project names {data['tyrant']!r} as Tyrant; open it again from Tyrant on this PC.")
            return {"CANCELLED"}
        args = [data["tyrant"], "blender", "destinations", "-w", data["workspace"], armature[project.TAG]]
        try:
            done = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
                                  creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            answer = json.loads(done.stdout.strip().splitlines()[-1])
        except (OSError, ValueError, IndexError, subprocess.TimeoutExpired) as ex:
            self.report({"ERROR"}, f"Tyrant is not reachable ({ex}); open the project again from Tyrant.")
            return {"CANCELLED"}
        _destinations["mods"] = answer.get("mods", [])
        _destinations["species"] = answer.get("species", "")
        return context.window_manager.invoke_props_dialog(self, width=420)

    def draw(self, _context):
        layout = self.layout
        layout.prop(self, "mod")
        if self.mod == NEW_MOD:
            layout.prop(self, "new_id")
            layout.prop(self, "new_name")
        else:
            layout.prop(self, "target")

    def execute(self, context):
        armature = bpy.data.objects.get(self.armature)
        if armature is None:
            return {"CANCELLED"}
        species = _destinations["species"]
        try:
            return self._send(context, armature, species)
        except (project.ProjectError, send.SendError) as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}

    def _send(self, context, armature, species):
        if self.mod == NEW_MOD:
            if not self.new_id.strip():
                self.report({"ERROR"}, "Give the new mod an id (e.g. my-carcharo).")
                return {"CANCELLED"}
            destination = {"mod": self.new_id.strip(), "species": species, "skin": None}
            _start_send(context, armature, destination, self.new_name.strip() or self.new_id.strip())
        else:
            destination = {"mod": self.mod, "species": species, "skin": None if self.target == SPECIES_MODEL else self.target}
            _start_send(context, armature, destination)
        return {"FINISHED"}


class VIEW3D_PT_tyrant(bpy.types.Panel):
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Tyrant"
    bl_label = "Tyrant"

    def draw(self, context):
        layout = self.layout
        reason = send.why_not_sendable(context)
        if reason:
            layout.label(text=reason, icon="INFO")
            return
        armature = project.armature_of(context)
        data, error = project_data(armature)
        if error:
            layout.label(text=error, icon="ERROR")
            return
        if data.get("gameChanged"):
            layout.label(text="The game was updated since this was made: Start fresh in Tyrant for the new model.", icon="ERROR")
        source = data.get("source") or {}
        layout.label(text=f"From: {source.get('species', '?')} {source.get('skin') or ''}".strip() + (f" ({source['mod']})" if source.get("mod") else ""))
        destination = data.get("destination")
        if destination:
            what = f"skin {destination['skin']}" if destination.get("skin") else f"{destination['species']} model"
            layout.label(text=f"To: {destination['mod']} → {what}")
        else:
            layout.label(text="To: chosen on the first send")

        layout.prop(armature, "tyrant_growth", slider=True)
        if armature.tyrant_growth < 1.0:
            layout.label(text="Set Growth to 1 before sculpting or editing.", icon="ERROR")
        if not data.get("growth"):
            layout.label(text="Dump the game's data (Workspace tab) for its growth.", icon="INFO")

        for warning in send.modifier_warnings(send.sendable(armature)[1:]):
            layout.label(text=warning, icon="ERROR")
        row = layout.row()
        row.enabled = not is_busy(armature)
        row.operator("tyrant.send", icon="EXPORT")

        report = armature.get(REPORT)
        if report:
            result = json.loads(report)
            box = layout.box()
            if result.get("busy"):
                box.label(text="Tyrant is building the model…", icon="TIME")
                return
            if result.get("ok"):
                lods = ", ".join(f"LOD {i}: {v:,} vertices" for i, v in enumerate(result.get("lodVertices") or []))
                box.label(text=f"Sent. {lods}", icon="CHECKMARK")
            for error in result.get("errors") or []:
                box.label(text=error, icon="CANCEL")
            for warning in result.get("warnings") or []:
                box.label(text=warning, icon="ERROR")


CLASSES = (TYRANT_OT_send, TYRANT_OT_choose_destination, VIEW3D_PT_tyrant)
