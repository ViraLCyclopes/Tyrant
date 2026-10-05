import json
import os
import struct
import tempfile
import unittest

import bpy
from mathutils import Matrix, Quaternion, Vector

from test_ik_build import open_ik
from tyrant_blender import ik, rig, send


def head(arm, name):
    bpy.context.view_layer.update()
    return arm.matrix_world @ arm.pose.bones[name].head


def mesh_of(arm):
    return next(o for o in bpy.context.scene.objects if o.type == "MESH" and any(m.type == "ARMATURE" and m.object == arm for m in o.modifiers))


def weight_to(mesh, vertex, bone):
    """The fixture's mesh hangs on MainBone: one vertex is given to another bone, to see the edit carry it."""
    for group in mesh.vertex_groups:
        group.remove([vertex])
    group = mesh.vertex_groups.get(bone) or mesh.vertex_groups.new(name=bone)
    group.add([vertex], 1.0, "REPLACE")


def rest_head(arm, name):
    return arm.matrix_world @ arm.data.bones[name].head_local


@unittest.skipUnless(os.environ.get("TYRANT_TEST_IK_PROJECT"), "needs the IK fixture project")
class RigEditTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.arm, self.path = open_ik(controls=False)
        with open(self.path, encoding="utf-8") as f:
            self.data = json.load(f)

    def test_apply_keeps_what_is_shown_records_the_bone_and_moves_the_rest(self):
        before_rest = rest_head(self.arm, "Foot.L")
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))  # along the bone (its own frame)
        shown = head(self.arm, "Foot.L")

        rig.apply(self.arm, self.data)

        self.assertLess((head(self.arm, "Foot.L") - shown).length, 1e-4)  # nothing jumps
        self.assertEqual(set(rig.offsets(self.arm)), {"Calve.L"})
        self.assertAlmostEqual(rig.offsets(self.arm)["Calve.L"][0].length, 0.2, places=4)
        self.assertGreater((rest_head(self.arm, "Foot.L") - before_rest).length, 0.15)  # the rest is the edited skeleton
        self.assertFalse(rig.editing(self.arm))

    def test_the_mesh_follows_only_the_edit(self):
        mesh = mesh_of(self.arm)
        weight_to(mesh, 0, "Calve.L")
        co_before = mesh.data.vertices[0].co.copy()
        calve_before = rest_head(self.arm, "Calve.L")
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))

        rig.apply(self.arm, self.data)

        moved_rest = rest_head(self.arm, "Calve.L") - calve_before
        moved_vertex = mesh.matrix_world.to_3x3() @ (mesh.data.vertices[0].co - co_before)
        self.assertLess((moved_vertex - moved_rest).length, 1e-4)  # carried like its bone, once
        for key in mesh.data.shape_keys.key_blocks if mesh.data.shape_keys else []:
            self.assertLess((key.data[0].co - mesh.data.vertices[0].co).length, 0.6)  # every shape key followed

    def test_a_second_edit_adds_to_the_first_even_under_a_scaled_parent(self):
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Calve.L"].scale = Vector((2.0, 2.0, 2.0))
        rig.apply(self.arm, self.data)
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Foot.L"].location += Vector((0.0, 0.1, 0.0))

        rig.apply(self.arm, self.data)

        table = rig.offsets(self.arm)
        self.assertAlmostEqual(table["Calve.L"][2].x, 2.0, places=3)
        # 0.1 in Blender under a parent grown 2x is 0.05 in that parent's own (scaled) space, as the game counts it
        self.assertAlmostEqual(table["Foot.L"][0].length, 0.05, places=3)

    def test_an_uneven_scale_on_a_turned_bone_is_recorded_as_posed(self):
        femur = self.arm.pose.bones["Femur.L"]
        femur.rotation_mode = "QUATERNION"
        femur.rotation_quaternion = Quaternion((1, 0, 0), 0.8) @ femur.rotation_quaternion  # turned in its parent
        rig.start(self.arm, self.data)
        femur.scale = Vector((1.0, 1.5, 1.0))  # longer along the bone
        shown = head(self.arm, "Foot.L")

        rig.apply(self.arm, self.data)

        _, turn, scale = rig.offsets(self.arm)["Femur.L"]
        self.assertLess((scale - Vector((1.0, 1.5, 1.0))).length, 1e-3)
        self.assertLess(turn.angle, 1e-3)
        self.assertLess((head(self.arm, "Foot.L") - shown).length, 1e-3)  # nothing jumps

    def test_cancel_puts_the_pose_back_and_records_nothing(self):
        rig.start(self.arm, self.data)
        before = head(self.arm, "Foot.L")
        self.arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))

        rig.cancel(self.arm, self.data)

        self.assertLess((head(self.arm, "Foot.L") - before).length, 1e-4)
        self.assertEqual(rig.offsets(self.arm), {})
        self.assertFalse(rig.editing(self.arm))

    def test_clear_returns_mesh_and_rest_to_the_games(self):
        mesh = mesh_of(self.arm)
        weight_to(mesh, 0, "Calve.L")
        co = mesh.data.vertices[0].co.copy()
        rest = rest_head(self.arm, "Foot.L")
        shown = head(self.arm, "Foot.L")
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))
        self.arm.pose.bones["Calve.L"].rotation_quaternion = Quaternion((1, 0, 0), 0.4)
        rig.apply(self.arm, self.data)

        rig.clear(self.arm, self.data)

        self.assertLess((rest_head(self.arm, "Foot.L") - rest).length, 1e-4)
        self.assertLess((mesh.data.vertices[0].co - co).length, 1e-4)
        self.assertLess((head(self.arm, "Foot.L") - shown).length, 1e-4)  # the stance is as before the edit
        self.assertEqual(rig.offsets(self.arm), {})

    def test_ik_controls_are_taken_off_while_editing_and_built_again(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        arm, path = open_ik(controls=True)
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        rig.start(arm, data)
        self.assertFalse(ik.built(arm))
        arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))

        rig.apply(arm, data)

        self.assertTrue(ik.built(arm))

    def test_unity_conversion_mirrors_x(self):
        m, q, s = Vector((0.1, 0.2, 0.3)), Quaternion((0.9, 0.1, 0.2, 0.3)).normalized(), Vector((1, 2, 3))
        u = rig.to_unity({"Jaw": (m, q, s)})["Jaw"]
        self.assertAlmostEqual(u["move"][0], -0.1, places=6)
        self.assertAlmostEqual(u["rotate"][1], -q.y, places=6)
        back = rig.from_unity({"Jaw": u})["Jaw"]
        self.assertLess((back[0] - m).length, 1e-6)
        self.assertLess(back[1].rotation_difference(q).angle, 1e-5)

    def test_the_exported_skeleton_is_the_games_with_the_offsets_composed(self):
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))
        self.arm.pose.bones["Calve.L"].rotation_quaternion = Quaternion((0, 0, 1), 0.3)
        rig.apply(self.arm, self.data)
        out = os.path.join(tempfile.mkdtemp(), "e.glb")

        send.export(self.arm, out)

        exported = _worlds(out)
        original = _worlds(os.path.join(os.path.dirname(self.path), "model.glb"))
        offset = rig.offsets(self.arm)["Calve.L"]
        game_local = original["Femur.L"].inverted() @ original["Calve.L"]
        expected = exported["Femur.L"] @ rig._matrix(rig.compose(offset, game_local.decompose()))
        got = exported["Calve.L"]
        self.assertLess((expected.translation - got.translation).length, 1e-3)
        self.assertLess(expected.to_quaternion().rotation_difference(got.to_quaternion()).angle, 1e-3)


def _worlds(glb):
    """{joint: its world matrix (glTF space)} from the .glb's inverse bind matrices, read without Blender."""
    with open(glb, "rb") as f:
        blob = f.read()
    length = struct.unpack_from("<I", blob, 12)[0]
    doc = json.loads(blob[20:20 + length])
    data_start = 20 + length + 8
    skin = doc["skins"][0]
    accessor = doc["accessors"][skin["inverseBindMatrices"]]
    view = doc["bufferViews"][accessor["bufferView"]]
    start = data_start + view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    out = {}
    for k, joint in enumerate(skin["joints"]):
        v = struct.unpack_from("<16f", blob, start + 64 * k)
        inverse_bind = Matrix([v[0:4], v[4:8], v[8:12], v[12:16]]).transposed()  # glTF is column-major
        out[doc["nodes"][joint]["name"]] = inverse_bind.inverted()
    return out
