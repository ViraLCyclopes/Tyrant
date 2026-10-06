import json
import os
import socket
import unittest

import bpy

from test_import import fresh_project
from tyrant_blender import listener, project, ui


class ListenerTests(unittest.TestCase):
    def test_accepts_only_open_with_an_existing_project_file(self):
        path = fresh_project()
        ok, opened = listener.parse(json.dumps({"open": path}))
        self.assertEqual(ok, {"ok": True})
        self.assertEqual(opened, path)

    def test_rejects_code_other_keys_other_files_and_garbage(self):
        path = fresh_project()
        for line in ('{"code": "import os"}', json.dumps({"open": path, "code": "x"}),
                     json.dumps({"open": os.path.join(os.path.dirname(path), "model.glb")}),
                     json.dumps({"open": "relative/tyrant-blender.json"}),
                     json.dumps({"open": os.path.join(os.path.dirname(path), "missing", "tyrant-blender.json")}),
                     json.dumps({"open": 5}), "not json", "[]"):
            response, opened = listener.parse(line)
            self.assertFalse(response["ok"], line)
            self.assertIsNone(opened, line)

    def test_server_answers_on_localhost_and_queues_only_good_requests(self):
        path = fresh_project()
        server = listener.Server(port=0)  # any free port
        server.start()
        try:
            for line, ok in ((b'{"code": "1"}\n', False), ((json.dumps({"open": path}) + "\n").encode(), True)):
                with socket.create_connection(("127.0.0.1", server.port), timeout=2) as s:
                    s.sendall(line)
                    answer = json.loads(s.makefile().readline())
                self.assertEqual(answer["ok"], ok)
            self.assertEqual(listener.pending.get(timeout=1), path)
            self.assertTrue(listener.pending.empty())
        finally:
            server.stop()


class SceneTests(unittest.TestCase):
    """Open in Blender puts the model in a scene of its own in the file that is open; it never replaces or saves the file."""

    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def window_scene(self):
        return bpy.context.window_manager.windows[0].scene

    def test_opening_adds_a_tagged_scene_and_leaves_the_rest_of_the_file_alone(self):
        first = self.window_scene()
        mine = bpy.data.objects.new("my JWE mesh", None)
        first.collection.objects.link(mine)
        path = fresh_project()

        ui.show_project(path)

        scene = self.window_scene()
        self.assertNotEqual(scene, first)
        self.assertEqual(scene[project.TAG], path)
        self.assertTrue(scene.name.startswith("Tyrant · game · Carcharodontosaurus"))
        self.assertTrue(any(o.type == "ARMATURE" for o in scene.objects))
        self.assertEqual([o.name for o in first.objects], ["my JWE mesh"])  # nothing was added to the other scene
        self.assertEqual(bpy.data.filepath, "")  # nothing was saved for you

    def test_opening_again_switches_back_without_importing_again(self):
        path = fresh_project()
        ui.show_project(path)
        scene = self.window_scene()
        self.window_scene_set(bpy.data.scenes[0] if bpy.data.scenes[0] != scene else bpy.data.scenes[1])

        ui.show_project(path)

        self.assertEqual(self.window_scene(), scene)
        self.assertEqual(len([o for o in bpy.data.objects if o.type == "ARMATURE"]), 1)

    def window_scene_set(self, scene):
        bpy.context.window_manager.windows[0].scene = scene

    def test_start_fresh_keeps_the_old_scene_and_imports_into_a_new_one(self):
        path = fresh_project()
        ui.show_project(path)
        old = self.window_scene()
        data = project.load(path)
        data["fresh"] = True
        project.save(path, data)

        ui.show_project(path)

        new = self.window_scene()
        self.assertNotEqual(new, old)
        self.assertTrue(old.name.endswith("(old)"))
        self.assertEqual(new[project.TAG], path)
        self.assertNotIn(project.TAG, old)
        self.assertFalse(project.load(path).get("fresh"))  # once


class QueueTests(unittest.TestCase):
    def test_the_open_queue_survives_bad_requests_one_per_tick(self):
        import tyrant_blender

        gone = os.path.join(os.path.dirname(fresh_project()), "gone", "tyrant-blender.json")
        listener.pending.put(gone)
        listener.pending.put(gone)
        self.assertEqual(tyrant_blender._drain(), 0.25)  # the timer keeps running
        self.assertEqual(listener.pending.qsize(), 1)  # one request per tick: the next waits for the next tick
        self.assertEqual(tyrant_blender._drain(), 0.25)
        self.assertTrue(listener.pending.empty())

    def test_an_add_on_updated_while_blender_runs_asks_for_a_restart_when_opening(self):
        said = []
        report, loaded = ui._report, dict(ui._LOADED)
        ui._report = said.append
        try:
            bpy.ops.wm.read_factory_settings(use_empty=True)
            ui.request_open(fresh_project())
            self.assertEqual(said, [])

            ui._LOADED["anims.py"] = -1.0  # as if Tyrant replaced the file after Blender loaded it
            bpy.ops.wm.read_factory_settings(use_empty=True)
            ui.request_open(fresh_project())
        finally:
            ui._report, ui._LOADED = report, loaded

        self.assertEqual(len(said), 1)
        self.assertIn("restart Blender", said[0])
