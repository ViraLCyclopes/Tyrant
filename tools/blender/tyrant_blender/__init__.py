"""Tyrant's Blender add-on: open Prehistoric Kingdom models from Tyrant and send them back."""
import os

import bpy

from . import animpanel, anims, growth, ik, ikpanel, importer, listener, materials, meshops, panel, project, rig, rigpanel, send, ui  # noqa: F401

_server = None


def _growth_changed(obj, _context):
    if obj.type == "ARMATURE" and obj.get(project.TAG):
        try:
            growth.set_growth(obj, obj.tyrant_growth)
        except (project.ProjectError, KeyError, TypeError, ValueError) as ex:
            print("Tyrant: the Growth slider cannot read this project:", ex)


def _sex_changed(obj, _context):
    if obj.type == "ARMATURE" and obj.get(project.TAG):
        try:
            path = obj[project.TAG]
            materials.apply_sex(growth.deformed_meshes(obj, bpy.context.scene.objects), project.load(path), os.path.dirname(path), growth.sex_of(obj))
            growth.set_growth(obj, obj.tyrant_growth)
        except (project.ProjectError, KeyError, TypeError, ValueError) as ex:
            print("Tyrant: the Sex switch cannot read this project:", ex)


def _drain():
    """Opens what Tyrant asked for, on Blender's main thread: one request per tick (opening replaces the file), never raising."""
    if not listener.pending.empty():
        try:
            ui.request_open(listener.pending.get())
        except Exception as ex:  # noqa: BLE001 - a timer that raises is removed by Blender
            print("Tyrant:", ex)
    return 0.25


def register():
    global _server
    for cls in ui.CLASSES + panel.CLASSES + ikpanel.CLASSES + rigpanel.CLASSES + animpanel.CLASSES:
        bpy.utils.register_class(cls)
    animpanel.register_properties()
    bpy.types.Object.tyrant_rig_only = bpy.props.BoolProperty(
        name="Rig edit only (keep the game's mesh)",
        description=("Send only the rig edit: the destination wears the game's own mesh, which stretches with the moved bones "
                     "(its own model, if it had one, is no longer used)"),
        default=False)
    bpy.types.Object.tyrant_growth = bpy.props.FloatProperty(
        name="Growth",
        description=("Baby (0) to adult (1), as the game grows this animal. Baby and juvenile proportions are shown relative to "
                     "the adult, so 1 is your model as you edit and send it. The game's limit per sex and skin is not applied"),
        min=0.0, max=1.0, default=1.0,
        update=_growth_changed)
    bpy.types.Object.tyrant_sex = bpy.props.EnumProperty(
        name="Sex", description="Show the animal as the game's male or female: their maps and how far their shape grows",
        items=(("MALE", "Male", "The male's maps and growth"), ("FEMALE", "Female", "The female's maps and growth")),
        default="MALE", update=_sex_changed)
    if not bpy.app.timers.is_registered(send.drain_finished):
        bpy.app.timers.register(send.drain_finished, first_interval=0.25, persistent=True)
    if panel.clear_stale_busy not in bpy.app.handlers.load_post:
        bpy.app.handlers.load_post.append(panel.clear_stale_busy)
    if materials.tidy_piled_up not in bpy.app.handlers.load_post:
        bpy.app.handlers.load_post.append(materials.tidy_piled_up)
    if bpy.app.background:
        return  # scripted runs (tests, installs) never listen
    try:
        _server = listener.Server()
        _server.start()
    except OSError as ex:
        _server = None  # another Blender has the port: Tyrant uses that one, or starts its own
        print(f"Tyrant: not listening for Tyrant on 127.0.0.1:{listener.PORT} ({ex}).")
    if not bpy.app.timers.is_registered(_drain):
        bpy.app.timers.register(_drain, first_interval=0.5, persistent=True)


def unregister():
    global _server
    if bpy.app.timers.is_registered(send.drain_finished):
        bpy.app.timers.unregister(send.drain_finished)
    if panel.clear_stale_busy in bpy.app.handlers.load_post:
        bpy.app.handlers.load_post.remove(panel.clear_stale_busy)
    if materials.tidy_piled_up in bpy.app.handlers.load_post:
        bpy.app.handlers.load_post.remove(materials.tidy_piled_up)
    if bpy.app.timers.is_registered(_drain):
        bpy.app.timers.unregister(_drain)
    if _server is not None:
        _server.stop()
        _server = None
    del bpy.types.Object.tyrant_growth
    del bpy.types.Object.tyrant_rig_only
    del bpy.types.Object.tyrant_sex
    animpanel.unregister_properties()
    for cls in reversed(ui.CLASSES + panel.CLASSES + ikpanel.CLASSES + rigpanel.CLASSES + animpanel.CLASSES):
        bpy.utils.unregister_class(cls)
