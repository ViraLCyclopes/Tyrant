"""
Opening Tyrant projects in this Blender: each model gets a scene of its own in the file that is open. Opening it again
switches back to that scene; nothing is ever replaced, and saving the file is the user's call.
"""
import os

import bpy

from . import importer, project

MAX_NAME = 63  # Blender's name length


def record_blend(path, blend):
    """Remembers the file holding the model's scene, so a Blender Tyrant starts later opens it."""
    data = project.load(path)
    data["blend"] = blend
    project.save(path, data)


def scene_name(data):
    source = data["source"]
    who = " ".join(p for p in (source["species"], source.get("skin")) if p)
    group = "object" if source.get("kind") == "object" else source.get("mod") or "game"
    return f"Tyrant · {group} · {who}"[:MAX_NAME]


def scene_of(path):
    return next((s for s in bpy.data.scenes if s.get(project.TAG) == path), None)


def _window():
    wm = bpy.context.window_manager
    return wm.windows[0] if wm and wm.windows else None


def _window_override():
    """A context with Blender's first window, or None when there is no real window (background runs list one anyway)."""
    window = _window()
    if bpy.app.background or window is None:
        return None
    return bpy.context.temp_override(window=window)


def _show(scene):
    window = _window()
    if window is not None:
        window.scene = scene


def show_project(path):
    """Switches to the model's scene in this file, or adds one with the model imported (Start fresh: a new one, the old kept)."""
    data = project.load(path)
    scene = scene_of(path)
    if data.get("fresh"):
        if scene is not None:
            del scene[project.TAG]
            scene.name = (scene.name[:MAX_NAME - 6] + " (old)")
            scene = None
        data["fresh"] = False
        project.save(path, data)
    if scene is not None:
        _show(scene)
        # Animations asked for at this Open in Blender that the model in this file does not have yet.
        from . import anims

        for armature in project.tagged_armatures(scene):
            try:
                anims.load_pending(armature, data, path)
            except Exception as ex:  # noqa: BLE001 - the scene still shows
                anims.report_failure(armature, "The animations", ex)
        return scene
    scene = bpy.data.scenes.new(scene_name(data))
    scene[project.TAG] = path
    _show(scene)
    with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
        importer.import_project(path)
    return scene


def open_project(path, started=False):
    """
    Tyrant's Open in Blender. started: this Blender was just started by Tyrant with an empty file, so the file that last held
    the model's scene (recorded at Send) is opened first.
    """
    data = project.load(path)
    blend = data.get("blend")
    if started and not bpy.data.filepath and blend and os.path.isfile(blend):
        override = _window_override()
        if override is not None:
            with override:
                bpy.ops.wm.open_mainfile(filepath=blend)
        else:
            bpy.ops.wm.open_mainfile(filepath=blend)
        # Loading a file replaces the data this code knew: show the scene once Blender has settled in the file.
        bpy.app.timers.register(lambda: _later(path), first_interval=0.1, persistent=True)
        return
    show_project(path)


def _later(path):
    request_open(path)
    return None


def _report(message):
    def draw(menu, _context):
        menu.layout.label(text=message)

    override = _window_override()
    if override is not None:
        with override:
            bpy.context.window_manager.popup_menu(draw, title="Tyrant", icon="ERROR")
    print("Tyrant:", message)


def request_open(path):
    """Opens a project for Tyrant (from the listener). Never raises: it runs in a timer, and Blender drops a timer that raises."""
    try:
        show_project(path)
    except project.ProjectError as ex:
        _report(str(ex))
    except Exception as ex:  # noqa: BLE001
        _report(f"Tyrant could not open this model ({ex}). Open it again from Tyrant with Start fresh.")


CLASSES = ()
