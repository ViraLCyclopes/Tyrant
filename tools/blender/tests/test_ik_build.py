import math
import os
import shutil
import tempfile
import unittest

import bpy
from mathutils import Matrix, Quaternion, Vector

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


def chain_ends(arm):
    """Where each chain's last joint stands (world)."""
    return {c["joints"][-1]: head_of(arm, c["joints"][-1]).copy() for c in ik.built(arm)}


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

    def test_adding_the_controls_keeps_the_stance(self):
        """The model stands in the prefab's stance: the other bones keep it, the chain ends stay where they were (the
        chains now hold it through their controls)."""
        arm, path = open_ik(controls=False)
        before = game_pose(arm)
        heads = {n: head_of(arm, n).copy() for n in ("Heel.L", "Heel.R", "Head")}

        ik.add_controls(arm, project.load(path))

        joints = {j for c in ik.built(arm) for j in c["joints"]}
        others = {n: m for n, m in before.items() if n not in joints and not any(arm.data.bones[j] in arm.data.bones[n].parent_recursive for j in joints)}
        self.assertLess(max_change(others, game_pose(arm)), 1e-4)
        for name, at in heads.items():
            self.assertLess((head_of(arm, name) - at).length, 1e-3, name)

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
        arm, path = open_ik()
        ik.reset_pose(arm)  # at rest, where the controls were built
        # The game's end offset (0, -0.05, 0) under the heel, in the heel's frame as it rests (the bind pose; C# pins the
        # Unity → glTF conversion of the offset).
        offset = next(c for c in project.load(path)["ik"]["chains"] if c["name"] == "Leg L")["endOffset"]
        expected = arm.matrix_world @ (arm.data.bones["Heel.L"].matrix_local @ Vector(offset))
        self.assertEqual(offset, [0.0, -0.05, 0.0])
        self.assertLess((head_of(arm, "ctrl_foot.L") - expected).length, 1e-4)

    def test_a_foot_control_moves_the_heel_onto_it(self):
        arm, _ = open_ik()
        heel_before = head_of(arm, "Heel.L").copy()

        move(arm, "ctrl_foot.L", (0.0, -0.1, 0.15))

        self.assertGreater((head_of(arm, "Heel.L") - heel_before).length, 0.1)
        self.assertLess((head_of(arm, "Heel.L") - head_of(arm, "mch_tip_foot.L")).length, 1e-3)

    def test_the_poles_sit_close_in_front_of_the_knees(self):
        arm, _ = open_ik(controls=False)
        ik.reset_pose(arm)
        ik.add_controls(arm, project.load(arm[project.TAG]))
        for side in ("L", "R"):
            leg = [head_of(arm, f"{n}.{side}") for n in ("Femur", "Calve", "Foot", "Heel")]
            length = sum((a - b).length for a, b in zip(leg, leg[1:]))
            self.assertAlmostEqual((head_of(arm, f"ctrl_knee.{side}") - head_of(arm, f"Calve.{side}")).length, 0.4 * length, places=3)

    def test_the_knees_bend_toward_their_poles_in_front(self):
        arm, _ = open_ik(controls=False)
        ik.reset_pose(arm)
        ik.add_controls(arm, project.load(arm[project.TAG]))
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
        heel = head_of(arm, "Heel.R").copy()

        reasons = ik.add_controls(arm, data)

        self.assertEqual(reasons, [])
        leg = next(c for c in ik.built(arm) if c["name"] == "Leg R")
        self.assertEqual(leg["joints"][-1], "Heel.R")
        self.assertLess((head_of(arm, "Heel.R") - heel).length, 1e-3)
        move(arm, "ctrl_foot.R", (0.0, -0.1, 0.15))
        self.assertLess((head_of(arm, "Heel.R") - head_of(arm, "mch_tip_foot.R")).length, 1e-3)

    def test_a_model_opens_in_the_prefabs_stance_with_its_chains_held_by_the_controls(self):
        """The fixture's prefab pose differs from its bind pose, as on game rigs. Without IK the bones show it as they are;
        with IK the chain joints stay at rest and their controls hold the same stance."""
        arm, _ = open_ik(controls=False)
        self.assertGreater(arm.pose.bones["Hip"].matrix_basis.to_quaternion().angle, 0.1)
        self.assertGreater(arm.pose.bones["Femur.L"].matrix_basis.to_quaternion().angle, 0.1)
        heels = {n: head_of(arm, n).copy() for n in ("Heel.L", "Heel.R")}
        bpy.ops.wm.read_factory_settings(use_empty=True)

        arm, _ = open_ik()

        self.assertGreater(arm.pose.bones["Hip"].matrix_basis.to_quaternion().angle, 0.1)
        self.assertLess(arm.pose.bones["Femur.L"].matrix_basis.to_quaternion().angle, 1e-6)
        for name, at in heels.items():
            self.assertLess((head_of(arm, name) - at).length, 1e-3, name)

    def test_clearing_every_transform_keeps_the_ik_lined_up(self):
        arm, _ = open_ik()
        move(arm, "ctrl_foot.L", (0.0, -0.1, 0.15))
        arm.pose.bones["Femur.R"].rotation_quaternion = Quaternion((1, 0, 0), math.radians(20))
        ours = {n for c in ik.built(arm) for n in c["bones"] if n.startswith("mch_")}
        for bone in arm.pose.bones:  # Alt+G, Alt+R, Alt+S on every visible bone
            if bone.name not in ours:
                bone.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()

        for bone in arm.pose.bones:
            if bone.name in {j for c in ik.built(arm) for j in c["joints"]}:
                self.assertLess(max(abs(a - b) for ra, rb in zip(bone.matrix, bone.bone.matrix_local) for a, b in zip(ra, rb)), 1e-4, bone.name)

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

    def test_adding_at_baby_growth_with_a_pose_keeps_the_chain_ends(self):
        arm, path = open_ik(controls=False)
        arm.tyrant_growth = 0.3
        arm.pose.bones["Femur.L"].rotation_quaternion = Quaternion((1, 0, 0), math.radians(25))
        arm.pose.bones["Neck"].rotation_quaternion = Quaternion((0, 0, 1), math.radians(15))
        heads = {n: head_of(arm, n).copy() for n in ("Heel.L", "Heel.R", "Head")}

        ik.add_controls(arm, project.load(path))

        for name, at in heads.items():
            self.assertLess((head_of(arm, name) - at).length, 1e-3, name)
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

        arm.tyrant_growth = 0.0  # SkinDumps: Hip baby 0.2 lower, in its parent's space (the armature's up)

        self.assertLess((head_of(arm, "Heel.L") - (heel_adult + Vector((0.0, 0.0, -0.2)))).length, 1e-3)
        self.assertLess((head_of(arm, "Heel.L") - head_of(arm, "mch_tip_foot.L")).length, 1e-3)

    def test_an_unexpected_ik_failure_still_opens_the_model_and_says_why(self):
        def broken(_arm, _data):
            raise RuntimeError("something unexpected")

        real = ik.add_controls
        ik.add_controls = broken
        try:
            arm, _ = open_ik()
        finally:
            ik.add_controls = real
        self.assertEqual(arm.type, "ARMATURE")
        self.assertTrue(any("something unexpected" in line for line in ik.skipped(arm)), ik.skipped(arm))

    def test_controls_follow_growth_that_scales_a_bone_inside_the_chain(self):
        """Growth changes the rig: here it shrinks the left femur (Stegosaurus' growth scales its femurs, calves, arms)."""
        def scaled_femur(path):
            data = project.load(path)
            data["growth"]["bones"].append({"name": "Femur.L", "mode": "Scale", "translation": False, "scale": True,
                                            "baby": [0, 0, 0, 0.8, 0.8, 0.8], "adolescent": [0, 0, 0, 0.9, 0.9, 0.9], "adult": [0, 0, 0, 1, 1, 1]})
            data["ikOnOpen"] = False
            project.save(path, data)
            return importer.import_project(path)

        arm = scaled_femur(fresh_ik_project())
        arm.tyrant_growth = 0.0
        heel_without_ik = head_of(arm, "Heel.L").copy()
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path = fresh_ik_project()
        arm = scaled_femur(path)
        ik.add_controls(arm, project.load(path))

        arm.tyrant_growth = 0.0

        self.assertLess((head_of(arm, "Heel.L") - heel_without_ik).length, 1e-3)

    def test_a_chain_the_game_starts_switched_off_still_works(self):
        """Tyrannosaurus' legs have influence 0 in its prefab (the game fades foot IK in at runtime): the IK/FK value decides."""
        path = fresh_ik_project()
        data = project.load(path)
        for chain in data["ik"]["chains"]:
            chain["influence"] = 0.0
        project.save(path, data)
        arm = importer.import_project(path)

        move(arm, "ctrl_foot.L", (0.0, -0.1, 0.15))

        self.assertLess((head_of(arm, "Heel.L") - head_of(arm, "mch_tip_foot.L")).length, 1e-3)
