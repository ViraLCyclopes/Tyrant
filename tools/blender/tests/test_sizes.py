import json
import os
import shutil
import struct
import tempfile
import unittest

import bpy

from test_import import fresh_project
from tyrant_blender import growth, importer, panel, project, send

ALLOSAURUS = [[0.0, 0.164], [1.0, 1.0]]  # its scale curve's ends: a hatchling is 0.164 of the adult


def allosaurus(editable=True, skin=None, sizes=None):
    """The fixture project with an Allosaurus' size: male 1.08, female 1, sent to the species model (or a skin)."""
    path = fresh_project()
    data = project.load(path)
    data["growth"]["scale"] = ALLOSAURUS
    data["growth"]["originalScale"] = ALLOSAURUS
    data["sexes"]["male"]["size"] = 1.08
    data["sexes"]["female"]["size"] = 1.0
    data["sizes"] = dict({"adult": None, "baby": None, "male": None, "female": None, "maleOriginal": 1.08, "femaleOriginal": 1.0},
                         **(sizes or {}))
    data["sizesEditable"] = editable
    data["destination"] = {"mod": "m", "species": "Carcharodontosaurus", "skin": skin}
    data["tyrant"] = os.path.join(tempfile.gettempdir(), "tyrant.exe")  # an absolute path: Send only runs a tyrant.exe on this PC
    project.save(path, data)
    return path


# The Velociraptor's growth curve as the game has it (the dump of TYRANT_TEST_HERMITE_PROJECT): (time, value, in, out tangent).
VELOCIRAPTOR = [(0.0, 0.393908322, 1.73721325, 1.73721325), (1.0, 1.0, 0.0356990322, 0.0356990322)]


def hermite(keys, t):
    """Unity's AnimationCurve.Evaluate for unweighted keys: Hermite between keys, clamped outside them."""
    if t <= keys[0][0]:
        return keys[0][1]
    if t >= keys[-1][0]:
        return keys[-1][1]
    (t0, v0, _, out), (t1, v1, into, _) = next((a, b) for a, b in zip(keys, keys[1:]) if t <= b[0])
    dt = t1 - t0
    s = (t - t0) / dt
    return ((2 * s ** 3 - 3 * s ** 2 + 1) * v0 + (s ** 3 - 2 * s ** 2 + s) * dt * out
            + (-2 * s ** 3 + 3 * s ** 2) * v1 + (s ** 3 - s ** 2) * dt * into)


def baby_male():
    return 0.164 * (1 + (1.08 - 1) * 0.2)


def glb_nodes(path):
    with open(path, "rb") as f:
        data = f.read()
    return json.loads(data[20:20 + struct.unpack_from("<I", data, 12)[0]])["nodes"]


class SizeTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def assertScale(self, arm, value):
        for axis in arm.scale:
            self.assertAlmostEqual(axis, value, places=4)

    def test_growth_scales_armature_by_size_at_baby(self):
        arm = importer.import_project(allosaurus())

        arm.tyrant_growth = 0.0
        self.assertScale(arm, baby_male())  # S(0, 1.08, 1) = curve(0) · (1 + (1.08 − 1) · f(0)), f(0) = 0.2

        arm.tyrant_growth = 1.0
        self.assertScale(arm, 1.08)
        arm.tyrant_sex = "FEMALE"
        arm.tyrant_growth = 0.0
        self.assertScale(arm, 0.164)

    def test_growth_halfway_uses_the_games_fade(self):
        arm = importer.import_project(allosaurus())
        arm.tyrant_growth = 0.5  # the fixture's skin curve is a straight line: maturity 0.5
        curve = 0.164 + (1 - 0.164) * 0.5
        self.assertScale(arm, curve * (1 + 0.08 * (0.2 + 0.8 * 0.75)))

    def test_size_follows_maturity_not_the_skin_curve(self):
        # The game sizes by maturity itself (AnimalNative.CurrentScale): the skin curve (textures only) must not bend it.
        path = allosaurus()
        data = project.load(path)
        data["growth"]["skin"] = [(i / 100) ** 3 for i in range(101)]  # far from a straight line: 0.25 → 0.0156
        project.save(path, data)
        arm = importer.import_project(path)
        arm.tyrant_growth = 0.25
        curve = 0.164 + (1 - 0.164) * 0.25
        self.assertScale(arm, curve * (1 + 0.08 * (0.2 + 0.8 * (1 - 0.75 ** 2))))

    def test_without_a_scale_curve_the_model_keeps_its_scale(self):
        arm = importer.import_project(fresh_project())  # the fixture's species has no scale curve
        arm.tyrant_growth = 0.0
        self.assertScale(arm, 1.0)

    def test_rebuild_matches_core(self):
        # SizeMath.Rebuild: adult · shape(m) / (1 + (adult − 1)·f(m)); shape maps C(0) to baby · C(1).
        keys = growth.rebuild([[0.0, 0.5], [1.0, 1.0], [2.0, 1.2]], 1.2, None)
        self.assertEqual(len(keys), 18)
        self.assertAlmostEqual(keys[0][1], 1.2 * 0.5 / 1.04, places=6)
        self.assertAlmostEqual(keys[16][1], 1.0, places=6)
        self.assertEqual(keys[17], [2.0, 1.2])
        baby = growth.rebuild([[0.0, 0.5], [1.0, 1.0]], 1.0, 0.25)
        self.assertAlmostEqual(baby[0][1], 0.25, places=6)
        self.assertAlmostEqual(baby[16][1], 1.0, places=6)

    def test_send_ignores_display_scale(self):
        path = allosaurus()
        arm = importer.import_project(path)
        arm.tyrant_growth = 0.0
        glb = os.path.join(os.path.dirname(path), "send.glb")

        send.export(arm, glb)

        for node in glb_nodes(glb):
            for axis in node.get("scale", [1, 1, 1]):
                self.assertAlmostEqual(axis, 1.0, places=5)
        self.assertScale(arm, baby_male())  # the size shown is back after the export
        self.assertAlmostEqual(arm.tyrant_growth, 0.0)

        arm.scale = [2 * v for v in arm.scale]  # you double the object: your own scale is kept, and sent
        arm.tyrant_growth = 0.0
        self.assertScale(arm, 2 * baby_male())
        send.export(arm, glb)
        armature_node = next(n for n in glb_nodes(glb) if n.get("name") == arm.name)
        for axis in armature_node["scale"]:
            self.assertAlmostEqual(axis, 2.0, places=5)
        self.assertScale(arm, 2 * baby_male())

    def test_size_panel_sends_sizes(self):
        path = allosaurus(sizes={"male": 1.1, "baby": 0.2})  # set in the app before
        arm = importer.import_project(path)
        bpy.context.view_layer.objects.active = arm
        started = []
        real = send.run_async
        send.run_async = lambda args, done: started.append((args, done))
        try:
            panel._start_send(bpy.context, arm)  # nothing changed: no sizes go
            self.assertNotIn("--sizes", started[-1][0])

            self.assertAlmostEqual(arm.tyrant_size_baby, 0.2, places=5)
            arm.tyrant_size_adult = 1.2
            arm.tyrant_size_female = 0.9
            arm.tyrant_size_male = 1.1  # the value it has: not an edit
            self.assertScale(arm, 1.08 * 1.2 * 1.1)  # the display follows at once: adult male = 1.08 × 1.2 × 1.1
            panel._start_send(bpy.context, arm)
        finally:
            send.run_async = real

        args, done = started[-1]
        with open(args[args.index("--sizes") + 1], encoding="utf-8") as f:
            sent = json.load(f)
        # Only the keys edited in Blender: Tyrant merges them onto the mod's current sizes (the app may have changed others).
        self.assertEqual(sent, {"format": 1, "target": "Carcharodontosaurus", "adult": 1.2, "female": 0.9})

        done({"ok": True, "errors": [], "warnings": [], "lodVertices": []})
        self.assertFalse(growth.size_edits(arm))  # sent: the project now holds them

    def test_sizes_of_a_skin_destination_are_read_only(self):
        path = allosaurus(editable=False, skin="red", sizes={"adult": 1.5})
        arm = importer.import_project(path)
        data = project.load(path)
        self.assertAlmostEqual(arm.tyrant_size_adult, 1.5, places=5)
        self.assertAlmostEqual(arm.tyrant_size_baby, 0.164, places=5)  # unset: the game's own share
        arm.tyrant_size_adult = 2.0  # refused: the species' sizes are set in the app
        self.assertAlmostEqual(arm.tyrant_size_adult, 1.5, places=5)
        drawn = []

        class Layout:
            def __getattr__(self, name):
                def call(*args, **kwargs):
                    drawn.append((name, args, kwargs))
                    return self
                return call

        def say(layout, context, text, icon="NONE"):
            drawn.append(("say", (text,), {}))

        panel.draw_sizes(Layout(), bpy.context, arm, data, say)
        self.assertIn(("say", ("Sizes apply to the whole species; set them in the app.",), {}), drawn)
        self.assertIn(("prop", (arm, "tyrant_size_adult"), {}), drawn)

        bpy.context.view_layer.objects.active = arm
        started = []
        real = send.run_async
        send.run_async = lambda args, done: started.append(args)
        try:
            panel._start_send(bpy.context, arm, {"mod": "m", "species": "Carcharodontosaurus", "skin": "red"})
        finally:
            send.run_async = real
        self.assertNotIn("--sizes", started[-1])


@unittest.skipUnless(os.environ.get("TYRANT_TEST_HERMITE_PROJECT"), "needs the Hermite fixture project")
class HermiteGrowthTests(unittest.TestCase):
    """Growth on a project Tyrant wrote from a dump whose growth curve has the game's tangents: the size shown follows the
    game's Hermite curve between its keys, not a straight line (which is 23% small at half growth for this curve)."""

    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        src = os.environ["TYRANT_TEST_HERMITE_PROJECT"]
        dst = tempfile.mkdtemp(prefix="tyrant-hermite-")
        shutil.copytree(os.path.dirname(src), dst, dirs_exist_ok=True)
        self.path = os.path.join(dst, os.path.basename(src))

    def assertNear(self, actual, expected):
        self.assertAlmostEqual(actual / expected, 1.0, delta=0.01)

    def test_growth_follows_the_games_curve_between_its_keys(self):
        arm = importer.import_project(self.path)
        male, _ = growth.sex_sizes(arm, project.load(self.path))
        for value in (0.25, 0.5, 0.75):
            arm.tyrant_growth = value
            self.assertNear(arm.scale.x, hermite(VELOCIRAPTOR, value) * (1 + (male - 1) * growth.fade(value)))

    def test_a_new_adult_size_keeps_the_games_shape(self):
        data = project.load(self.path)
        data["sizesEditable"] = True
        data["sizes"].update({"maleOriginal": 1.0, "femaleOriginal": 1.0})
        data["destination"] = {"mod": "m", "species": "Carcharodontosaurus", "skin": None}
        project.save(self.path, data)
        arm = importer.import_project(self.path)
        arm.tyrant_size_adult = 1.2
        for value in (0.25, 0.5, 0.75):
            arm.tyrant_growth = value
            self.assertNear(arm.scale.x, 1.2 * hermite(VELOCIRAPTOR, value))  # S = adult × shape(m) for a sex size of 1
