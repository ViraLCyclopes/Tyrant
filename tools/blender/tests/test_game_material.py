import json
import os
import unittest

import bpy
import numpy as np

from test_checks import ported
from test_import import fresh_project
from tyrant_blender import gamematerial, images, importer, materials, send


def _image(name, value, size=8):
    image = bpy.data.images.new(name, size, size, alpha=True)
    image.pixels.foreach_set(np.full(size * size * 4, value, dtype=np.float32))
    return image


def with_maps(mesh, normal=None, roughness=None):
    tree = mesh.active_material.node_tree
    bsdf = tree.nodes["Principled BSDF"]
    if normal is not None:
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = normal
        normal_map = tree.nodes.new("ShaderNodeNormalMap")
        tree.links.new(node.outputs["Color"], normal_map.inputs["Color"])
        tree.links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])
    if roughness is not None:
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = roughness
        tree.links.new(node.outputs["Color"], bsdf.inputs["Roughness"])
    return mesh


class GameMaterialTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.path = fresh_project()
        self.dir = os.path.dirname(self.path)
        self.arm = importer.import_project(self.path)
        self.own = next(o for o in send.sendable(self.arm) if o.type == "MESH")

    def _ported(self, base=None, **maps):
        return with_maps(ported(self.arm, image=base or _image("body", 0.6)), **maps)

    def _pixels(self, material, slot):
        path = bpy.path.abspath(material.node_tree.nodes[slot].image.filepath)
        return images.read_pixels(bpy.data.images.load(path, check_existing=False))

    def test_images_go_into_the_pk_animal_slots_with_infant_copies(self):
        mesh = self._ported(normal=_image("nrm", 0.5), roughness=_image("rough", 0.3))
        material, note = gamematerial.use_game_material(self.arm, [mesh])
        nodes = material.node_tree.nodes
        self.assertIn(materials.GROUP, nodes)
        for slot in images.SLOTS:
            self.assertIn(slot, nodes, slot)
        self.assertIn(os.path.join("textures", "ported", "1"), os.path.normpath(bpy.path.abspath(nodes["diffuse"].image.filepath)))
        self.assertTrue(material.get(gamematerial.PORTED))
        self.assertEqual([s.material for s in mesh.material_slots], [material])
        self.assertTrue(any("eyes" in line for line in note))
        self.assertEqual(json.loads(mesh[gamematerial.NOTE]), note)

    def test_extra_red_is_smoothness_and_stays_below_the_eyes(self):
        material, _ = gamematerial.use_game_material(self.arm, [self._ported(roughness=_image("rough", 0.02))])
        extra = self._pixels(material, "extra")
        self.assertLessEqual(float(extra[0::4].max()), 0.9 + 1 / 255)
        self.assertAlmostEqual(float(extra[1::4].min()), 1.0, delta=1 / 255)  # no AO

    def test_no_roughness_image_gives_a_flat_smoothness(self):
        material, _ = gamematerial.use_game_material(self.arm, [self._ported()])
        self.assertAlmostEqual(float(self._pixels(material, "extra")[0]), 0.5, delta=1 / 255)

    def test_masks_start_blank(self):
        material, _ = gamematerial.use_game_material(self.arm, [self._ported()])
        for slot in ("pattern", "fur"):
            self.assertEqual(float(self._pixels(material, slot).reshape(-1, 4)[:, :3].max()), 0.0)

    def test_the_users_material_is_kept(self):
        mesh = self._ported()
        old = mesh.active_material
        gamematerial.use_game_material(self.arm, [mesh])
        self.assertTrue(old.use_fake_user)
        self.assertIn(old.name, bpy.data.materials)

    def test_two_materials_with_different_images_are_refused_and_shared_images_are_fine(self):
        mesh = self._ported()
        other = bpy.data.materials.new("Teeth")
        node = other.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = _image("teeth", 0.9)
        other.node_tree.links.new(node.outputs["Color"], other.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
        mesh.data.materials.append(other)
        with self.assertRaises(gamematerial.GameMaterialError) as caught:
            gamematerial.use_game_material(self.arm, [mesh])
        self.assertIn("one texture set", str(caught.exception))
        node.image = mesh.material_slots[0].material.node_tree.nodes["Image Texture"].image  # same picture: one set
        gamematerial.use_game_material(self.arm, [mesh])

    def test_a_second_run_is_refused(self):
        mesh = self._ported()
        gamematerial.use_game_material(self.arm, [mesh])
        with self.assertRaises(gamematerial.GameMaterialError) as caught:
            gamematerial.use_game_material(self.arm, [mesh])
        self.assertIn("already uses the game material", str(caught.exception))

    def test_tyrants_own_material_is_refused(self):
        with self.assertRaises(gamematerial.GameMaterialError) as caught:
            gamematerial.use_game_material(self.arm, [self.own])
        self.assertIn("already uses the game material", str(caught.exception))

    def test_a_second_ported_mesh_gets_its_own_folder(self):
        first, _ = gamematerial.use_game_material(self.arm, [self._ported()])
        second, _ = gamematerial.use_game_material(self.arm, [self._ported(base=_image("other", 0.1))])
        self.assertNotEqual(bpy.path.abspath(first.node_tree.nodes["diffuse"].image.filepath),
                            bpy.path.abspath(second.node_tree.nodes["diffuse"].image.filepath))

    def test_the_sex_switch_leaves_the_ported_material_alone(self):
        material, _ = gamematerial.use_game_material(self.arm, [self._ported()])
        before = material.node_tree.nodes["diffuse"].image.filepath
        self.arm.tyrant_sex = "FEMALE"
        self.assertEqual(material.node_tree.nodes["diffuse"].image.filepath, before)

    def test_send_takes_every_ported_slot(self):
        mesh = self._ported(normal=_image("nrm", 0.5))
        bpy.data.objects.remove(self.own)  # the port replaces the model
        gamematerial.use_game_material(self.arm, [mesh])
        changed, _ = images.collect([mesh], self.dir)
        self.assertEqual(sorted(c[0] for c in changed), sorted(images.SLOTS))

    def test_the_operator_uses_the_selection(self):
        mesh = self._ported()
        for obj in bpy.context.view_layer.objects:
            obj.select_set(obj == mesh)
        bpy.context.view_layer.objects.active = mesh
        self.assertEqual(bpy.ops.tyrant.use_game_material(), {"FINISHED"})
        self.assertTrue(mesh.active_material.get(gamematerial.PORTED))
