import math
import os
import shutil
import tempfile
import unittest

import bpy
from mathutils import Quaternion, Vector

from tyrant_blender import ik, importer, project


def fresh_ik_project():
    """A copy of the C#-written IK fixture project (IkFixture: two legs and a neck with the game's chains)."""
    src = os.environ["TYRANT_TEST_IK_PROJECT"]
    dst = tempfile.mkdtemp(prefix="tyrant-ik-")
    shutil.copytree(os.path.dirname(src), dst, dirs_exist_ok=True)
    return os.path.join(dst, os.path.basename(src))


def open_ik(controls=True):
    path = fresh_ik_project()
    if not controls:
        data = project.load(path)
        data["ikOnOpen"] = False
        project.save(path, data)
    return importer.import_project(path), path


def head_of(arm, name):
    bpy.context.view_layer.update()
    return arm.matrix_world @ arm.pose.bones[name].head


def game_pose(arm):
    """Every game bone's pose matrix (Tyrant's own bones left out)."""
    bpy.context.view_layer.update()
    ours = {n for c in ik.built(arm) for n in c["bones"]}
    return {b.name: b.matrix.copy() for b in arm.pose.bones if b.name not in ours}


def max_change(before, after):
    return max(max(abs(x - y) for ra, rb in zip(before[n], after[n]) for x, y in zip(ra, rb)) for n in before)


def move(arm, bone, by):
    bpy.context.view_layer.update()
    pose_bone = arm.pose.bones[bone]
    m = pose_bone.matrix.copy()
    m.translation = m.translation + Vector(by)
    pose_bone.matrix = m
    bpy.context.view_layer.update()


def set_value(arm, bone, key, value):
    arm.pose.bones[bone][key] = value
    ik.refresh(arm)


