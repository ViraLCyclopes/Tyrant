"""Use game material: a ported mesh's own images in the game's PK Animal material. The game draws an animal with one
texture set, so the mesh's materials must show one picture (baking them together is the user's job). Infant slots copy
the adult images; pattern and fur masks start blank; the extra map's smoothness stays below 0.9 (above it is the eyes)."""
import json
import os
import shutil

import bpy
import numpy as np

from . import checks, images, materials, project

PORTED = "tyrant_ported"
NOTE = "tyrant_material_report"
MAX_SMOOTHNESS = 0.9
FLAT_SMOOTHNESS = 0.5
MASK_SIZE = 256


class GameMaterialError(Exception):
    pass


def _folder(project_dir):
    """textures/ported/<n>: the first free number, so a second ported mesh never overwrites the first's files."""
    n = 1
    while os.path.exists(os.path.join(project_dir, "textures", "ported", str(n))):
        n += 1
    return f"textures/ported/{n}"


def _roughness(material):
    """(image, channel) behind the Principled Roughness: a Separate Color output picks its channel (glTF's ORM is G)."""
    bsdf = checks.principled(material)
    if bsdf is None or not bsdf.inputs["Roughness"].is_linked:
        return None, 0
    link = bsdf.inputs["Roughness"].links[0]
    channel = 0
    if link.from_node.type == "SEPARATE_COLOR":
        channel = list(link.from_node.outputs).index(link.from_socket)
    return checks._image_into(bsdf.inputs["Roughness"]), min(channel, 2)


def _normal(material):
    bsdf = checks.principled(material)
    return checks._image_into(bsdf.inputs["Normal"]) if bsdf is not None and bsdf.inputs["Normal"].is_linked else None


def _resized_pixels(image, width, height):
    if tuple(image.size) == (width, height):
        return images.read_pixels(image)
    copy = image.copy()
    try:
        copy.scale(width, height)
        return images.read_pixels(copy)
    finally:
        bpy.data.images.remove(copy)


def _is_game_material(material):
    return material is not None and bool(material.get(PORTED) or (material.node_tree is not None and materials.GROUP in material.node_tree.nodes))


def use_game_material(armature, meshes):
    """Builds the PK Animal material from the meshes' own images and gives it to them; returns (material, note lines)."""
    path = armature[project.TAG]
    data = project.load(path)
    game_name, spec = next(((n, s) for n, s in (data.get("materials") or {}).items() if s.get("animal")), (None, None))
    if spec is None:
        raise GameMaterialError("This model has no animal material to use.")
    for mesh in meshes:
        if any(_is_game_material(s.material) for s in mesh.material_slots):
            raise GameMaterialError(f"{mesh.name} already uses the game material.")
    found = checks.pictures(meshes)
    if len(found) > 1:
        raise GameMaterialError(checks.one_set_message(found))
    if not found:
        names = ", ".join(m.name for m in meshes)
        raise GameMaterialError(f"{names} shows no image: plug your texture into the Principled BSDF's Base Color first.")
    base = next(iter(found))
    own = [s.material for m in meshes for s in m.material_slots if s.material is not None]
    normal = next((n for n in (_normal(m) for m in own) if n is not None), None)
    rough, channel = next(((r, c) for r, c in (_roughness(m) for m in own) if r is not None), (None, 0))

    project_dir = os.path.dirname(path)
    relative = _folder(project_dir)
    out = os.path.join(project_dir, *relative.split("/"))
    os.makedirs(out, exist_ok=True)
    width, height = base.size
    maps = {}

    def put(slot, write):
        target = os.path.join(out, slot + ".png")
        write(target)
        maps[slot] = f"{relative}/{slot}.png"
        return target

    first = put("diffuse", lambda p: images.write_png(base, p))
    put("infantDiffuse", lambda p: shutil.copyfile(first, p))
    if normal is not None:
        first = put("normal", lambda p: images.write_png(normal, p))
        put("infantNormal", lambda p: shutil.copyfile(first, p))
    extra = np.zeros(width * height * 4, dtype=np.float32)
    if rough is not None:
        extra[0::4] = np.minimum(1.0 - _resized_pixels(rough, width, height)[channel::4], MAX_SMOOTHNESS)
    else:
        extra[0::4] = FLAT_SMOOTHNESS
    extra[1::4] = 1.0
    extra[3::4] = 1.0
    first = put("extra", lambda p: images.save_pixels("tyrant-extra", width, height, extra, p, non_color=True))
    put("infantExtra", lambda p: shutil.copyfile(first, p))
    blank = np.zeros(MASK_SIZE * MASK_SIZE * 4, dtype=np.float32)
    blank[3::4] = 1.0
    for slot in ("pattern", "infantPattern", "fur", "infantFur"):
        put(slot, lambda p: images.save_pixels("tyrant-mask", MASK_SIZE, MASK_SIZE, blank, p, non_color=True))

    material = bpy.data.materials.new(game_name)
    material[PORTED] = True
    materials.build(material, {"animal": True, "cutoff": spec.get("cutoff"), "maps": maps, "colors": spec.get("colors")}, project_dir)
    for old in own:
        old.use_fake_user = True  # kept in the file, unassigned
    for mesh in meshes:
        mesh.data.materials.clear()
        mesh.data.materials.append(material)
    plugged = "diffuse" + (", normal" if normal is not None else "") + " (infant slots copy them)"
    note = [f"Plugged in: {plugged}; extra from " + ("your roughness." if rough is not None else "a flat smoothness."),
            "Left blank: the pattern and fur masks (no pattern colours, no fur) until you plug your own into the 'pattern' and 'fur' image nodes.",
            "Paint the eyes above 0.9 in the extra map's red channel (the game colours them as eyes)."]
    for mesh in meshes:
        mesh[NOTE] = json.dumps(note)
    return material, note
