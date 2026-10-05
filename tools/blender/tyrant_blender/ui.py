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
    if os.path.exists(blend):  # never over someone's work: the earlier file becomes <name>.old.blend
        os.replace(blend, blend[:-len(".blend")] + ".old.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    record_blend(path, blend)


def _window_override():
    """A context with Blender's first window, or None when there is no real window (background runs list one anyway)."""
    wm = bpy.context.window_manager
    if bpy.app.background or not wm or not wm.windows:
        return None
    return bpy.context.temp_override(window=wm.windows[0])


def _run(operator, *args, **kwargs):
    override = _window_override()
    if override is None:
        return operator(*args, **kwargs)
    with override:
        return operator(*args, **kwargs)


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
    except Exception as ex:  # noqa: BLE001 - shown to the user instead of an empty scene and a console traceback
        _report(f"Tyrant could not import this model ({ex}). Open it again from Tyrant with Start fresh.")
    return None


def _report(message):
    def draw(menu, _context):
        menu.layout.label(text=message)

    override = _window_override()
    if override is not None:
        with override:
            bpy.context.window_manager.popup_menu(draw, title="Tyrant", icon="ERROR")
    print("Tyrant:", message)


def ask_then_open(path):
    """Asks what to do with the unsaved changes, then opens (the dialog's choice runs TYRANT_OT_open_project)."""
    if _window_override() is None:  # no window to ask in: a dialog then crashes Blender instead of failing
        _report("This file has unsaved changes: save it, then Open in Blender again from Tyrant.")
        return
    try:
        _run(bpy.ops.tyrant.open_project, "INVOKE_DEFAULT", path=path)
    except RuntimeError as ex:  # no window to ask in (background) or the dialog failed
        _report(f"Tyrant could not ask about the unsaved changes ({ex}); save this file, then Open in Blender again.")


def request_open(path):
    """Opens a project for Tyrant; asks first when the current file has unsaved changes. Never raises (it runs in a timer)."""
    try:
        if bpy.data.is_dirty:
            ask_then_open(path)
        else:
            open_project(path)
    except project.ProjectError as ex:
        _report(str(ex))
    except Exception as ex:  # noqa: BLE001 - a timer that raises is removed by Blender: Tyrant's later opens would be lost
        _report(f"Tyrant could not open {path}: {ex}")


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
