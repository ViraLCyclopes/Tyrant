"""Tyrant's Blender add-on: open Prehistoric Kingdom models from Tyrant and send them back."""
import bpy

from . import growth, importer, materials, meshops, project  # noqa: F401


def _growth_changed(obj, _context):
    if obj.type == "ARMATURE" and obj.get(project.TAG):
        growth.set_growth(obj, obj.tyrant_growth)


def register():
    bpy.types.Object.tyrant_growth = bpy.props.FloatProperty(
        name="Growth", description="Baby (0) to adult (1), as the game grows this animal", min=0.0, max=1.0, default=1.0,
        update=_growth_changed)


def unregister():
    del bpy.types.Object.tyrant_growth
