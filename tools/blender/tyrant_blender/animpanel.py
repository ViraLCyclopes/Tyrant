"""The Tyrant panel's Animations box: Add animations (the game's, as Actions), play one, In place, Move to IK controls."""
import json

import bpy

from . import anims, growth, project, send

REPORT = anims.REPORT  # armature: what the last Add animations said (errors and notes)


def _armature(context):
    armature = project.armature_of(context)
    if armature is None:
        raise anims.AnimError(send.why_not_sendable(context) or "Open the model from Tyrant first.")
    return armature


def _data(armature):
    data = growth._project(armature)
    if data is None:
        raise anims.AnimError("This model's Tyrant project is gone or on a network share. Open it again from Tyrant.")
    return data


def finish_add(armature_name, path, result):
    """Runs on Blender's main thread when Tyrant has written the asked animations: loads them, keeps what it said."""
    armature = bpy.data.objects.get(armature_name)
    if armature is None:
        return
    errors = list(result.get("errors") or [])
    try:
        anims.load_files(armature, project.load(path), result.get("files") or [], errors)
    except Exception as ex:  # noqa: BLE001 - whatever went wrong is shown in the panel, never left as "reading"
        armature[REPORT] = json.dumps(errors + [f"The animations could not be loaded ({type(ex).__name__}: {ex})."])
    for window in bpy.context.window_manager.windows if bpy.context.window_manager else []:
        for area in window.screen.areas:
            area.tag_redraw()


class TyrantAnimationItem(bpy.types.PropertyGroup):
    clip_id: bpy.props.StringProperty()
    label: bpy.props.StringProperty()
    details: bpy.props.StringProperty()
    pick: bpy.props.BoolProperty(name="Add")


class TYRANT_UL_animations(bpy.types.UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_propname, index):
        row = layout.row(align=True)
        row.prop(item, "pick", text="")
        row.label(text=item.label)
        row.label(text=item.details)

    def filter_items(self, context, data, propname):
        items = getattr(data, propname)
        flags = [self.bitflag_filter_item] * len(items)
        if self.filter_name:
            wanted = self.filter_name.lower()
            flags = [self.bitflag_filter_item if wanted in item.label.lower() else 0 for item in items]
        return flags, []


class TYRANT_OT_anim_add(bpy.types.Operator):
    """Add the game's animations of this species as Actions (pick them from the list)"""

    bl_idname = "tyrant.anim_add"
    bl_label = "Add animations"
    bl_options = {"REGISTER"}

    def invoke(self, context, event):
        try:
            armature = _armature(context)
            data = _data(armature)
            listed = anims.ensure_list(armature, data, armature[project.TAG])
        except (anims.AnimError, project.ProjectError) as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        if not listed:
            self.report({"ERROR"}, "No animations of this species were found in the game data (run the data dump again in Tyrant).")
            return {"CANCELLED"}
        items = context.window_manager.tyrant_anim_items
        items.clear()
        for animation in listed:
            item = items.add()
            item.clip_id = animation["id"]
            item.label = animation["name"]
            flags = [f for f, on in (("loops", animation.get("loops")), ("travels", animation.get("travels"))) if on]
            item.details = f"{animation.get('length', 0):.2f} s" + (f" · {', '.join(flags)}" if flags else "")
        return context.window_manager.invoke_props_dialog(self, width=520)

    def draw(self, context):
        wm = context.window_manager
        row = self.layout.row(align=True)
        row.operator("tyrant.anim_pick_all", text="Select all").pick = True
        row.operator("tyrant.anim_pick_all", text="Clear").pick = False
        self.layout.template_list("TYRANT_UL_animations", "", wm, "tyrant_anim_items", wm, "tyrant_anim_index", rows=14)

    def execute(self, context):
        try:
            armature = _armature(context)
            data = _data(armature)
        except (anims.AnimError, project.ProjectError) as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        ids = [item.clip_id for item in context.window_manager.tyrant_anim_items if item.pick]
        if not ids:
            self.report({"WARNING"}, "Tick the animations to add.")
            return {"CANCELLED"}
        if not send.trusted_tyrant(data.get("tyrant")):
            self.report({"ERROR"}, "Reopen this model from Tyrant (on this PC) to add animations.")
            return {"CANCELLED"}
        path = armature[project.TAG]
        armature[REPORT] = json.dumps([f"Tyrant is reading {len(ids)} animation(s)…"])
        name = armature.name
        args = [data["tyrant"], "blender", "animations", "-w", data["workspace"], path, *ids]
        send.run_async(args, lambda result: finish_add(name, path, result))
        return {"FINISHED"}


