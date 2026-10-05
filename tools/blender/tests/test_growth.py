import unittest

import bpy

from test_import import fresh_project
from tyrant_blender import growth, importer, project


def mesh_object():
    return next(o for o in bpy.data.objects if o.type == "MESH")


class GrowthTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_sampling_is_linear_between_samples(self):
        table = [i / 100 for i in range(101)]
        self.assertAlmostEqual(growth.sample(table, 0.333), 0.333, places=5)
        self.assertEqual(growth.sample(table, -1), 0.0)
        self.assertEqual(growth.sample(table, 2), 1.0)
        self.assertAlmostEqual(growth.sample(None, 0.4), 0.4)

    def test_shape_values_follow_the_game(self):
        # AnimalGrowthManager.UpdateGrowth, weights 0–100 shown as 0–1; the game's weights stay within 0–100.
        self.assertEqual(growth.shape_values(0.0, True), (0.0, 1.0))
        self.assertEqual(growth.shape_values(0.25, True), (0.5, 0.5))
        self.assertEqual(growth.shape_values(0.5, True), (1.0, 0.0))
        self.assertEqual(growth.shape_values(1.0, True), (0.0, 0.0))
        self.assertEqual(growth.shape_values(0.75, False), (0.5, 0.0))
        self.assertEqual(growth.shape_values(0.25, False), (1.0, 0.5))

    def test_slider_sets_the_growth_key_and_maturity(self):
        arm = importer.import_project(fresh_project())
        maturity = bpy.data.materials["Carch"].node_tree.nodes["Maturity"].outputs[0]
        key = mesh_object().data.shape_keys.key_blocks[1]  # the fixture's only key = the game's key 0

        arm.tyrant_growth = 0.25
        self.assertAlmostEqual(key.value, 0.5, places=4)
        self.assertAlmostEqual(maturity.default_value, 0.25, places=4)

        arm.tyrant_growth = 1.0
        self.assertAlmostEqual(key.value, 0.0, places=4)
        self.assertAlmostEqual(maturity.default_value, 1.0, places=4)

    def test_bones_take_the_babys_proportions_relative_to_the_adult(self):
        arm = importer.import_project(fresh_project())
        hip = arm.pose.bones["Hip"]
        rest_z = (arm.matrix_world @ hip.head).z

        arm.tyrant_growth = 0.0  # SkinDumps: Hip baby y 0.8, adult y 1.0 → 0.2 lower
        bpy.context.view_layer.update()
        self.assertAlmostEqual((arm.matrix_world @ hip.head).z, rest_z - 0.2, places=3)

        arm.tyrant_growth = 1.0  # adult = the rest pose you edit and send
        bpy.context.view_layer.update()
        for bone in arm.pose.bones:
            self.assertTrue(all(abs(a - b) < 1e-5 for ra, rb in zip(bone.matrix_basis, ((1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))) for a, b in zip(ra, rb)), bone.name)

    def test_unknown_growth_bones_are_skipped(self):
        arm = importer.import_project(fresh_project())  # SkinDumps also grows Arm.L, which the fixture skeleton lacks
        arm.tyrant_growth = 0.5  # must not raise

    def test_no_growth_data_uses_a_straight_line(self):
        path = fresh_project()
        data = project.load(path)
        data["growth"] = None
        project.save(path, data)
        arm = importer.import_project(path)
        arm.tyrant_growth = 0.5
        self.assertAlmostEqual(bpy.data.materials["Carch"].node_tree.nodes["Maturity"].outputs[0].default_value, 0.5, places=4)
