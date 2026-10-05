"""The Tyrant panel (3D view sidebar → Tyrant): where the model came from and goes, the Growth slider, Send to Tyrant, the report."""
import json
import os
import subprocess
import textwrap

import bpy

from . import checks, gamematerial, growth, images, project, send, ui

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


@bpy.app.handlers.persistent  # else Blender drops it at the first file load
def clear_stale_busy(*_args):
    """A file saved while Tyrant was building would say "building" forever: on load it becomes "send again"."""
    for obj in bpy.data.objects:
        if obj.type == "ARMATURE" and is_busy(obj):
            obj[REPORT] = json.dumps({"ok": False, "errors": ["The last send did not finish (Blender was closed meanwhile); send again."]})


def _stop_on_problems(armature, data):
    """Refuses Send with its problems in the panel's report box (the first also as Blender's error)."""
    found = checks.problems(armature, data)
    if found:
        armature[REPORT] = json.dumps({"ok": False, "errors": found})
        _redraw()
        raise send.SendError(found[0])


def _start_send(context, armature, destination=None, new_mod_name=None):
    path = armature[project.TAG]
    data = project.load(path)
    if not send.trusted_tyrant(data.get("tyrant")):
        raise send.SendError(f"This project names {data.get('tyrant')!r} as Tyrant; open it again from Tyrant on this PC.")
    _stop_on_problems(armature, data)
    folder = os.path.dirname(path)
    glb = os.path.join(folder, "send.glb")
    send.export(armature, glb)
    target = destination or data.get("destination") or {}
    sex = armature.tyrant_sex.lower()
    key = images.destination_key(target, sex)
    changed, notes = images.collect(send.sendable(armature)[1:], folder)
    changed = images.not_sent_yet(armature, changed, key)
    if bpy.data.filepath:  # an untitled file is the user's to name and save
        bpy.ops.wm.save_mainfile()
        ui.record_blend(path, bpy.data.filepath)  # a Blender Tyrant starts later opens this file at the model's scene
    armature[REPORT] = json.dumps({"busy": True})
    name = armature.name

    def done(result):
        target_object = bpy.data.objects.get(name)
        if target_object is not None:
            result["warnings"] = notes + list(result.get("warnings") or [])
            target_object[REPORT] = json.dumps(result)
            if result.get("ok"):
                written = set(result.get("images") or [])
                images.record_sent(target_object, [c for c in changed if c[0] in written], key)
        _redraw()
        return None

    send.run_async(send.command(data, path, glb, destination, new_mod_name, changed, sex), done)


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
            _stop_on_problems(armature, data)
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


class TYRANT_OT_use_game_material(bpy.types.Operator):
    """Give the selected meshes the game's animal material with their own images (the game draws an animal with one texture set)"""

    bl_idname = "tyrant.use_game_material"
    bl_label = "Use game material"

    def execute(self, context):
        armature = project.armature_of(context)
        if armature is None:
            self.report({"ERROR"}, send.why_not_sendable(context) or "Open the model from Tyrant first.")
            return {"CANCELLED"}
        meshes = [o for o in context.selected_objects if o.type == "MESH"]
        if not meshes:
            self.report({"ERROR"}, "Select the mesh(es) to give the game material.")
            return {"CANCELLED"}
        try:
            _material, note = gamematerial.use_game_material(armature, meshes)
        except (gamematerial.GameMaterialError, project.ProjectError, ValueError) as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        self.report({"INFO"}, note[0])
        _redraw()
        return {"FINISHED"}


def chars_per_line(width, ui_scale):
    """How many characters of Blender's UI font fit a sidebar this wide (its labels never wrap by themselves)."""
    return max(12, int((width - 40 * ui_scale) / (6.5 * ui_scale)))


def wrap_lines(text, width, ui_scale):
    return textwrap.wrap(text, chars_per_line(width, ui_scale)) or [text]


def say(layout, context, text, icon="NONE"):
    """A message on as many lines as the sidebar needs (one label is cut in the middle with '...')."""
    width = context.region.width if context.region else 300
    column = layout.column(align=True)
    for i, line in enumerate(wrap_lines(text, width, context.preferences.view.ui_scale)):
        column.label(text=line, icon=icon if i == 0 else ("BLANK1" if icon != "NONE" else "NONE"))


class VIEW3D_PT_tyrant(bpy.types.Panel):
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Tyrant"
    bl_label = "Tyrant"

    def draw(self, context):
        layout = self.layout
        reason = send.why_not_sendable(context)
        if reason:
            say(layout, context, reason, "INFO")
            return
        armature = project.armature_of(context)
        data, error = project_data(armature)
        if error:
            say(layout, context, error, "ERROR")
            return
        if data.get("gameChanged"):
            say(layout, context, "The game was updated since this was made: Start fresh in Tyrant for the new model.", "ERROR")
        source = data.get("source") or {}
        say(layout, context, f"From: {source.get('species', '?')} {source.get('skin') or ''}".strip() + (f" ({source['mod']})" if source.get("mod") else ""))
        destination = data.get("destination")
        if destination:
            what = f"skin {destination['skin']}" if destination.get("skin") else f"{destination['species']} model"
            say(layout, context, f"To: {destination['mod']} → {what}")
        else:
            say(layout, context, "To: chosen on the first send")

        layout.prop(armature, "tyrant_sex", expand=True)
        layout.prop(armature, "tyrant_growth", slider=True)
        sexes = data.get("sexes")
        if sexes:
            say(layout, context, f"In game: male {sexes['male']['size']:g}x, female {sexes['female']['size']:g}x size (not shown)")
        if armature.tyrant_growth < 1.0:
            say(layout, context, "Set Growth to 1 before sculpting or editing.", "ERROR")
        if not data.get("growth"):
            say(layout, context, "Dump the game's data (Workspace tab) for its growth.", "INFO")

        layout.operator("tyrant.use_game_material", icon="MATERIAL")
        active = context.active_object
        if active is not None and active.get(gamematerial.NOTE):
            note_box = layout.box()
            for line in json.loads(active[gamematerial.NOTE]):
                say(note_box, context, line, "INFO")

        for warning in send.modifier_warnings(send.sendable(armature)[1:]):
            say(layout, context, warning, "ERROR")
        goes, stays = checks.preview(armature, context.scene)
        preview = layout.box()
        say(preview, context, "Sends: " + ", ".join(goes), "EXPORT")
        for name, why in stays[:6]:
            say(preview, context, f"{name}: {why}", "BLANK1")
        if len(stays) > 6:
            say(preview, context, f"…and {len(stays) - 6} more left out", "BLANK1")
        row = layout.row()
        row.enabled = not is_busy(armature)
        row.operator("tyrant.send", icon="EXPORT")

        report = armature.get(REPORT)
        if report:
            result = json.loads(report)
            box = layout.box()
            if result.get("busy"):
                say(box, context, "Tyrant is building the model…", "TIME")
                return
            if result.get("ok"):
                lods = ", ".join(f"LOD {i}: {v:,} vertices" for i, v in enumerate(result.get("lodVertices") or []))
                say(box, context, f"Sent. {lods}", "CHECKMARK")
            if result.get("ok") and result.get("images"):
                say(box, context, f"Images: {', '.join(result['images'])} ({result.get('imagesTo') or 'the mod'})", "IMAGE_DATA")
            for error in result.get("errors") or []:
                say(box, context, error, "CANCEL")
            for warning in result.get("warnings") or []:
                say(box, context, warning, "ERROR")


CLASSES = (TYRANT_OT_send, TYRANT_OT_choose_destination, TYRANT_OT_use_game_material, VIEW3D_PT_tyrant)
