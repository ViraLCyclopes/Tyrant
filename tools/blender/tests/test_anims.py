import json
import os
import unittest

import bpy
from mathutils import Matrix, Quaternion, Vector

from test_ik_build import open_ik
from tyrant_blender import anims, ik, rig


def true_local(arm, name):
    """The bone's parent-relative pose (Blender frames), with the rig edit's lost scale put back."""
    bpy.context.view_layer.update()
    sigma = rig._sigma(arm)
    one = Vector((1, 1, 1))
    bone = arm.pose.bones[name]
    world = bone.matrix @ Matrix.Diagonal(sigma.get(name, one)).to_4x4()
    if bone.parent is None:
        return world
    parent = bone.parent.matrix @ Matrix.Diagonal(sigma.get(bone.parent.name, one)).to_4x4()
    return parent.inverted() @ world


def unity(p, q, s=Vector((1, 1, 1))):
    """A glTF-space local (position, quaternion, scale) as the clip file's Unity-space values."""
    return [-p.x, p.y, p.z], [q.x, -q.y, -q.z, q.w], [s.x, s.y, s.z]


def clip(name, bones, length=1.0, rate=30.0, travels=False):
    """bones: {bone: [(time, (pos, quat, scale))]} in glTF space (Blender bone frames); written as the game's Unity keys."""
    out = []
    for bone, keys in bones.items():
        position, rotation, scale = [], [], []
        for t, (p, q, s) in keys:
            up, uq, us = unity(p, q, s)
            position.append({"time": t, "x": up[0], "y": up[1], "z": up[2]})
            rotation.append({"time": t, "x": uq[0], "y": uq[1], "z": uq[2], "w": uq[3]})
            scale.append({"time": t, "x": us[0], "y": us[1], "z": us[2]})
        out.append({"bone": bone, "position": position, "rotation": rotation, "scale": scale})
    return {"id": "Carch|" + name, "name": name, "frameRate": rate, "length": length, "loops": True, "travels": travels,
            "bones": out, "skipped": []}


def rest_trs(arm, name):
    """The bone's rest local (parent-relative), as position, rotation, scale."""
    bone = arm.data.bones[name]
    local = bone.matrix_local if bone.parent is None else bone.parent.matrix_local.inverted() @ bone.matrix_local
    return local.decompose()


