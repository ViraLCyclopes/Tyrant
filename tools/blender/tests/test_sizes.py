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
    data["sizeSkins"] = [[1.08, 1.0]]  # the species' every skin, as the Size box's note counts them
    data["geneticRanges"] = {"incubation": [0.925, 1.075], "nursery": None, "breeding": [0.85, 1.15]}  # the game's
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


def drawn_sizes(arm, data):
    """What the Size box draws, as (call, args, kwargs)."""
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
    return drawn


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

    def test_size_display_not_capped_by_project_limit(self):
        # The view shows the size you set; the note says where the game would cap it (Send raises the species' limit to fit).
        path = allosaurus(sizes={"adult": 4.0})  # an adult male of 1.08 x 4
        data = project.load(path)
        data["sizeLimit"] = [0.05, 2.0]
        project.save(path, data)
        arm = importer.import_project(path)
        arm.tyrant_growth = 1.0
        self.assertGreater(arm.scale.x, 2.0)
        self.assertScale(arm, 1.08 * 4)

        arm.tyrant_size_adult = 20.0
        arm.tyrant_size_male = 20.0  # 1.08 x 20 x 20: only Tyrant's own bound of 20 holds it
        self.assertScale(arm, 20.0)

    def test_note_when_past_the_limit(self):
        path = allosaurus(sizes={"adult": 4.0})
        data = project.load(path)
        data["sizeLimit"] = [0.05, 2.0]
        project.save(path, data)
        arm = importer.import_project(path)
        # Counted as Check and Send count it, the game's genetic ranges included: 1.08 x 4 x 1.15 = 4.97, so Send raises the
        # limit to 5 (BlenderSizesTests.Blender_note_and_send_agree_on_the_add_ons_fixture pins Tyrant's side of this number).
        note = "In game this is capped at 2×. Send to Tyrant will raise this species' Size limit to 5×."
        self.assertEqual(growth.limit_note(arm, data), note)
        self.assertIn(("say", (note,), {}), drawn_sizes(arm, data))

        data["sizeLimit"] = [0.05, 3.5]
        self.assertEqual(growth.limit_note(arm, data), "In game this is capped at 3.5×. Send to Tyrant will raise this species' Size limit to 5×.")

        data["sizeLimit"] = [0.05, 20.0]  # within the limit: no note
        self.assertIsNone(growth.limit_note(arm, data))
        self.assertFalse([d for d in drawn_sizes(arm, data) if d[0] == "say" and "capped" in d[1][0]])

        data["sizeLimit"] = [0.05, 2.0]
        arm.tyrant_size_adult = 1.0  # 1.08 x 1.15 = 1.24 at most: within the game's 2
        self.assertIsNone(growth.limit_note(arm, data))

    def test_raise_clears_the_safety_margin(self):
        # As Tyrant's raise (SpeciesSizes.RaisedLimit): the game backs a size off 0.1% inside its limit, so a largest just under
        # a 0.5 step gets the next step (ModSizesTests.Raise_clears_the_safety_margin).
        self.assertEqual(growth.raised_limit(2.9985, 2.0), 3.5)
        self.assertEqual(growth.raised_limit(2.99, 2.0), 3.0)
        self.assertEqual(growth.raised_limit(2.99, 10.0), 10.0)  # never lowered
        path = allosaurus(sizes={"adult": 4.0})
        data = project.load(path)
        data["sizeLimit"] = [0.05, 4.97]  # 1.08 x 4 x 1.15 = 4.968: inside 4.97, past the 4.965 the game backs off to
        project.save(path, data)
        arm = importer.import_project(path)
        self.assertEqual(growth.limit_note(arm, data), "In game this is capped at 4.97×. Send to Tyrant will raise this species' Size limit to 5×.")

    def test_note_counts_the_species_ranges_and_every_skin(self):
        path = allosaurus(sizes={"adult": 4.0})
        data = project.load(path)
        data["sizeLimit"] = [0.05, 2.0]
        data["geneticRanges"] = {"incubation": [0.925, 1.075], "nursery": [0.5, 3.0], "breeding": [0.85, 1.15]}
        project.save(path, data)
        arm = importer.import_project(path)
        # The species' own Nursery range: 1.08 x 4 x 3 = 12.96, raised to 13.
        self.assertEqual(growth.limit_note(arm, data), "In game this is capped at 2×. Send to Tyrant will raise this species' Size limit to 13×.")

        data["geneticRanges"] = {"incubation": [0.925, 1.075], "nursery": None, "breeding": [0.85, 1.15]}
        data["sizeSkins"] = [[1.08, 1.0], [1.5, 1.2]]  # another skin is bigger: 1.5 x 4 x 1.15 = 6.9
        self.assertEqual(growth.limit_note(arm, data), "In game this is capped at 2×. Send to Tyrant will raise this species' Size limit to 7×.")

    def test_note_at_the_highest_limit_promises_no_raise(self):
        path = allosaurus(sizes={"adult": 20.0})
        data = project.load(path)
        data["sizeLimit"] = [0.05, 20.0]
        project.save(path, data)
        arm = importer.import_project(path)
        # 1.08 x 20 x 1.15 is past 20, the highest limit: Send cannot raise it, so the note does not say it will.
        self.assertEqual(growth.limit_note(arm, data), "In game this is capped at 20×.")

    def test_sizes_go_to_a_species_chosen_on_send(self):
        # Sizes edited for a destination that is gone (Send cleared it) still go to the species model chosen next.
        path = allosaurus(sizes={})
        arm = importer.import_project(path)
        arm.tyrant_size_adult = 4.0
        data = project.load(path)
        data["destination"] = None
        data["sizesEditable"] = False
        project.save(path, data)
        growth.forget(path)
        bpy.context.view_layer.objects.active = arm
        started = []
        real = send.run_async
        send.run_async = lambda args, done: started.append(args)
        try:
            panel._start_send(bpy.context, arm, {"mod": "horny-rex", "species": "Carcharodontosaurus", "skin": None}, "Horny Rex")
            panel._start_send(bpy.context, arm, {"mod": "horny-rex", "species": "Carcharodontosaurus", "skin": "red"})
        finally:
            send.run_async = real
        with open(started[0][started[0].index("--sizes") + 1], encoding="utf-8") as f:
            self.assertEqual(json.load(f), {"format": 1, "target": "Carcharodontosaurus", "adult": 4.0})
        self.assertNotIn("--sizes", started[1])  # a skin's sizes are the species'

    def test_the_size_panel_reaches_20(self):
        arm = importer.import_project(allosaurus())
        arm.tyrant_size_adult = 15.0
        self.assertAlmostEqual(growth.size_edits(arm)["adult"], 15.0, places=5)
        arm.tyrant_size_female = 25.0  # past the file's maximum: Blender keeps it at 20
        self.assertAlmostEqual(growth.size_edits(arm)["female"], 20.0, places=5)

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
