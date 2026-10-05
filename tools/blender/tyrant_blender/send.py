"""Send to Tyrant: export only the Tyrant armature and the meshes it deforms, then let tyrant.exe add the model to the mod."""
import json
import os
import queue
import subprocess
import threading

import bpy

from . import growth


def sendable(armature):
    """The armature and every mesh in the scene with an Armature modifier on it (a ported mesh counts once it has one)."""
    meshes = [o for o in bpy.context.scene.objects
              if o.type == "MESH" and any(m.type == "ARMATURE" and m.object == armature for m in o.modifiers)]
    return [armature] + meshes


def modifier_warnings(meshes):
    """Modifiers the export cannot keep: the glTF exporter does not apply modifiers on meshes with shape keys."""
    return [f"'{mesh.name}' has a {mod.type} modifier; it is not applied when sending (shape keys). Apply it first or remove it."
            for mesh in meshes if mesh.type == "MESH" for mod in mesh.modifiers if mod.type != "ARMATURE"]


class SendError(Exception):
    pass


def export(armature, path):
    """Writes the .glb Tyrant builds the model from: adult, rest pose, deforming bones only (Tyrant's IK bones stay out),
    materials as names and viewport colours only (Tyrant matches them by name; placeholder mode drops the names). Hidden
    objects go too (hidden only for the export's length). Your pose and the Growth shown are as before afterwards."""
    shown = armature.tyrant_growth
    armature.tyrant_growth = 1.0
    growth.set_growth(armature, 1.0)
    try:
        _export(armature, path)
    finally:
        armature.tyrant_growth = shown
        growth.set_growth(armature, shown)


def _export(armature, path):
    """The export itself, at adult (see export)."""
    objects = sendable(armature)
    view_layer = bpy.context.view_layer
    excluded = [o.name for o in objects if view_layer.objects.get(o.name) is None]
    if excluded:
        raise SendError(f"{', '.join(excluded)} is in a collection excluded from the view layer; tick it in the Outliner, then send.")
    hidden = [(o, o.hide_get(), o.hide_viewport) for o in objects]
    selected = [o for o in view_layer.objects if o.select_get()]
    active = view_layer.objects.active
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    # Blender's glTF exporter skins a mesh only when it hangs under the armature: a mesh brought in from another file keeps
    # its old parent (only its Armature modifier points here), so for the export it is parented here without moving.
    parents = [(o, o.parent, o.parent_type, o.parent_bone, o.matrix_parent_inverse.copy()) for o in objects[1:] if o.parent != armature]
    try:
        for obj, parent, _, _, inverse in parents:
            world_of_parent = parent.matrix_world if parent is not None else None
            obj.parent = armature
            obj.parent_type = "OBJECT"
            obj.matrix_parent_inverse = armature.matrix_world.inverted() @ (world_of_parent @ inverse if world_of_parent is not None else inverse)
        for obj, _, _ in hidden:
            obj.hide_viewport = False
            obj.hide_set(False)
        for obj in view_layer.objects:
            obj.select_set(obj in objects)
        view_layer.objects.active = armature
        bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_skins=True, export_morph=True,
                                  export_morph_normal=True, export_animations=False, export_materials="VIEWPORT", export_yup=True,
                                  export_rest_position_armature=True, export_apply=False,
                                  export_def_bones=True)
    finally:
        for obj in view_layer.objects:
            obj.select_set(obj in selected)
        view_layer.objects.active = active
        for obj, hide, hide_viewport in hidden:
            obj.hide_set(hide)
            obj.hide_viewport = hide_viewport
        for obj, parent, parent_type, parent_bone, inverse in parents:
            obj.parent = parent
            obj.parent_type = parent_type
            obj.parent_bone = parent_bone
            obj.matrix_parent_inverse = inverse


def command(data, project_path, glb, destination, new_mod_name, images=(), sex="male"):
    """tyrant.exe's arguments for sending this project's export (with a destination chosen now, or the saved one), plus the
    changed images ((slot, png, hash)) and the sex shown, whose maps they go to."""
    args = [data["tyrant"], "blender", "send", "-w", data["workspace"], project_path, glb]
    if destination:
        args += ["--mod", destination["mod"]]
        if destination.get("skin"):
            args += ["--skin", destination["skin"]]
        if new_mod_name:
            args += ["--new-mod-name", new_mod_name]
    for slot, path, *_ in images:
        args += ["--image", f"{slot}={path}"]
    if images:
        args += ["--sex", sex]
    return args


def _not_found(exe):
    return {"ok": False, "errors": [f"Tyrant was not found at {exe}; open the project again from Tyrant so it records where Tyrant is now."],
            "warnings": [], "lodVertices": []}


def run(args, timeout=900):
    """Runs tyrant.exe and returns its JSON answer (its last line that is JSON)."""
    if not os.path.isfile(args[0]):
        return _not_found(args[0])
    try:
        done = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout,
                              creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    except FileNotFoundError:
        return _not_found(args[0])
    except subprocess.TimeoutExpired:
        return {"ok": False, "errors": ["Tyrant took too long to build the model."], "warnings": [], "lodVertices": []}
    for line in reversed(done.stdout.splitlines()):
        line = line.strip()
        if line.startswith("{"):
            try:
                return json.loads(line)
            except ValueError:
                break
    tail = " ".join((done.stderr or done.stdout).strip().splitlines()[-3:])
    return {"ok": False, "errors": [f"Tyrant did not answer ({tail or 'no output'})."], "warnings": [], "lodVertices": []}


finished = queue.Queue()


def run_async(args, done):
    """Runs tyrant.exe off Blender's main thread; done(result) runs on it, from drain_finished (a main-thread timer)."""
    def work():
        finished.put((done, run(args)))

    threading.Thread(target=work, name="tyrant-send", daemon=True).start()


def drain_finished():
    """Applies finished sends on Blender's main thread (registered as a timer by the add-on)."""
    while not finished.empty():
        done, result = finished.get()
        try:
            done(result)
        except Exception as ex:  # noqa: BLE001 - a timer that raises is removed by Blender
            print("Tyrant:", ex)
    return 0.25


def trusted_tyrant(path):
    """Only a tyrant.exe on this PC: the exe named in a project file is run by Send."""
    return (isinstance(path, str) and os.path.isabs(path) and project_is_local(path)
            and os.path.basename(path).lower() == "tyrant.exe")


def project_is_local(path):
    from . import project

    return project.is_local(path)


def why_not_sendable(context):
    """Why Send cannot pick an armature here (None when it can)."""
    from . import project

    if project.armature_of(context) is not None:
        return None
    scene_project = context.scene.get(project.TAG)
    if scene_project and not project.tagged_armatures(context.scene):
        return "This is a game object, opened for reference or as a base: Send to Tyrant is for animals (new objects come later)."
    if len(project.tagged_armatures(context.scene)) > 1:
        return "Select the model or armature to send (this scene has several from Tyrant)."
    return "This scene was not opened from Tyrant; use Open in Blender in Tyrant."
