import math
import os
import unittest

import bpy
from mathutils import Quaternion

from test_ik_build import game_pose, head_of, max_change, move, open_ik, set_value
from tyrant_blender import ik, ikpanel, project


@unittest.skipUnless(os.environ.get("TYRANT_TEST_IK_PROJECT"), "needs the IK fixture project")
class IkToolTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_remove_puts_the_armature_back(self):
        arm, path = open_ik(controls=False)
        before = {b.name: b.matrix_local.copy() for b in arm.data.bones}
        ik.add_controls(arm, project.load(path))

        ik.remove_controls(arm)

        self.assertEqual(set(before), {b.name for b in arm.data.bones})
        self.assertLess(max(max(abs(x - y) for ra, rb in zip(before[n], arm.data.bones[n].matrix_local) for x, y in zip(ra, rb)) for n in before), 1e-6)
        self.assertFalse(any(c.name.startswith(ik.PREFIX) for b in arm.pose.bones for c in b.constraints))
        self.assertFalse(arm.animation_data and any(ik.PREFIX in d.data_path for d in arm.animation_data.drivers))
        self.assertIsNone(arm.data.collections.get(ik.CONTROLS))
        self.assertIsNone(arm.data.collections.get(ik.MECHANISM))
        self.assertEqual(ik.built(arm), [])
        with self.assertRaisesRegex(ik.IkError, "no IK controls"):
            ik.remove_controls(arm)

    def test_the_buttons_add_and_remove(self):
        arm, _ = open_ik(controls=False)
        bpy.context.view_layer.objects.active = arm

        self.assertEqual(bpy.ops.tyrant.ik_add(), {"FINISHED"})
        self.assertIn("ctrl_foot.L", arm.data.bones)
        self.assertEqual(bpy.ops.tyrant.ik_remove(), {"FINISHED"})
        self.assertNotIn("ctrl_foot.L", arm.data.bones)

    def test_an_old_project_without_ik_data_says_how_to_get_it(self):
        arm, path = open_ik(controls=False)
        data = project.load(path)
        del data["ik"]  # written before Tyrant read IK chains
        data["tyrant"] = r"C:\no-such-folder\tyrant.exe"
        project.save(path, data)

        with self.assertRaisesRegex(ik.IkError, "Reopen this model from Tyrant to get its IK data"):
            ikpanel.ensure_ik_data(arm)

    def test_snap_puts_the_controls_on_the_fk_pose(self):
        arm, _ = open_ik()
        set_value(arm, "ctrl_foot.L", ik.IK_FK, 0.0)
        arm.pose.bones["Femur.L"].rotation_quaternion = Quaternion((1, 0, 0), math.radians(30))
        heel_fk = head_of(arm, "Heel.L").copy()

        ik.snap_controls(arm, ["Leg L"])
        set_value(arm, "ctrl_foot.L", ik.IK_FK, 1.0)

        self.assertLess((head_of(arm, "Heel.L") - heel_fk).length, 1e-3)

    def test_bake_this_frame_keys_the_ik_pose_and_switches_to_fk(self):
        arm, _ = open_ik()
        move(arm, "ctrl_foot.L", (0.0, -0.1, 0.15))
        posed = game_pose(arm)
        scene = bpy.context.scene

        frames = ik.bake(arm, scene, frame_range=False)

        self.assertEqual(frames, [scene.frame_current])
        self.assertEqual(arm.pose.bones["ctrl_foot.L"][ik.IK_FK], 0.0)
        self.assertLess(max_change(posed, game_pose(arm)), 1e-4)
        arm.pose.bones["Calve.L"].rotation_quaternion = (1, 0, 0, 0)
        scene.frame_set(scene.frame_current)  # the keys put the baked pose back
        self.assertLess(max_change(posed, game_pose(arm)), 1e-4)

    def test_bake_frame_range_follows_an_animated_control(self):
        arm, _ = open_ik()
        scene = bpy.context.scene
        scene.frame_start, scene.frame_end = 1, 3
        control = arm.pose.bones["ctrl_foot.L"]
        scene.frame_set(1)
        control.location = (0.0, 0.0, 0.0)
        control.keyframe_insert("location", frame=1)
        control.location = (0.0, 0.2, 0.1)
        control.keyframe_insert("location", frame=3)
        visual = {}
        for frame in (1, 2, 3):
            scene.frame_set(frame)
            visual[frame] = game_pose(arm)

        ik.bake(arm, scene, frame_range=True)

        for frame in (1, 2, 3):
            scene.frame_set(frame)
            self.assertLess(max_change(visual[frame], game_pose(arm)), 1e-3, frame)

    def test_bake_with_no_chain_on_ik_says_so(self):
        arm, _ = open_ik()
        for chain in ik.built(arm):
            set_value(arm, chain["target"], ik.IK_FK, 0.0)
        with self.assertRaisesRegex(ik.IkError, "No chain is on IK"):
            ik.bake(arm, bpy.context.scene, frame_range=False)

    def test_reset_pose_clears_the_pose_and_keeps_growth(self):
        arm, path = open_ik()
        arm.tyrant_growth = 0.5
        hip = arm.pose.bones["Hip"].location.copy()  # Growth's own channel
        arm.pose.bones["Calve.L"].rotation_quaternion = Quaternion((1, 0, 0), math.radians(20))
        move(arm, "ctrl_foot.L", (0.0, 0.1, 0.1))

        ik.reset_pose(arm, project.load(path))

        self.assertLess(arm.pose.bones["Calve.L"].rotation_quaternion.rotation_difference(Quaternion()).angle, 1e-6)
        self.assertLess(arm.pose.bones["ctrl_foot.L"].location.length, 1e-6)
        self.assertLess((arm.pose.bones["Hip"].location - hip).length, 1e-6)
        self.assertAlmostEqual(arm.tyrant_growth, 0.5)
