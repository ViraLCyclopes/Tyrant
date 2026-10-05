import os
import shutil
import tempfile
import unittest

import bpy

from tyrant_blender import materials, project, send, ui


def fresh_object():
    """A copy of the C#-written object project (a fence post: no armature)."""
    src = os.environ["TYRANT_TEST_OBJECT_PROJECT"]
    dst = tempfile.mkdtemp(prefix="tyrant-object-")
    shutil.copytree(os.path.dirname(src), dst, dirs_exist_ok=True)
    return os.path.join(dst, os.path.basename(src))


class ObjectTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_a_game_object_opens_as_its_own_scene_with_its_picture(self):
        path = fresh_object()
        scene = ui.show_project(path)

        self.assertTrue(scene.name.startswith("Tyrant · object · Fence_Post"))
        meshes = [o for o in scene.objects if o.type == "MESH"]
        self.assertEqual(len(meshes), 1)
        self.assertEqual(meshes[0][project.TAG], path)
        self.assertFalse(any(o.type == "ARMATURE" for o in scene.objects))
        material = bpy.data.materials["Adobe"]
        self.assertNotIn(materials.GROUP, material.node_tree.nodes)  # scenery: plain Principled with the picture
        self.assertTrue(material.node_tree.nodes["diffuse"].image)

    def test_send_says_objects_are_for_reference(self):
        scene = ui.show_project(fresh_object())
        bpy.context.view_layer.objects.active = None
        with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
            self.assertIn("game object", send.why_not_sendable(bpy.context))
