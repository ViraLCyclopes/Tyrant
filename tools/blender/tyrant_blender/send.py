"""Send to Tyrant: export only the Tyrant armature and the meshes it deforms, then let tyrant.exe add the model to the mod."""
import json
import os
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


def export(armature, path):
    """Writes the .glb Tyrant builds the model from: adult, rest pose, placeholder materials (Tyrant matches them by name)."""
    armature.tyrant_growth = 1.0
    growth.set_growth(armature, 1.0)
    objects = sendable(armature)
    view_layer = bpy.context.view_layer
    selected = [o for o in view_layer.objects if o.select_get()]
    active = view_layer.objects.active
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for obj in view_layer.objects:
        obj.select_set(obj in objects)
    view_layer.objects.active = armature
    try:
        bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_skins=True, export_morph=True,
                                  export_morph_normal=True, export_animations=False, export_materials="PLACEHOLDER", export_yup=True,
                                  export_rest_position_armature=True, export_apply=False)
    finally:
        for obj in view_layer.objects:
            obj.select_set(obj in selected)
        view_layer.objects.active = active


def command(data, project_path, glb, destination, new_mod_name):
    """tyrant.exe's arguments for sending this project's export (with a destination chosen now, or the saved one)."""
    args = [data["tyrant"], "blender", "send", "-w", data["workspace"], project_path, glb]
    if destination:
        args += ["--mod", destination["mod"]]
        if destination.get("skin"):
            args += ["--skin", destination["skin"]]
        if new_mod_name:
            args += ["--new-mod-name", new_mod_name]
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


def run_async(args, done):
    """Runs tyrant.exe off Blender's main thread, then calls done(result) on it."""
    def work():
        result = run(args)
        bpy.app.timers.register(lambda: done(result), first_interval=0.0)

    threading.Thread(target=work, name="tyrant-send", daemon=True).start()


def why_not_sendable(context):
    """Why Send cannot pick an armature here (None when it can)."""
    from . import project

    if project.armature_of(context) is not None:
        return None
    if len(project.tagged_armatures(context.scene)) > 1:
        return "Select the model or armature to send (this scene has several from Tyrant)."
    return "This scene was not opened from Tyrant; use Open in Blender in Tyrant."
