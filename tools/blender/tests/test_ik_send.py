import json
import math
import os
import struct
import unittest

import bpy
from mathutils import Quaternion

from test_ik_build import game_pose, max_change, move, open_ik
from tyrant_blender import checks, project, send


def glb_nodes(path):
    data = open(path, "rb").read()
    gltf = json.loads(data[20:20 + struct.unpack_from("<I", data, 12)[0]])
    return [n.get("name", "") for n in gltf["nodes"]]


@unittest.skipUnless(os.environ.get("TYRANT_TEST_IK_PROJECT"), "needs the IK fixture project")
class IkSendTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_send_keeps_the_pose_and_growth_and_ships_no_control_bones(self):
        arm, path = open_ik()
        move(arm, "ctrl_foot.L", (0.0, -0.1, 0.15))
        arm.pose.bones["Neck"].rotation_quaternion = Quaternion((0, 0, 1), math.radians(10))
        arm.tyrant_growth = 0.4
        before = game_pose(arm)
        glb = os.path.join(os.path.dirname(path), "send.glb")

        send.export(arm, glb)

        self.assertAlmostEqual(arm.tyrant_growth, 0.4)
        self.assertLess(max_change(before, game_pose(arm)), 1e-5)
        nodes = glb_nodes(glb)
        self.assertFalse([n for n in nodes if n.startswith(("ctrl_", "mch_"))])
        for bone in ("MainBone", "Hip", "Femur.L", "Heel.L", "Head"):
            self.assertIn(bone, nodes)

    def test_a_game_bone_set_not_to_deform_stops_send(self):
        arm, path = open_ik()
        arm.data.bones["Calve.L"].use_deform = False

        found = checks.problems(arm, project.load(path))

        self.assertTrue(any("Calve.L" in p and "Deform" in p for p in found), found)