class TYRANT_OT_anim_pick_all(bpy.types.Operator):
    """Tick (or untick) every animation in the list"""

    bl_idname = "tyrant.anim_pick_all"
    bl_label = "Select all"
    bl_options = {"INTERNAL"}

    pick: bpy.props.BoolProperty(default=True)

    def execute(self, context):
        for item in context.window_manager.tyrant_anim_items:
            item.pick = self.pick
        return {"FINISHED"}


class TYRANT_OT_anim_play(bpy.types.Operator):
    """Play this animation (the scene's frame range follows it)"""

    bl_idname = "tyrant.anim_play"
    bl_label = "Play"
    bl_options = {"REGISTER", "UNDO"}

    action: bpy.props.StringProperty()

    def execute(self, context):
        try:
            armature = _armature(context)
        except anims.AnimError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        action = bpy.data.actions.get(self.action)
        if action is None:
            self.report({"ERROR"}, f"There is no animation '{self.action}'.")
            return {"CANCELLED"}
        anims.play(armature, action)
        return {"FINISHED"}


class TYRANT_OT_anim_to_ik(bpy.types.Operator):
    """Key the feet and head controls to follow the playing animation on every frame and switch those chains to IK, to edit
    with planted feet (Bake frame range turns it back into bone keys)"""

    bl_idname = "tyrant.anim_to_ik"
    bl_label = "Move to IK controls"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        try:
            anims.move_to_ik(_armature(context), context.scene)
        except anims.AnimError as ex:
            self.report({"ERROR"}, str(ex))
            return {"CANCELLED"}
        return {"FINISHED"}


def draw(layout, context, armature, data, say):
    box = layout.box()
    box.label(text="Animations", icon="ACTION")
    box.operator("tyrant.anim_add", icon="ADD")
    playing = armature.animation_data.action if armature.animation_data else None
    column = box.column(align=True)
    for action in anims.tyrant_actions():
        row = column.row(align=True)
        operator = row.operator("tyrant.anim_play", text=action.name, icon="PLAY" if action == playing else "ACTION",
                                depress=action == playing)
        operator.action = action.name
    if anims.tyrant_actions():
        box.prop(armature, "tyrant_in_place")
        box.operator("tyrant.anim_to_ik", icon="CON_KINEMATIC")
        say(box, context, "The IK controls follow the animation; Move to IK controls to edit it with them.", "INFO")
        say(box, context, "Blender does not run the game's foot planting or look-at, so feet can sit slightly off.", "INFO")
    for line in json.loads(armature.get(REPORT) or "[]"):
        say(box, context, line, "ERROR" if "not" in line or "could" in line else "INFO")


CLASSES = (TyrantAnimationItem, TYRANT_UL_animations, TYRANT_OT_anim_add, TYRANT_OT_anim_pick_all, TYRANT_OT_anim_play,
           TYRANT_OT_anim_to_ik)


def register_properties():
    bpy.types.WindowManager.tyrant_anim_items = bpy.props.CollectionProperty(type=TyrantAnimationItem)
    bpy.types.WindowManager.tyrant_anim_index = bpy.props.IntProperty()
    bpy.types.Object.tyrant_in_place = bpy.props.BoolProperty(
        name="In place",
        description="Hide the animation's travel (its keys are not changed); off shows the animal walking off as in the game",
        default=False,
        update=lambda obj, _context: anims.set_in_place(obj, obj.tyrant_in_place) if obj.type == "ARMATURE" else None)


def unregister_properties():
    del bpy.types.WindowManager.tyrant_anim_items
    del bpy.types.WindowManager.tyrant_anim_index
    del bpy.types.Object.tyrant_in_place
