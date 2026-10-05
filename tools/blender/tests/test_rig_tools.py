import json
import os
import unittest

import bpy
from mathutils import Quaternion, Vector

from test_ik_build import fresh_ik_project, open_ik
from tyrant_blender import growth, importer, project, rig, rigpanel, send


def head(arm, name):
    bpy.context.view_layer.update()
    return arm.matrix_world @ arm.pose.bones[name].head


@unittest.skipUnless(os.environ.get("TYRANT_TEST_IK_PROJECT"), "needs the IK fixture project")
class RigToolsTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_send_always_states_the_rig_and_can_send_it_alone(self):
        data = {"tyrant": "C:/t/tyrant.exe", "workspace": "D:/ws"}
        args = send.command(data, "p.json", "s.glb", None, None, rig_file="r.json")
        self.assertEqual(args[args.index("--rig") + 1], "r.json")
        self.assertNotIn("--rig-only", args)
        self.assertIn("--rig-only", send.command(data, "p.json", "s.glb", None, None, rig_file="r.json", rig_only=True))

    def test_the_operators_start_apply_and_clear_a_rig_edit(self):
        arm, _ = open_ik(controls=False)
        bpy.context.view_layer.objects.active = arm

        self.assertEqual(bpy.ops.tyrant.rig_start(), {"FINISHED"})
        arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))
        self.assertEqual(bpy.ops.tyrant.rig_apply(), {"FINISHED"})
        self.assertIn("Calve.L", rig.offsets(arm))
        self.assertEqual(bpy.ops.tyrant.rig_clear(), {"FINISHED"})
        self.assertEqual(rig.offsets(arm), {})

    def test_growth_on_an_edited_rest_moves_the_bone_the_way_the_game_does(self):
        arm, path = open_ik(controls=False)
        data = project.load(path)
        hip = next(b for b in data["growth"]["bones"] if b["name"] == "Hip")
        rig.start(arm, data)
        arm.pose.bones["Hip"].rotation_mode = "QUATERNION"
        arm.pose.bones["Hip"].rotation_quaternion = Quaternion((0, 0, 1), 1.2)
        rig.apply(arm, data)
        _, turn, _ = rig.offsets(arm)["Hip"]
        adult = head(arm, "Hip")

        arm.tyrant_growth = 0.0

        # The game moves Hip in its parent's space by (baby − adult), and the rig edit turns that move: rotate × move.
        move = Vector(hip["baby"][0:3]) - Vector(hip["adult"][0:3])
        parent = arm.matrix_world @ arm.pose.bones["Hip"].parent.matrix
        expected = parent.to_3x3().normalized() @ (turn @ move)
        self.assertLess(((head(arm, "Hip") - adult) - expected).length, 1e-3)

    def test_the_panel_names_edited_bones_the_game_moves(self):
        arm, path = open_ik(controls=False)
        data = project.load(path)
        data["rigInfo"] = {"clipMoved": ["Femur.L"], "growthMoved": ["Hip"], "growthScaled": [], "growthSupported": False}
        rig._store(arm, {"Femur.L": (Vector((0, 0.1, 0)), Quaternion(), Vector((1, 1, 1))),
                         "Hip": (Vector((0, 0.1, 0)), Quaternion(), Vector((1, 1, 1)))})

        lines = rigpanel.warnings(arm, data)

        self.assertTrue(any("Femur.L" in line and "animations" in line for line in lines))
        self.assertTrue(any("Hip" in line and "growth" in line and "refuses" in line for line in lines))

    def test_a_model_opened_with_a_rig_on_the_games_mesh_gets_it_applied(self):
        path = fresh_ik_project()
        data = project.load(path)
        data["rig"] = {"Calve.L": {"move": [0, -0.1, 0]}}
        data["rigBaked"] = False
        project.save(path, data)

        arm = importer.import_project(path)

        self.assertIn("Calve.L", rig.offsets(arm))
        before = arm.matrix_world @ arm.data.bones["Foot.L"].head_local
        rig.clear(arm, data)  # Clear works from an adopted rig too
        self.assertGreater((arm.matrix_world @ arm.data.bones["Foot.L"].head_local - before).length, 0.05)
        self.assertEqual(rig.offsets(arm), {})

    def test_a_model_made_for_its_rig_keeps_its_rest_and_knows_the_games(self):
        path = fresh_ik_project()
        data = project.load(path)
        data["rig"] = {"Calve.L": {"move": [0, -0.1, 0]}}
        data["rigBaked"] = True
        project.save(path, data)

        arm = importer.import_project(path)
        rest = arm.matrix_world @ arm.data.bones["Foot.L"].head_local

        self.assertIn("Calve.L", rig.offsets(arm))
        self.assertLess((arm.matrix_world @ arm.data.bones["Foot.L"].head_local - rest).length, 1e-6)
        self.assertTrue(arm.get(rig.GAME))

    def test_the_rig_box_draws_while_editing_and_after(self):
        arm, path = open_ik(controls=False)
        data = project.load(path)
        drawn = []

        class Layout:
            def __getattr__(self, name):
                def call(*args, **kwargs):
                    drawn.append((name, args, kwargs))
                    return self
                return call

        def say(layout, context, text, icon="NONE"):
            drawn.append(("say", (text,), {}))

        rigpanel.draw(Layout(), bpy.context, arm, data, say)
        self.assertIn(("operator", ("tyrant.rig_start",), {"icon": "POSE_HLT"}), drawn)
        rig.start(arm, data)
        drawn.clear()
        rigpanel.draw(Layout(), bpy.context, arm, data, say)
        self.assertTrue(any(d[0] == "operator" and d[1] == ("tyrant.rig_apply",) for d in drawn))
        rig.cancel(arm, data)

    def test_a_top_bones_rig_survives_reopening_and_sending_again(self):
        """Bones with no parent bone convert between Blender's Z-up armature space and the game's Y-up space both ways."""
        path = fresh_ik_project()
        data = project.load(path)
        data["rig"] = {"MainBone": {"move": [0, 0.1, 0]}}
        data["rigBaked"] = False
        project.save(path, data)

        arm = importer.import_project(path)

        move = rig.unity_rig(arm)["MainBone"]["move"]
        self.assertLess(max(abs(a - b) for a, b in zip(move, [0, 0.1, 0])), 1e-5)
        before = arm.matrix_world @ arm.data.bones["MainBone"].head_local
        rig.clear(arm, data)
        lifted = before - arm.matrix_world @ arm.data.bones["MainBone"].head_local
        self.assertAlmostEqual(lifted.z, 0.1, places=4)  # Unity's up (y) is Blender's up (z)

    def test_send_is_refused_when_the_models_rig_edit_could_not_be_shown(self):
        from tyrant_blender import panel

        arm, _ = open_ik(controls=False)
        arm[rig.PROBLEM] = "The rig edit could not be shown (RuntimeError: x)."

        with self.assertRaises(send.SendError) as raised:
            panel._start_send(bpy.context, arm)

        self.assertIn("rig edit", str(raised.exception))
        self.assertIn("Start fresh", str(raised.exception))
