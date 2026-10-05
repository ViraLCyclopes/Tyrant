import os
import unittest

import bpy
import numpy as np

from test_import import fresh_project
from tyrant_blender import images, importer, send


def _diffuse(arm):
    mesh = next(o for o in send.sendable(arm) if o.type == "MESH")
    return mesh, mesh.active_material.node_tree.nodes["diffuse"]


def _paint(image, value=0.25):
    pixels = np.full(image.size[0] * image.size[1] * 4, value, dtype=np.float32)
    image.pixels.foreach_set(pixels)
    image.update()


class ImageTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.path = fresh_project()
        self.dir = os.path.dirname(self.path)
        self.arm = importer.import_project(self.path)
        self.meshes = send.sendable(self.arm)[1:]

    def test_untouched_tyrant_images_are_not_taken(self):
        changed, warnings = images.collect(self.meshes, self.dir)
        self.assertEqual(changed, [])
        self.assertEqual(warnings, [])

    def test_unsaved_painting_is_taken_and_the_file_is_left_alone(self):
        _, node = _diffuse(self.arm)
        file = bpy.path.abspath(node.image.filepath)
        before = images.file_hash(file)
        _paint(node.image)
        changed, _ = images.collect(self.meshes, self.dir)
        self.assertEqual([c[0] for c in changed], ["diffuse"])
        self.assertEqual(changed[0][1], os.path.join(self.dir, images.FOLDER, "diffuse.png"))
        self.assertTrue(os.path.isfile(changed[0][1]))
        self.assertEqual(images.file_hash(file), before)  # Tyrant's copy was not saved over
        self.assertTrue(node.image.is_dirty)  # and the painting is still unsaved, as the user left it

    def test_an_image_saved_over_is_taken(self):
        _, node = _diffuse(self.arm)
        _paint(node.image)
        node.image.save()
        self.assertFalse(node.image.is_dirty)
        changed, _ = images.collect(self.meshes, self.dir)
        self.assertEqual([c[0] for c in changed], ["diffuse"])

    def test_a_different_image_plugged_in_is_taken(self):
        _, node = _diffuse(self.arm)
        node.image = bpy.data.images.new("mine", 4, 4, alpha=True)
        changed, _ = images.collect(self.meshes, self.dir)
        self.assertEqual([c[0] for c in changed], ["diffuse"])

    def test_a_changed_image_goes_on_every_send(self):
        _, node = _diffuse(self.arm)
        _paint(node.image)
        first, _ = images.collect(self.meshes, self.dir)
        again, _ = images.collect(self.meshes, self.dir)
        self.assertEqual([c[0] for c in again], ["diffuse"])  # Tyrant compares it with the mod and skips what it holds
        self.assertEqual(first[0][2], again[0][2])  # the same pixels make the same PNG

    def test_an_image_without_pixels_is_skipped_with_a_warning(self):
        _, node = _diffuse(self.arm)
        node.image = bpy.data.images.load(os.path.join(self.dir, "textures", "diffuse.png"), check_existing=False)
        node.image.filepath = os.path.join(self.dir, "gone.png")
        node.image.reload()
        changed, warnings = images.collect(self.meshes, self.dir)
        self.assertEqual(changed, [])
        self.assertTrue(any("diffuse" in w for w in warnings))

    def test_a_float_image_is_written_with_its_shown_colours(self):
        _, node = _diffuse(self.arm)
        hdr = bpy.data.images.new("hdr", 2, 2, alpha=True, float_buffer=True)
        hdr.pixels.foreach_set(np.tile(np.array([0.2, 0.2, 0.2, 1.0], dtype=np.float32), 4))
        node.image = hdr
        changed, _ = images.collect(self.meshes, self.dir)
        back = bpy.data.images.load(changed[0][1], check_existing=False)
        value = images.read_pixels(back)[0]
        self.assertAlmostEqual(value, 0.484, delta=0.02)  # linear 0.2 shown as sRGB 0.484, as the game samples the PNG

    def test_command_passes_images_and_sex(self):
        data = {"tyrant": "C:/t/tyrant.exe", "workspace": "C:/ws"}
        args = send.command(data, "p.json", "s.glb", None, None, images=[("diffuse", "C:/x/diffuse.png", "H")], sex="female")
        self.assertIn("--image", args)
        self.assertIn("diffuse=C:/x/diffuse.png", args)
        self.assertEqual(args[-2:], ["--sex", "female"])
        self.assertNotIn("--sex", send.command(data, "p.json", "s.glb", None, None))  # no images: as before
