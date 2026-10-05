# Prints ENABLED or DISABLED: is Tyrant's extension enabled in this (normal, not factory) Blender start?
import addon_utils

loaded, enabled = addon_utils.check("bl_ext.user_default.tyrant_blender")
print("ENABLED" if enabled else "DISABLED")
