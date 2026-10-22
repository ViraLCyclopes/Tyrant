import json
import os
import tempfile
import unittest

import bpy
from mathutils import Vector

from test_ik_build import open_ik
from tyrant_blender import rig, send


@unittest.skipUnless(os.environ.get("TYRANT_TEST_IK_PROJECT"), "needs the IK fixture project")
class ExportModelTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.arm, self.project = open_ik(controls=False)
        self.folder = tempfile.mkdtemp()
        with open(self.project, encoding="utf-8") as f:
            self.data = json.load(f)

    def edit(self):
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))
        rig.apply(self.arm, self.data)

    def export(self, name, fmt):
        path = os.path.join(self.folder, name)
        result = bpy.ops.tyrant.export_model(filepath=path, format=fmt)
        return path, result

    def test_export_writes_the_model_and_its_rig_file(self):
        self.edit()
        for name, fmt in (("m.glb", "glb"), ("m.fbx", "fbx")):
            path, result = self.export(name, fmt)
            self.assertEqual(result, {"FINISHED"})
            self.assertGreater(os.path.getsize(path), 1000)
            with open(path + ".rig.json", encoding="utf-8") as f:
                self.assertEqual(json.load(f), json.loads(json.dumps(rig.unity_rig(self.arm))))
        self.assertFalse(os.path.exists(os.path.join(os.path.dirname(self.project), "send.glb")))

    def test_export_without_a_rig_edit_removes_a_stale_rig_file(self):
        path = os.path.join(self.folder, "m.glb")
        with open(path + ".rig.json", "w", encoding="utf-8") as f:
            f.write("{}")
        _, result = self.export("m.glb", "glb")
        self.assertEqual(result, {"FINISHED"})
        self.assertTrue(os.path.isfile(path))
        self.assertFalse(os.path.exists(path + ".rig.json"))

    def test_export_refuses_while_a_rig_edit_is_started(self):
        rig.start(self.arm, self.data)
        with self.assertRaises(RuntimeError) as caught:
            self.export("m.glb", "glb")
        self.assertIn("A rig edit is started", str(caught.exception))
        self.assertFalse(os.path.exists(os.path.join(self.folder, "m.glb")))

    def test_fbx_export_reads_back_like_the_glb(self):
        # The Tyrant side (ModelRigReader) reads both back in BlenderAddonRunTests; here both are written, alike, with the rig file.
        self.edit()
        glb, _ = self.export("a.glb", "glb")
        fbx, _ = self.export("a.fbx", "fbx")
        with open(glb + ".rig.json", encoding="utf-8") as a, open(fbx + ".rig.json", encoding="utf-8") as b:
            self.assertEqual(json.load(a), json.load(b))
        self.assertEqual(open(fbx, "rb").read(18), b"Kaydara FBX Binary")
        self.assertTrue(send.sendable(self.arm))
