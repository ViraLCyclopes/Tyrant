"""The images Send takes: slot images the user changed (painted, saved over, or swapped), written as PNGs for Tyrant.
Unchanged means Tyrant's own copy as Tyrant wrote it (textures/.tyrant-sources.json) with no unsaved painting. The user's
image files and Tyrant's copies are never written: Send writes the current pixels to <project>/send-images/<slot>.png."""
import hashlib
import json
import os

import bpy
import numpy as np

SLOTS = ("diffuse", "normal", "extra", "pattern", "fur", "infantDiffuse", "infantNormal", "infantExtra", "infantPattern", "infantFur")
LEDGER = ".tyrant-sources.json"
FOLDER = "send-images"


def file_hash(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def _ledger(project_dir):
    try:
        with open(os.path.join(project_dir, "textures", LEDGER), encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError):
        return {}
    return data if isinstance(data, dict) else {}


def read_pixels(image):
    width, height = image.size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    return pixels


def save_pixels(name, width, height, pixels, path, non_color, float_buffer=False):
    """Saves RGBA float pixels as a PNG through a temporary image (removed afterwards)."""
    image = bpy.data.images.new(name, width, height, alpha=True, float_buffer=float_buffer)
    try:
        if non_color:
            image.colorspace_settings.name = "Non-Color"
        image.pixels.foreach_set(pixels)
        image.filepath_raw = path
        image.file_format = "PNG"
        image.save()
    finally:
        bpy.data.images.remove(image)


def write_png(image, path):
    """The image's current pixels (unsaved painting included) as a PNG at path; the image itself is left as it was."""
    width, height = image.size
    if width == 0 or height == 0:
        raise ValueError(f"image '{image.name}' has no pixels (is its file missing?)")
    pixels = read_pixels(image)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    non_color = image.colorspace_settings.name == "Non-Color"
    if image.is_float and not non_color:
        # A float image holds linear colours and Blender saves them to PNG unconverted: encode them as sRGB, as shown.
        rgb = np.clip(pixels.reshape(-1, 4)[:, :3], 0.0, 1.0)
        pixels.reshape(-1, 4)[:, :3] = np.where(rgb <= 0.0031308, rgb * 12.92, 1.055 * np.power(rgb, 1 / 2.4) - 0.055)
    save_pixels("tyrant-send", width, height, pixels, path, non_color)


def slot_images(meshes):
    """(slot, image) for each slot-named image node in the meshes' materials; the first material with a slot wins."""
    found = {}
    for mesh in meshes:
        for slot in mesh.material_slots:
            material = slot.material
            if material is None or material.node_tree is None:
                continue
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.name in SLOTS and node.image is not None:
                    found.setdefault(node.name, node.image)
    return list(found.items())


def _untouched(image, project_dir, known):
    """Tyrant's own copy as Tyrant wrote it: a saved file in textures/ whose hash the ledger has, and no unsaved painting."""
    if image.is_dirty or image.packed_file is not None or image.source != "FILE":
        return False
    path = os.path.normpath(bpy.path.abspath(image.filepath))
    try:
        rel = os.path.relpath(path, os.path.join(project_dir, "textures")).replace("\\", "/")
    except ValueError:  # another drive
        return False
    entry = known.get(rel)
    if rel.startswith("../") or not isinstance(entry, dict) or not os.path.isfile(path):
        return False
    expected = entry.get("Hash") or entry.get("hash")
    return isinstance(expected, str) and file_hash(path) == expected.upper()


def collect(meshes, project_dir):
    """Writes each changed slot's PNG to <project>/send-images/; returns ([(slot, png, hash)], warnings)."""
    out = os.path.join(project_dir, FOLDER)
    os.makedirs(out, exist_ok=True)
    for name in os.listdir(out):
        if name.lower().endswith(".png"):
            os.remove(os.path.join(out, name))
    known = _ledger(project_dir)
    changed, warnings = [], []
    for slot, image in slot_images(meshes):
        if _untouched(image, project_dir, known):
            continue
        path = os.path.join(out, slot + ".png")
        try:
            write_png(image, path)
        except (ValueError, RuntimeError) as ex:
            warnings.append(f"The '{slot}' image was not sent: {ex}.")
            continue
        changed.append((slot, path, file_hash(path)))
    return changed, warnings