@unittest.skipUnless(os.environ.get("TYRANT_TEST_IK_PROJECT"), "needs the IK fixture project")
class IkBuildTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.context.preferences.filepaths.use_scripts_auto_execute = False  # the drivers must work without Auto Run

    def test_open_in_blender_builds_the_controls_unless_switched_off(self):
        arm, _ = open_ik()
        names = {b.name for b in arm.data.bones}
        for name in ("ctrl_foot.L", "ctrl_knee.L", "ctrl_foot.R", "ctrl_knee.R", "ctrl_head", "ctrl_look",
                     "mch_grow_foot.L", "mch_tip_foot.L", "mch_end_foot.L", "mch_tip_head", "mch_end_head"):
            self.assertIn(name, names)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        arm, _ = open_ik(controls=False)
        self.assertNotIn("ctrl_foot.L", {b.name for b in arm.data.bones})
        self.assertEqual(ik.built(arm), [])

    def test_adding_the_controls_moves_no_bone(self):
        arm, path = open_ik(controls=False)
        before = game_pose(arm)

        ik.add_controls(arm, project.load(path))

        self.assertLess(max_change(before, game_pose(arm)), 1e-4)

    def test_controls_do_not_deform_and_live_in_their_collections(self):
        arm, _ = open_ik()
        ours = [n for c in ik.built(arm) for n in c["bones"]]
        self.assertTrue(all(not arm.data.bones[n].use_deform for n in ours))
        self.assertTrue(arm.data.collections[ik.CONTROLS].is_visible)
        self.assertFalse(arm.data.collections[ik.MECHANISM].is_visible)
        self.assertIn(arm.data.bones["ctrl_foot.L"], list(arm.data.collections[ik.CONTROLS].bones))
        self.assertIn(arm.data.bones["mch_end_foot.L"], list(arm.data.collections[ik.MECHANISM].bones))
        shape = arm.pose.bones["ctrl_foot.L"].custom_shape
        self.assertIsNotNone(shape)
        self.assertEqual(len(shape.users_scene), 0)  # never in a scene: Send and the goes/stays list never see it

    def test_the_foot_control_sits_on_the_games_ground_contact_point(self):
        arm, _ = open_ik()
        # Heel.L in Unity (0.3, 0, -0.05) + end offset (0, -0.05, 0) → glTF (-0.3, -0.05, -0.05) → Blender (x, -z, y).
        self.assertLess((head_of(arm, "ctrl_foot.L") - Vector((-0.3, 0.05, -0.05))).length, 1e-4)

    def test_a_foot_control_moves_the_heel_onto_it(self):
        arm, _ = open_ik()
        heel_before = head_of(arm, "Heel.L").copy()

        move(arm, "ctrl_foot.L", (0.0, -0.1, 0.15))

        self.assertGreater((head_of(arm, "Heel.L") - heel_before).length, 0.1)
        self.assertLess((head_of(arm, "Heel.L") - head_of(arm, "mch_tip_foot.L")).length, 1e-3)

    def test_the_knees_bend_toward_their_poles_in_front(self):
        arm, _ = open_ik()
        for side in ("L", "R"):  # L from the game's force, R from the leg's own bend: both point forward (-Y)
            self.assertLess(head_of(arm, f"ctrl_knee.{side}").y, head_of(arm, f"Calve.{side}").y - 0.1)
        knee_before = head_of(arm, "Calve.L").copy()

        move(arm, "ctrl_foot.L", (0.0, 0.0, 0.3))

        self.assertLess(head_of(arm, "Calve.L").y, knee_before.y - 0.02)

    def test_the_head_control_moves_the_neck_and_aim_turns_the_head(self):
        arm, _ = open_ik()
        move(arm, "ctrl_head", (0.0, 0.1, -0.1))  # toward the body: the fixture's neck is almost straight at rest
        self.assertLess((head_of(arm, "Head") - head_of(arm, "mch_tip_head")).length, 1e-3)

        set_value(arm, "ctrl_head", ik.AIM, 1.0)
        move(arm, "ctrl_look", (0.6, 0.0, 0.0))

        track = next(c for c in arm.pose.bones["Head"].constraints if c.type == "DAMPED_TRACK")
        m = (arm.matrix_world @ arm.pose.bones["Head"].matrix).to_3x3()
        axis = {"TRACK_X": m.col[0], "TRACK_NEGATIVE_X": -m.col[0], "TRACK_Y": m.col[1], "TRACK_NEGATIVE_Y": -m.col[1],
                "TRACK_Z": m.col[2], "TRACK_NEGATIVE_Z": -m.col[2]}[track.track_axis]
        to_look = head_of(arm, "ctrl_look") - head_of(arm, "Head")
        self.assertLess(math.degrees(axis.angle(to_look)), 2.0)

    def test_ik_fk_switches_with_auto_run_off(self):
        arm, _ = open_ik()
        set_value(arm, "ctrl_foot.L", ik.IK_FK, 0.0)
        heel = head_of(arm, "Heel.L").copy()

        move(arm, "ctrl_foot.L", (0.0, -0.1, 0.15))
        self.assertLess((head_of(arm, "Heel.L") - heel).length, 1e-5)  # FK: the control does nothing

        set_value(arm, "ctrl_foot.L", ik.IK_FK, 1.0)
        self.assertLess((head_of(arm, "Heel.L") - head_of(arm, "mch_tip_foot.L")).length, 1e-3)

    def test_a_missing_chain_bone_skips_only_that_chain(self):
        arm, path = open_ik(controls=False)
        data = project.load(path)
        data["ik"]["chains"][1]["joints"][1]["name"] = "Shin.R"  # as on a renamed or ported rig

        reasons = ik.add_controls(arm, data)

        self.assertEqual(reasons, ["Leg R skipped: no bone 'Shin.R'"])
        self.assertEqual(ik.skipped(arm), reasons)
        names = {b.name for b in arm.data.bones}
        self.assertIn("ctrl_foot.L", names)
        self.assertIn("ctrl_head", names)
        self.assertNotIn("ctrl_foot.R", names)

    def test_a_tip_on_its_joint_uses_the_joint_before(self):
        """Stegosaurus' hands: the game's last joint sits on the one before it, the end offset gives the reach."""
        arm, path = open_ik(controls=False)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode="EDIT")
        heel = arm.data.edit_bones["Heel.R"]
        tip = arm.data.edit_bones.new("Tip.R")
        tip.head, tip.tail, tip.parent = heel.head.copy(), heel.head + Vector((0.0, 0.0, 0.1)), heel
        bpy.ops.object.mode_set(mode="OBJECT")
        data = project.load(path)
        data["ik"]["chains"][1]["joints"].append({"name": "Tip.R", "length": 0.0})
        before = game_pose(arm)

        reasons = ik.add_controls(arm, data)

        self.assertEqual(reasons, [])
        leg = next(c for c in ik.built(arm) if c["name"] == "Leg R")
        self.assertEqual(leg["joints"][-1], "Heel.R")
        self.assertLess(max_change(before, game_pose(arm)), 1e-4)
        move(arm, "ctrl_foot.R", (0.0, -0.1, 0.15))
        self.assertLess((head_of(arm, "Heel.R") - head_of(arm, "mch_tip_foot.R")).length, 1e-3)

    def test_the_fixture_opens_posed_like_a_game_rig(self):
        arm, _ = open_ik(controls=False)
        self.assertGreater(arm.pose.bones["Femur.L"].matrix_basis.to_quaternion().angle, 0.2)

    def test_the_prefabs_tiny_scales_are_cleared_on_open(self):
        arm, _ = open_ik(controls=False)
        self.assertLess((arm.pose.bones["Neck"].scale - Vector((1.0, 1.0, 1.0))).length, 1e-6)

    def test_adding_twice_or_without_chains_is_refused(self):
        arm, path = open_ik()
        with self.assertRaisesRegex(ik.IkError, "Already has IK controls"):
            ik.add_controls(arm, project.load(path))
        bpy.ops.wm.read_factory_settings(use_empty=True)
        arm, path = open_ik(controls=False)
        data = project.load(path)
        data["ik"] = None
        with self.assertRaisesRegex(ik.IkError, "no IK chains"):
            ik.add_controls(arm, data)

    def test_adding_at_baby_growth_with_a_pose_moves_nothing(self):
        arm, path = open_ik(controls=False)
        arm.tyrant_growth = 0.3
        arm.pose.bones["Femur.L"].rotation_quaternion = Quaternion((1, 0, 0), math.radians(25))
        arm.pose.bones["Neck"].rotation_quaternion = Quaternion((0, 0, 1), math.radians(15))
        before = game_pose(arm)

        ik.add_controls(arm, project.load(path))

        self.assertLess(max_change(before, game_pose(arm)), 1e-3)
        self.assertAlmostEqual(arm.tyrant_growth, 0.3)

    def test_growth_keeps_rotations(self):
        arm, _ = open_ik()
        set_value(arm, "ctrl_foot.L", ik.IK_FK, 0.0)
        turn = Quaternion((1, 0, 0), math.radians(20))
        arm.pose.bones["Calve.L"].rotation_quaternion = turn

        arm.tyrant_growth = 0.0

        self.assertLess(arm.pose.bones["Calve.L"].rotation_quaternion.rotation_difference(turn).angle, 1e-6)

    def test_controls_follow_growth_to_the_chain_end(self):
        arm, _ = open_ik()
        heel_adult = head_of(arm, "Heel.L").copy()

        arm.tyrant_growth = 0.0  # SkinDumps: Hip baby 0.2 lower

        self.assertLess(abs(head_of(arm, "Heel.L").z - (heel_adult.z - 0.2)), 1e-3)
        self.assertLess((head_of(arm, "Heel.L") - head_of(arm, "mch_tip_foot.L")).length, 1e-3)
