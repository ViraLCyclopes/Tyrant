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


class OpenProjectTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_a_new_project_is_imported_and_saved_as_its_blend(self):
        path = fresh_project()
        ui.import_and_save(path)
        data = project.load(path)
        self.assertTrue(data["blend"].endswith(".blend"))
        self.assertTrue(os.path.isfile(data["blend"]))
        self.assertEqual(os.path.dirname(data["blend"]), os.path.dirname(path))
        self.assertEqual(bpy.data.filepath, data["blend"])