@unittest.skipUnless(os.environ.get("TYRANT_TEST_IK_PROJECT"), "needs the IK fixture project")
class AnimTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.arm, self.path = open_ik(controls=False)
        with open(self.path, encoding="utf-8") as f:
            self.data = json.load(f)

    def calve_walk(self, lift=0.1):
        p, q, s = rest_trs(self.arm, "Calve.L")
        return clip("Walk", {"Calve.L": [(0.0, (p, q, s)), (1.0, (p + Vector((0, lift, 0)), q, s))]})

    def test_a_clip_plays_each_bones_local_as_keyed(self):
        action = anims.load(self.arm, self.data, self.calve_walk())
        anims.play(self.arm, action)
        bpy.context.scene.frame_set(31)

        p, q, _ = rest_trs(self.arm, "Calve.L")
        self.assertLess((true_local(self.arm, "Calve.L").translation - (p + Vector((0, 0.1, 0)))).length, 1e-4)
        self.assertEqual(action.name, "Walk")
        self.assertEqual((bpy.context.scene.frame_start, bpy.context.scene.frame_end), (1, 31))

    def test_loading_the_same_animation_again_replaces_its_action(self):
        anims.load(self.arm, self.data, self.calve_walk())
        anims.load(self.arm, self.data, self.calve_walk(0.2))

        self.assertEqual([a.name for a in bpy.data.actions if a.get(anims.CLIP_ID)], ["Walk"])

    def test_on_a_rig_edited_rest_the_edit_is_composed_as_the_game_does(self):
        game_rest = rest_trs(self.arm, "Calve.L")
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))
        rig.apply(self.arm, self.data)
        offset = rig.offsets(self.arm)["Calve.L"]
        p, q, s = game_rest
        walk = clip("Walk", {"Calve.L": [(0.0, (p, q, s)), (1.0, (p + Vector((0, 0.1, 0)), q, s))]})

        anims.play(self.arm, anims.load(self.arm, self.data, walk))
        bpy.context.scene.frame_set(31)

        expected = rig._matrix(rig.compose(offset, (p + Vector((0, 0.1, 0)), q, s)))
        self.assertLess((true_local(self.arm, "Calve.L").translation - expected.translation).length, 1e-3)

    def test_under_a_parent_a_rig_edit_scaled_children_play_where_the_game_puts_them(self):
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Femur.L"].scale = Vector((2.0, 2.0, 2.0))
        rig.apply(self.arm, self.data)
        p, q, s = rig._trs(rig._unflat(json.loads(self.arm[rig.GAME])["Calve.L"]))
        walk = clip("Walk", {"Calve.L": [(0.0, (p, q, s)), (1.0, (p, q, s))]})

        anims.play(self.arm, anims.load(self.arm, self.data, walk))
        bpy.context.scene.frame_set(15)

        self.assertLess((true_local(self.arm, "Calve.L").translation - p).length, 1e-3)

    def test_growth_channels_are_left_to_the_growth_slider(self):
        p, q, s = rest_trs(self.arm, "Hip")
        walk = clip("Walk", {"Hip": [(0.0, (p, q, s)), (1.0, (p + Vector((0, 0.3, 0)), q, s))]})

        action = anims.load(self.arm, self.data, walk)
        anims.play(self.arm, action)

        paths = {fc.data_path for fc in anims.fcurves(self.arm, action)}
        self.assertNotIn('pose.bones["Hip"].location', paths)
        self.assertIn('pose.bones["Hip"].rotation_quaternion', paths)
        bpy.context.scene.frame_set(1)
        before = self.arm.pose.bones["Hip"].head.copy()
        self.arm.tyrant_growth = 0.0
        bpy.context.view_layer.update()
        self.assertGreater((self.arm.pose.bones["Hip"].head - before).length, 1e-3)

    def test_in_place_holds_the_travelling_bone_without_changing_its_keys(self):
        p, q, s = rest_trs(self.arm, "MainBone")
        walk = clip("Walk", {"MainBone": [(0.0, (p, q, s)), (1.0, (p + Vector((0, 0, 2.0)), q, s))]}, travels=True)
        action = anims.load(self.arm, self.data, walk)
        anims.play(self.arm, action)
        keys = [tuple(k.co) for fc in anims.fcurves(self.arm, action) for k in fc.keyframe_points]
        bpy.context.scene.frame_set(31)
        bpy.context.view_layer.update()
        travelled = self.arm.pose.bones["MainBone"].head.copy()

        anims.set_in_place(self.arm, True)
        bpy.context.view_layer.update()
        held = self.arm.pose.bones["MainBone"].head.copy()
        rest = self.arm.data.bones["MainBone"].head_local

        self.assertGreater((travelled.xy - rest.xy).length, 1.0)
        self.assertLess((held.xy - rest.xy).length, 1e-4)
        self.assertEqual(keys, [tuple(k.co) for fc in anims.fcurves(self.arm, action) for k in fc.keyframe_points])
        anims.set_in_place(self.arm, False)
        bpy.context.view_layer.update()
        self.assertLess((self.arm.pose.bones["MainBone"].head - travelled).length, 1e-4)

    def test_move_to_ik_controls_keeps_the_feet_where_the_clip_had_them(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        arm, path = open_ik(controls=True)
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        p, q, s = rest_trs(arm, "Calve.L")
        turned = Quaternion((1, 0, 0), 0.5) @ q
        walk = clip("Walk", {"Calve.L": [(0.0, (p, q, s)), (1.0, (p, turned, s))]})
        anims.play(arm, anims.load(arm, data, walk))
        bpy.context.scene.frame_set(16)
        bpy.context.view_layer.update()
        fk_foot = arm.pose.bones["Foot.L"].head.copy()

        anims.move_to_ik(arm, bpy.context.scene)
        bpy.context.scene.frame_set(16)
        bpy.context.view_layer.update()

        chain = next(c for c in ik.built(arm) if c["name"] == "Leg L")
        self.assertGreater(arm.pose.bones[chain["target"]][ik.IK_FK], 0.5)
        self.assertLess((arm.pose.bones["Foot.L"].head - fk_foot).length, 0.02)

    def test_files_next_to_the_project_load_as_actions(self):
        folder = os.path.join(os.path.dirname(self.path), "animations")
        os.makedirs(folder, exist_ok=True)
        file = os.path.join(folder, "Carch_Walk.json")
        with open(file, "w", encoding="utf-8") as f:
            json.dump(self.calve_walk(), f)

        actions = anims.load_files(self.arm, self.data, [file])

        self.assertEqual([a.name for a in actions], ["Walk"])

    def test_the_animations_box_draws_and_a_finished_add_loads_the_files_and_reports_errors(self):
        from tyrant_blender import animpanel

        folder = os.path.join(os.path.dirname(self.path), "animations")
        os.makedirs(folder, exist_ok=True)
        file = os.path.join(folder, "Carch_Walk.json")
        with open(file, "w", encoding="utf-8") as f:
            json.dump(self.calve_walk(), f)

        animpanel.finish_add(self.arm.name, self.path, {"ok": True, "files": [file], "errors": ["'Carch|Nope' is not one of its animations"]})

        self.assertEqual(self.arm.animation_data.action.name, "Walk")
        self.assertIn("Nope", self.arm[animpanel.REPORT])
        drawn = []

        class Layout:
            def __getattr__(self, name):
                def call(*args, **kwargs):
                    drawn.append((name, args, kwargs))
                    return self
                return call

        animpanel.draw(Layout(), bpy.context, self.arm, self.data, lambda layout, context, text, icon="NONE": drawn.append(("say", (text,), {})))
        self.assertTrue(any(d[0] == "operator" and d[1] == ("tyrant.anim_add",) for d in drawn))
        self.assertTrue(any(d[0] == "operator" and d[1] == ("tyrant.anim_play",) for d in drawn))
        self.assertTrue(any(d[0] == "prop" and d[1][1] == "tyrant_in_place" for d in drawn))

    def test_the_in_place_switch_follows_the_property(self):
        self.arm.tyrant_in_place = True
        bone = anims.travel_bone(self.arm)
        self.assertFalse(self.arm.pose.bones[bone.name].constraints[anims.IN_PLACE].mute)
        self.arm.tyrant_in_place = False
        self.assertTrue(self.arm.pose.bones[bone.name].constraints[anims.IN_PLACE].mute)

    def test_a_project_opened_with_animations_loads_them(self):
        from tyrant_blender import importer, project

        folder = os.path.join(os.path.dirname(self.path), "animations")
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, "Carch_Walk.json"), "w", encoding="utf-8") as f:
            json.dump(self.calve_walk(), f)
        data = project.load(self.path)
        data["animationFiles"] = ["animations/Carch_Walk.json"]
        project.save(self.path, data)
        bpy.ops.wm.read_factory_settings(use_empty=True)

        arm = importer.import_project(self.path)

        self.assertEqual(arm.animation_data.action.name, "Walk")

    def test_a_clip_holding_the_prefab_pose_reproduces_the_stance_the_model_opens_in(self):
        """The root bone too: its local is in the armature's own space, which Blender turns Z up (the real Carch's MainBone
        is turned 180 degrees there, and a wrong conversion tips the whole animal over)."""
        import struct

        with open(os.path.join(os.path.dirname(self.path), "model.glb"), "rb") as f:
            blob = f.read()
        doc = json.loads(blob[20:20 + struct.unpack_from("<I", blob, 12)[0]])
        nodes = {n.get("name"): n for n in doc["nodes"]}
        bpy.context.view_layer.update()
        stance = {n: self.arm.pose.bones[n].matrix.copy() for n in ("MainBone", "Hip", "Femur.L")}
        bones = {}
        for name in stance:
            node = nodes[name]
            p = Vector(node.get("translation", [0, 0, 0]))
            r = node.get("rotation", [0, 0, 0, 1])
            q = Quaternion((r[3], r[0], r[1], r[2]))
            sc = Vector(node.get("scale", [1, 1, 1]))
            bones[name] = [(0.0, (p, q, sc)), (1.0, (p, q, sc))]

        anims.play(self.arm, anims.load(self.arm, self.data, clip("Hold", bones)))
        bpy.context.scene.frame_set(1)
        bpy.context.view_layer.update()

        for name, before in stance.items():
            now = self.arm.pose.bones[name].matrix
            self.assertLess(before.to_quaternion().rotation_difference(now.to_quaternion()).angle, 1e-3, name)
            self.assertLess((before.translation - now.translation).length, 1e-3, name)

    def test_a_channel_the_clip_leaves_out_keeps_the_games_value_with_the_rig_edit(self):
        """Unity leaves an unanimated channel at the prefab's value; a rig edit's scale on that bone stays on top of it."""
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Calve.L"].scale = Vector((1.5, 1.5, 1.5))
        rig.apply(self.arm, self.data)
        prefab = {r["name"]: r for r in self.data["rest"]}["Calve.L"]
        p = Vector(prefab["position"])
        q = Quaternion((prefab["rotation"][3], prefab["rotation"][0], prefab["rotation"][1], prefab["rotation"][2]))
        only_rotation = clip("Turn", {"Calve.L": [(0.0, (p, q, Vector((1, 1, 1))))]})
        only_rotation["bones"][0]["position"] = []
        only_rotation["bones"][0]["scale"] = []

        anims.play(self.arm, anims.load(self.arm, self.data, only_rotation))
        bpy.context.scene.frame_set(1)

        local = true_local(self.arm, "Calve.L")
        offset = rig.offsets(self.arm)["Calve.L"]
        expected = rig._matrix(rig.compose(offset, (p, q, Vector(prefab["scale"]))))
        self.assertLess((local.to_scale() - expected.to_scale()).length, 1e-3)
        self.assertLess((local.translation - expected.translation).length, 1e-3)

    def test_a_bone_the_model_lacks_and_the_clips_skipped_curves_are_reported(self):
        walk = self.calve_walk()
        walk["bones"].append({"bone": "Tail.099", "position": [{"time": 0, "x": 0, "y": 0, "z": 0}], "rotation": [], "scale": []})
        walk["skipped"] = ["curve kind 9 is not read (only bone position, rotation and scale)"]
        folder = os.path.join(os.path.dirname(self.path), "animations")
        os.makedirs(folder, exist_ok=True)
        file = os.path.join(folder, "Carch_Walk.json")
        with open(file, "w", encoding="utf-8") as f:
            json.dump(walk, f)

        anims.load_files(self.arm, self.data, [file])

        report = json.loads(self.arm[anims.REPORT])
        self.assertTrue(any("Tail.099" in line for line in report))
        self.assertTrue(any("curve kind 9" in line for line in report))

    def test_a_broken_animation_file_is_reported_not_left_reading(self):
        from tyrant_blender import animpanel

        folder = os.path.join(os.path.dirname(self.path), "animations")
        os.makedirs(folder, exist_ok=True)
        file = os.path.join(folder, "Carch_Bad.json")
        with open(file, "w", encoding="utf-8") as f:
            json.dump({"id": "Carch|Bad", "name": "Bad", "bones": {"not": "a list"}}, f)
        self.arm[anims.REPORT] = json.dumps(["Tyrant is reading 1 animation(s)…"])

        animpanel.finish_add(self.arm.name, self.path, {"ok": True, "files": [file], "errors": []})

        report = json.loads(self.arm[anims.REPORT])
        self.assertTrue(any("could not be loaded" in line for line in report))
        self.assertFalse(any("reading" in line for line in report))

    def test_opening_a_model_already_in_the_file_with_new_animations_adds_them(self):
        from tyrant_blender import project, ui

        walk = self.calve_walk()
        bpy.ops.wm.read_factory_settings(use_empty=True)
        ui.show_project(self.path)
        folder = os.path.join(os.path.dirname(self.path), "animations")
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, "Carch_Walk.json"), "w", encoding="utf-8") as f:
            json.dump(walk, f)
        data = project.load(self.path)
        data["animationFiles"] = ["animations/Carch_Walk.json"]
        data["animationErrors"] = ["'Carch|Gone' is not one of its animations"]
        project.save(self.path, data)
        from tyrant_blender import growth
        growth.forget(self.path)

        scene = ui.show_project(self.path)

        arm = project.tagged_armatures(scene)[0]
        self.assertIn("Walk", [a.name for a in anims.tyrant_actions()])
        self.assertTrue(any("Carch|Gone" in line for line in json.loads(arm[anims.REPORT])))

    def walk_from_file(self, walk=None):
        """The calve walk (on the game's skeleton) as Tyrant writes it next to the project, loaded and playing."""
        folder = os.path.join(os.path.dirname(self.path), "animations")
        os.makedirs(folder, exist_ok=True)
        file = os.path.join(folder, "Carch_Walk.json")
        with open(file, "w", encoding="utf-8") as f:
            json.dump(walk or self.calve_walk(), f)
        return anims.load_files(self.arm, self.data, [file])[0]

    def test_a_playing_animation_is_held_off_during_a_rig_edit_and_plays_again_after(self):
        self.walk_from_file()

        rig.start(self.arm, self.data)
        self.assertIsNone(self.arm.animation_data.action)
        self.arm.pose.bones["Calve.L"].location += Vector((0.0, 0.2, 0.0))
        moved = Vector(self.arm.pose.bones["Calve.L"].location)
        bpy.context.scene.frame_set(15)
        self.assertLess((Vector(self.arm.pose.bones["Calve.L"].location) - moved).length, 1e-6)
        rig.apply(self.arm, self.data)

        self.assertEqual(self.arm.animation_data.action.name, "Walk")

    def test_cancel_puts_the_playing_animation_back(self):
        self.walk_from_file()
        rig.start(self.arm, self.data)

        rig.cancel(self.arm, self.data)

        self.assertEqual(self.arm.animation_data.action.name, "Walk")

    def test_after_a_rig_edit_the_animations_play_on_the_new_skeleton(self):
        p, _, _ = rest_trs(self.arm, "Calve.L")
        self.walk_from_file()
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Femur.L"].scale = Vector((2.0, 2.0, 2.0))
        rig.apply(self.arm, self.data)

        bpy.context.scene.frame_set(31)

        # The game puts the calve where the clip keys it under the scaled thigh (the clip's local, unscaled).
        self.assertLess((true_local(self.arm, "Calve.L").translation - (p + Vector((0, 0.1, 0)))).length, 1e-3)

    def test_after_clearing_the_rig_edit_the_animations_play_on_the_games_skeleton(self):
        p, _, _ = rest_trs(self.arm, "Calve.L")
        walk = self.calve_walk()
        rig.start(self.arm, self.data)
        self.arm.pose.bones["Femur.L"].scale = Vector((2.0, 2.0, 2.0))
        rig.apply(self.arm, self.data)
        self.walk_from_file(walk)

        rig.clear(self.arm, self.data)
        bpy.context.scene.frame_set(31)

        self.assertEqual(self.arm.animation_data.action.name, "Walk")
        self.assertLess((true_local(self.arm, "Calve.L").translation - (p + Vector((0, 0.1, 0)))).length, 1e-3)

    def test_select_all_and_clear_tick_every_animation_in_the_add_list(self):
        items = bpy.context.window_manager.tyrant_anim_items
        items.clear()
        for clip_id in ("Carch|Walk", "Carch|Roar", "Carch|Idle"):
            items.add().clip_id = clip_id

        bpy.ops.tyrant.anim_pick_all(pick=True)
        self.assertTrue(all(item.pick for item in items))
        bpy.ops.tyrant.anim_pick_all(pick=False)
        self.assertFalse(any(item.pick for item in items))

    def test_a_track_on_the_armature_object_itself_is_not_reported_as_a_missing_bone(self):
        walk = self.calve_walk()
        walk["bones"].append(dict(walk["bones"][0], bone=self.arm.name))

        action = anims.load(self.arm, self.data, walk)

        self.assertEqual(json.loads(action[anims.NOTES]), [])

    def test_while_a_chain_plays_in_fk_its_controls_follow_the_animation(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        arm, path = open_ik(controls=True)
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        p, q, s = rest_trs(arm, "Calve.L")
        turned = Quaternion((1, 0, 0), 0.5) @ q
        anims.play(arm, anims.load(arm, data, clip("Walk", {"Calve.L": [(0.0, (p, q, s)), (1.0, (p, turned, s))]})))
        chain = next(c for c in ik.built(arm) if c["name"] == "Leg L")
        control = arm.pose.bones[chain["target"]]
        bpy.context.view_layer.update()
        at_start = control.matrix.translation.copy()

        bpy.context.scene.frame_set(16)
        bpy.context.view_layer.update()
        followed = control.matrix.translation.copy()
        ik.snap_controls(arm, [chain["name"]])
        bpy.context.view_layer.update()

        self.assertGreater((followed - at_start).length, 0.01)  # the foot moved, and its control with it
        self.assertLess((followed - control.matrix.translation).length, 1e-4)  # exactly where the foot has it

    def test_opening_a_model_with_ik_controls_and_animations_in_a_fresh_blender(self):
        # As Open in Blender with animations picked when Blender was closed: a new scene, IK controls built, the first
        # animation playing — the controls follow it (this crashed Blender inside its frame change).
        from test_ik_build import fresh_ik_project
        from tyrant_blender import project, ui

        walk = self.calve_walk()
        path = fresh_ik_project()
        folder = os.path.join(os.path.dirname(path), "animations")
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, "Carch_Walk.json"), "w", encoding="utf-8") as f:
            json.dump(walk, f)
        data = project.load(path)
        data["animationFiles"] = ["animations/Carch_Walk.json"]
        project.save(path, data)
        bpy.ops.wm.read_factory_settings(use_empty=True)

        scene = ui.show_project(path)
        scene.frame_set(16)

        arm = project.tagged_armatures(scene)[0]
        self.assertEqual(arm.animation_data.action.name, "Walk")
        self.assertTrue(ik.built(arm))

    def test_the_controls_follow_without_asking_blender_to_evaluate_inside_its_frame_change(self):
        # Blender 5.0 crashed when the frame-change handler made it evaluate the scene again (view_layer.update): the
        # handler places the controls from the pose the frame already has.
        bpy.ops.wm.read_factory_settings(use_empty=True)
        arm, path = open_ik(controls=True)
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        p, q, s = rest_trs(arm, "Calve.L")
        turned = Quaternion((1, 0, 0), 0.5) @ q
        anims.play(arm, anims.load(arm, data, clip("Walk", {"Calve.L": [(0.0, (p, q, s)), (1.0, (p, turned, s))]})))
        chain = next(c for c in ik.built(arm) if c["name"] == "Leg L")
        control = arm.pose.bones[chain["target"]]
        bpy.context.view_layer.update()
        at_start = control.matrix.translation.copy()
        evaluations = []
        snap, refresh = ik.snap_controls, ik.refresh
        ik.snap_controls = lambda *a, **k: evaluations.append("snap_controls")
        ik.refresh = lambda *a, **k: evaluations.append("refresh")
        try:
            bpy.context.scene.frame_set(16)
        finally:
            ik.snap_controls, ik.refresh = snap, refresh
        bpy.context.view_layer.update()

        self.assertEqual(evaluations, [])
        self.assertGreater((control.matrix.translation - at_start).length, 0.01)
