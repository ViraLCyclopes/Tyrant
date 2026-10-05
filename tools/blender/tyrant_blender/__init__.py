"""Tyrant's Blender add-on: open Prehistoric Kingdom models from Tyrant and send them back."""
import bpy

from . import growth, importer, listener, materials, meshops, panel, project, send, ui  # noqa: F401

_server = None


def _growth_changed(obj, _context):
    if obj.type == "ARMATURE" and obj.get(project.TAG):
        growth.set_growth(obj, obj.tyrant_growth)


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
    for cls in ui.CLASSES + panel.CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Object.tyrant_growth = bpy.props.FloatProperty(
        name="Growth", description="Baby (0) to adult (1), as the game grows this animal", min=0.0, max=1.0, default=1.0,
        update=_growth_changed)
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
    if bpy.app.timers.is_registered(_drain):
        bpy.app.timers.unregister(_drain)
    if _server is not None:
        _server.stop()
        _server = None
    del bpy.types.Object.tyrant_growth
    for cls in reversed(ui.CLASSES + panel.CLASSES):
        bpy.utils.unregister_class(cls)
