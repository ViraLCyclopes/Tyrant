import unittest

import bpy

from test_import import fresh_project
from tyrant_blender import growth, importer, project


def diffuse_image_path():
    return bpy.data.materials["Carch"].node_tree.nodes["diffuse"].image.filepath.replace("\\", "/")


def growth_key():
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    return mesh.data.shape_keys.key_blocks[1]  # the fixture's only key = the game's key 0


class SexTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_blend_maturity_follows_the_games_limit_per_sex(self):
        line = [i / 100 for i in range(101)]
        self.assertAlmostEqual(growth.blend_maturity(line, 1.0, 0.7), 0.7, places=5)  # a female stops at her limit
        self.assertAlmostEqual(growth.blend_maturity(line, 1.0, 1.0), 1.0, places=5)
        self.assertAlmostEqual(growth.blend_maturity(line, 0.25, 0.7), 0.25, places=5)  # the limit only changes the second half

    def test_switching_to_female_swaps_the_maps_and_her_adult_keeps_part_of_the_key(self):
        arm = importer.import_project(fresh_project())
        self.assertNotIn("/female/", diffuse_image_path())
        self.assertAlmostEqual(growth_key().value, 0.0, places=4)

        arm.tyrant_sex = "FEMALE"  # SkinDumps' Alt 1: female growth limit 0.7

        self.assertIn("/female/", diffuse_image_path())
        self.assertAlmostEqual(growth_key().value, 0.6, places=4)  # key 0 at blend maturity 0.7: (1 - 0.7) / 0.5

        arm.tyrant_sex = "MALE"
        self.assertNotIn("/female/", diffuse_image_path())
        self.assertAlmostEqual(growth_key().value, 0.0, places=4)

    def test_the_project_sex_is_shown_first(self):
        path = fresh_project()
        data = project.load(path)
        data["sex"] = "female"
        project.save(path, data)
        arm = importer.import_project(path)
        self.assertEqual(arm.tyrant_sex, "FEMALE")
        self.assertIn("/female/", diffuse_image_path())

    def test_without_sex_data_both_grow_fully(self):
        path = fresh_project()
        data = project.load(path)
        data["sexes"] = None
        project.save(path, data)
        arm = importer.import_project(path)
        arm.tyrant_sex = "FEMALE"
        self.assertAlmostEqual(growth_key().value, 0.0, places=4)
