import json
import os
import sys
import tempfile
import unittest

import bpy

from test_import import fresh_project
from tyrant_blender import importer, project, send


class SendTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_only_the_armature_and_its_deformed_meshes_go(self):
        arm = importer.import_project(fresh_project())
        bpy.ops.mesh.primitive_cube_add()  # a stray mesh (reference, physics box…)
        stray = bpy.context.active_object
        other = bpy.data.objects.new("OtherRig", bpy.data.armatures.new("OtherRig"))
        bpy.context.scene.collection.objects.link(other)
        bpy.ops.mesh.primitive_uv_sphere_add()
        ported = bpy.context.active_object
        ported.modifiers.new("Armature", "ARMATURE").object = arm

        names = {o.name for o in send.sendable(arm)}

        self.assertIn(arm.name, names)
        self.assertIn(ported.name, names)
        self.assertNotIn(stray.name, names)
        self.assertNotIn(other.name, names)

    def test_export_writes_a_glb_with_only_those_objects_at_rest(self):
        path = fresh_project()
        arm = importer.import_project(path)
        bpy.ops.mesh.primitive_cube_add()
        arm.tyrant_growth = 0.0
        glb = os.path.join(os.path.dirname(path), "send.glb")
        send.export(arm, glb)
        self.assertTrue(os.path.getsize(glb) > 0)
        self.assertAlmostEqual(arm.tyrant_growth, 1.0)  # Send puts the animal back to adult
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=glb, disable_bone_shape=True)  # else Blender adds its Icosphere bone shape
        self.assertEqual(len([o for o in bpy.data.objects if o.type == "MESH"]), 1)

    def test_hidden_armature_and_meshes_still_go_and_stay_hidden(self):
        path = fresh_project()
        arm = importer.import_project(path)
        mesh = next(o for o in send.sendable(arm) if o.type == "MESH")
        arm.hide_set(True)
        mesh.hide_viewport = True
        glb = os.path.join(os.path.dirname(path), "send.glb")
        send.export(arm, glb)
        self.assertTrue(arm.hide_get())
        self.assertTrue(mesh.hide_viewport)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=glb, disable_bone_shape=True)
        self.assertEqual(len([o for o in bpy.data.objects if o.type == "ARMATURE"]), 1)
        self.assertEqual(len([o for o in bpy.data.objects if o.type == "MESH"]), 1)

    def test_a_ported_mesh_still_parented_to_its_old_rig_is_sent_skinned(self):
        """A mesh brought in from another file keeps its old parent; only its Armature modifier points at the Tyrant rig."""
        import json, struct

        path = fresh_project()
        arm = importer.import_project(path)
        mesh = next(o for o in send.sendable(arm) if o.type == "MESH")
        old_rig = bpy.data.objects.new("OldRig", bpy.data.armatures.new("OldRig"))
        bpy.context.scene.collection.objects.link(old_rig)
        mesh.parent = old_rig  # as imported from the other game's file
        glb = os.path.join(os.path.dirname(path), "send.glb")

        send.export(arm, glb)

        data = open(glb, "rb").read()
        gltf = json.loads(data[20:20 + struct.unpack_from("<I", data, 12)[0]])
        mesh_nodes = [n for n in gltf["nodes"] if "mesh" in n]
        self.assertEqual(len(mesh_nodes), 1)
        self.assertIn("skin", mesh_nodes[0])  # linked to the Tyrant rig's skin, so Tyrant can fit it
        self.assertEqual(mesh.parent, old_rig)  # the scene is left as it was

    def test_other_modifiers_are_warned_about(self):
        arm = importer.import_project(fresh_project())
        mesh = next(o for o in send.sendable(arm) if o.type == "MESH")
        mesh.modifiers.new("Subdivision", "SUBSURF")
        warnings = send.modifier_warnings([mesh])
        self.assertEqual(len(warnings), 1)
        self.assertIn("SUBSURF", warnings[0])

    def test_two_armatures_need_a_selection(self):
        importer.import_project(fresh_project())
        importer.import_project(fresh_project())
        bpy.context.view_layer.objects.active = None
        self.assertIsNone(project.armature_of(bpy.context))

    def test_command_line_passes_the_destination(self):
        data = {"tyrant": "C:/T/tyrant.exe", "workspace": "D:/ws"}
        args = send.command(data, "D:/ws/blender/p/tyrant-blender.json", "D:/ws/blender/p/send.glb", {"mod": "m", "skin": "s"}, None)
        self.assertEqual(args, ["C:/T/tyrant.exe", "blender", "send", "-w", "D:/ws", "D:/ws/blender/p/tyrant-blender.json",
                                "D:/ws/blender/p/send.glb", "--mod", "m", "--skin", "s"])
        new = send.command(data, "p.json", "s.glb", {"mod": "n", "skin": None}, "New")
        self.assertEqual(new[-4:], ["--mod", "n", "--new-mod-name", "New"])
        self.assertEqual(send.command(data, "p.json", "s.glb", None, None)[-2:], ["p.json", "s.glb"])

    def test_running_tyrant_reads_its_json_line(self):
        fake = os.path.join(tempfile.mkdtemp(), "fake_tyrant.py")
        with open(fake, "w", encoding="utf-8") as f:
            f.write('import json\nprint("warming up")\nprint(json.dumps({"ok": True, "errors": [], "warnings": ["w"], "lodVertices": [3]}))\n')
        result = send.run([sys.executable, fake])
        self.assertTrue(result["ok"])
        self.assertEqual(result["warnings"], ["w"])

    def test_a_missing_tyrant_is_a_clear_error(self):
        result = send.run([os.path.join(tempfile.mkdtemp(), "nope", "tyrant.exe"), "blender", "send"])
        self.assertFalse(result["ok"])
        self.assertIn("open the project again from Tyrant", result["errors"][0])


class SendOperatorTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_the_panel_and_send_are_registered(self):
        self.assertTrue(hasattr(bpy.types, "VIEW3D_PT_tyrant"))
        self.assertTrue(hasattr(bpy.ops.tyrant, "send"))

    def test_send_refuses_a_scene_not_opened_from_tyrant(self):
        bpy.ops.mesh.primitive_cube_add()
        with self.assertRaises(RuntimeError) as caught:  # an operator's ERROR report reaches Python as RuntimeError
            bpy.ops.tyrant.send()
        self.assertIn("not opened from Tyrant", str(caught.exception))
        self.assertIn("not opened from Tyrant", send.why_not_sendable(bpy.context))

    def test_two_tyrant_rigs_ask_for_a_selection(self):
        importer.import_project(fresh_project())
        importer.import_project(fresh_project())
        bpy.context.view_layer.objects.active = None
        self.assertIn("Select the model or armature to send", send.why_not_sendable(bpy.context))
