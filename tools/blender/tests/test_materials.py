import unittest

import bpy

from test_import import fresh_project
from tyrant_blender import importer, materials, project


class MaterialTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_animal_material_uses_the_pk_animal_group_with_its_maps(self):
        importer.import_project(fresh_project())
        mat = bpy.data.materials["Carch"]
        group = mat.node_tree.nodes["PK Animal"]
        self.assertEqual(group.node_tree.name, materials.GROUP)
        self.assertEqual(mat.node_tree.nodes["diffuse"].image.colorspace_settings.name, "sRGB")
        self.assertEqual(mat.node_tree.nodes["pattern"].image.colorspace_settings.name, "Non-Color")
        self.assertTrue(group.inputs["Diffuse"].is_linked)
        self.assertTrue(group.inputs["Diffuse Alpha"].is_linked)
        self.assertTrue(group.inputs["Pattern"].is_linked)
        self.assertEqual(group.inputs["Strength"].default_value, 0.0)  # vanilla skin: textures untouched
        self.assertEqual(len([n for n in bpy.data.node_groups if n.name.startswith(materials.GROUP)]), 1)
        output = next(n for n in mat.node_tree.nodes if n.type == "OUTPUT_MATERIAL")
        self.assertTrue(output.inputs["Surface"].is_linked)

    def test_missing_maps_use_defaults(self):
        path = fresh_project()
        data = project.load(path)
        data["materials"]["Carch"]["maps"] = {"diffuse": data["materials"]["Carch"]["maps"]["diffuse"]}
        project.save(path, data)
        importer.import_project(path)
        group = bpy.data.materials["Carch"].node_tree.nodes["PK Animal"]
        self.assertFalse(group.inputs["Pattern"].is_linked)
        self.assertFalse(group.inputs["Extra"].is_linked)
        self.assertNotIn("pattern", bpy.data.materials["Carch"].node_tree.nodes)

    def test_skin_colours_set_the_group_inputs(self):
        path = fresh_project()
        data = project.load(path)
        data["materials"]["Carch"]["colors"] = {"a": "#ff0000", "b": "#0000ff", "secondary": None, "eye": "#00ff00",
                                                "strength": 0.8, "softness": 0.3, "hue": 0.1, "saturation": 0, "value": 0}
        project.save(path, data)
        importer.import_project(path)
        group = bpy.data.materials["Carch"].node_tree.nodes["PK Animal"]
        self.assertAlmostEqual(group.inputs["Strength"].default_value, 0.8, places=4)
        self.assertAlmostEqual(group.inputs["Colour A"].default_value[0], 1.0, places=4)
        self.assertAlmostEqual(group.inputs["Colour A"].default_value[2], 0.0, places=4)
        self.assertEqual(group.inputs["Has Secondary"].default_value, 0.0)
        self.assertEqual(group.inputs["Has Eye"].default_value, 1.0)

    def test_infant_maps_mix_by_maturity(self):
        path = fresh_project()
        data = project.load(path)
        maps = data["materials"]["Carch"]["maps"]
        maps["infantDiffuse"] = maps["diffuse"]
        project.save(path, data)
        importer.import_project(path)
        nodes = bpy.data.materials["Carch"].node_tree.nodes
        self.assertIn("infantDiffuse", nodes)
        self.assertIn("Maturity", nodes)
        self.assertTrue(nodes["Maturity"].outputs[0].is_linked)
