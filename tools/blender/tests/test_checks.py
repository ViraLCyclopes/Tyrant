import unittest

import bpy

from test_import import fresh_project
from tyrant_blender import checks, importer, project, send


def ported(arm, name="Ported", image=None, groups=("Hip",), keys=("Infant",)):
    """A mesh deformed by the rig, weighted to the given groups, with shape keys and (optionally) a Principled image."""
    bpy.ops.mesh.primitive_plane_add()
    mesh = bpy.context.active_object
    mesh.name = name
    mesh.modifiers.new("Armature", "ARMATURE").object = arm
    for group in groups:
        mesh.vertex_groups.new(name=group).add([v.index for v in mesh.data.vertices], 1.0, "REPLACE")
    if keys:
        mesh.shape_key_add(name="Basis")
        for key in keys:
            mesh.shape_key_add(name=key)
    if image is not None:
        material = bpy.data.materials.new(name + "Mat")
        tree = material.node_tree
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = image
        tree.links.new(node.outputs["Color"], tree.nodes["Principled BSDF"].inputs["Base Color"])
        mesh.data.materials.append(material)
    return mesh


class CheckTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.path = fresh_project()
        self.arm = importer.import_project(self.path)
        self.data = project.load(self.path)
        self.own = next(o for o in send.sendable(self.arm) if o.type == "MESH")

    def test_preview_says_what_goes_and_why_the_rest_stays(self):
        bpy.ops.mesh.primitive_cube_add()
        cube = bpy.context.active_object
        other = bpy.data.objects.new("OtherRig", bpy.data.armatures.new("OtherRig"))
        bpy.context.scene.collection.objects.link(other)
        bpy.ops.object.light_add()
        goes, stays = checks.preview(self.arm, bpy.context.scene)
        self.assertEqual(set(goes), {self.arm.name, self.own.name})
        reasons = dict(stays)
        self.assertIn("Armature modifier", reasons[cube.name])
        self.assertEqual(reasons["OtherRig"], "another armature")
        self.assertIn("not a mesh", reasons.values())

    def test_a_scene_with_no_deformed_mesh_says_how_to_add_the_modifier(self):
        self.own.modifiers.clear()
        found = checks.problems(self.arm, self.data)
        self.assertEqual(len(found), 1)
        self.assertIn("No mesh is deformed by", found[0])
        self.assertIn("Armature modifier", found[0])

    def test_a_mesh_weighted_to_another_skeleton_is_named(self):
        ported(self.arm, groups=("jwe_head", "jwe_neck"))
        self.own.modifiers.clear()
        found = checks.problems(self.arm, self.data)
        self.assertTrue(any("weighted to another skeleton" in p and "jwe_head" in p for p in found), found)

    def test_a_missing_growth_key_is_named(self):
        self.own.shape_key_remove(self.own.data.shape_keys.key_blocks["Infant"])
        found = checks.problems(self.arm, self.data)
        self.assertTrue(any("no shape key 'Infant'" in p and "Voxel Remesh" in p for p in found), found)

    def test_meshes_showing_different_pictures_are_one_texture_set_short(self):
        ported(self.arm, image=bpy.data.images.new("jwe_body", 4, 4))
        found = checks.problems(self.arm, self.data)
        self.assertTrue(any("one texture set" in p for p in found), found)

    def test_meshes_without_materials_do_not_break_checks_or_images(self):
        mesh = ported(self.arm)
        mesh.data.materials.append(None)
        self.own.modifiers.clear()
        self.assertEqual(checks.problems(self.arm, self.data), [])
        self.assertEqual(checks.pictures([mesh]), {})
        checks.preview(self.arm, bpy.context.scene)

    def test_send_stops_on_a_problem_and_shows_it_in_the_panel_report(self):
        self.own.modifiers.clear()
        bpy.context.view_layer.objects.active = self.arm
        with self.assertRaises(RuntimeError) as caught:
            bpy.ops.tyrant.send()
        self.assertIn("No mesh is deformed", str(caught.exception))
        self.assertIn("No mesh is deformed", self.arm["tyrant_report"])
