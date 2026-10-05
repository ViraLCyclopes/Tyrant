"""Opening Tyrant projects in this Blender (from Tyrant's listener or a fresh Blender Tyrant started)."""
import os

import bpy

from . import importer, project


def project_blend(path, data):
    """Where a project's .blend lives: next to its tyrant-blender.json, named after the species (and skin)."""
    source = data["source"]
    stem = "-".join(p for p in (source["species"], source.get("skin")) if p).lower().replace(" ", "-")
    return os.path.join(os.path.dirname(path), f"{stem}.blend")


def record_blend(path, blend):
    data = project.load(path)
    data["blend"] = blend
    project.save(path, data)


def import_and_save(path):
    """Imports the project into the current (empty) file and saves it as the project's .blend, so reopening finds the work."""
    data = project.load(path)
    importer.import_project(path)
    blend = project_blend(path, data)
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    record_blend(path, blend)


def _window_override():
    wm = bpy.context.window_manager
    return bpy.context.temp_override(window=wm.windows[0]) if wm and wm.windows else None


def _run(operator, **kwargs):
    override = _window_override()
    if override is None:
        return operator(**kwargs)
    with override:
        return operator(**kwargs)


def open_project(path):
    """Opens the project's saved .blend, or a new file with the model imported (then saved as the project's .blend)."""
    data = project.load(path)
    blend = data.get("blend")
    if blend and os.path.isfile(blend):
        _run(bpy.ops.wm.open_mainfile, filepath=blend)
        return
    _run(bpy.ops.wm.read_homefile, use_empty=True)
    # Loading a file replaces the data the running operator knew: import once Blender has settled in the new file.
    bpy.app.timers.register(lambda: _import_later(path), first_interval=0.1, persistent=True)


def _import_later(path):
    try:
        import_and_save(path)
    except project.ProjectError as ex:
        _report(str(ex))
    return None


def _report(message):
    def draw(menu, _context):
        menu.layout.label(text=message)

    wm = bpy.context.window_manager
    if wm and wm.windows:
        wm.popup_menu(draw, title="Tyrant", icon="ERROR")
    print("Tyrant:", message)


def request_open(path):
    """Opens a project for Tyrant; asks first when the current file has unsaved changes."""
    if bpy.data.is_dirty:
        _run(bpy.ops.tyrant.open_project, "INVOKE_DEFAULT", path=path)
    else:
        try:
            open_project(path)
        except project.ProjectError as ex:
            _report(str(ex))


class TYRANT_OT_open_project(bpy.types.Operator):
    """Open a Tyrant project (asks what to do with unsaved changes)"""

    bl_idname = "tyrant.open_project"
    bl_label = "Open from Tyrant"

    path: bpy.props.StringProperty(options={"HIDDEN"})
    action: bpy.props.EnumProperty(
        name="Unsaved changes",
        items=(("SAVE", "Save and open", "Save this file, then open the Tyrant project"),
               ("DISCARD", "Open without saving", "Lose the unsaved changes in this file"),
               ("CANCEL", "Cancel", "Stay in this file")),
        default="SAVE")

    def invoke(self, context, event):
        return context.window_manager.invoke_props_dialog(self, title="This file has unsaved changes")

    def execute(self, context):
        if self.action == "CANCEL":
            return {"CANCELLED"}
        if self.action == "SAVE":
            if not bpy.data.filepath:
                self.report({"ERROR"}, "This file was never saved: save it first (File → Save As), or choose Open without saving.")
                return {"CANCELLED"}
            bpy.ops.wm.save_mainfile()
        try:
            open_project(self.path)
        except project.ProjectError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        return {"FINISHED"}


CLASSES = (TYRANT_OT_open_project,)
