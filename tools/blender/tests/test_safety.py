"""Bad project files, a busy Send, results on the main thread, and which tyrant.exe the add-on will run."""
import json
import os
import sys
import tempfile
import threading
import time
import unittest

import bpy

from test_import import fresh_project
from tyrant_blender import growth, importer, listener, panel, project, send


class BadProjectTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def _broken(self, change):
        path = fresh_project()
        data = project.load(path)
        change(data)
        project.save(path, data)
        return path

    def test_nested_missing_keys_are_a_project_error(self):
        for change in (lambda d: d["rest"].__setitem__(0, {"name": "Hip"}),
                       lambda d: d["source"].pop("species"),
                       lambda d: d["growth"]["bones"][0].pop("baby"),
                       lambda d: d.__setitem__("materials", [])):
            with self.assertRaises(project.ProjectError):
                project.load(self._broken(change))

    def test_the_growth_slider_and_the_panel_survive_a_broken_project_file(self):
        import tyrant_blender

        path = fresh_project()
        arm = importer.import_project(path)
        with open(path, "w", encoding="utf-8") as f:
            f.write("{ broken")
        growth.forget(path)
        tyrant_blender._growth_changed(arm, None)  # must not raise
        data, error = panel.project_data(arm)
        self.assertIsNone(data)
        self.assertIn("open it again from tyrant", error.lower())


class BusyTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_send_refuses_while_tyrant_is_building(self):
        arm = importer.import_project(fresh_project())
        bpy.context.view_layer.objects.active = arm
        arm[panel.REPORT] = json.dumps({"busy": True})
        with self.assertRaises(RuntimeError) as caught:
            bpy.ops.tyrant.send()
        self.assertIn("still building", str(caught.exception))

    def test_a_busy_flag_saved_in_a_file_is_cleared_when_it_opens(self):
        arm = importer.import_project(fresh_project())
        arm[panel.REPORT] = json.dumps({"busy": True})
        panel.clear_stale_busy()
        self.assertFalse(json.loads(arm[panel.REPORT]).get("busy"))


class MainThreadTests(unittest.TestCase):
    def test_send_results_are_applied_on_blenders_main_thread(self):
        fake = os.path.join(tempfile.mkdtemp(), "fake_tyrant.py")
        with open(fake, "w", encoding="utf-8") as f:
            f.write('import json\nprint(json.dumps({"ok": True, "errors": [], "warnings": [], "lodVertices": [3]}))\n')
        seen = []
        send.run_async([sys.executable, fake], lambda result: seen.append((result["ok"], threading.current_thread() is threading.main_thread())))
        deadline = time.time() + 20
        while send.finished.empty() and time.time() < deadline:
            time.sleep(0.05)
        send.drain_finished()
        self.assertEqual(seen, [(True, True)])


class TrustTests(unittest.TestCase):
    def test_only_a_local_tyrant_exe_is_run(self):
        self.assertTrue(send.trusted_tyrant(r"C:\Tyrant\sidecar\tyrant.exe"))
        self.assertFalse(send.trusted_tyrant(r"\\server\share\tyrant.exe"))
        self.assertFalse(send.trusted_tyrant("//server/share/tyrant.exe"))
        self.assertFalse(send.trusted_tyrant(r"C:\Temp\evil.exe"))
        self.assertFalse(send.trusted_tyrant("tyrant.exe"))  # relative

    def test_network_project_paths_are_refused(self):
        answer, path = listener.parse(json.dumps({"open": r"\\server\share\tyrant-blender.json"}))
        self.assertFalse(answer["ok"])
        self.assertIsNone(path)
        self.assertFalse(project.is_local(r"\\server\share\tyrant-blender.json"))
        self.assertTrue(project.is_local(r"C:\ws\blender\game\x\tyrant-blender.json"))


class TooltipTests(unittest.TestCase):
    def test_the_growth_slider_says_what_1_shows(self):
        text = bpy.types.Object.bl_rna.properties["tyrant_growth"].description
        self.assertIn("1 is your model as you edit and send it", text)
        self.assertIn("sex", text)


class WrapTests(unittest.TestCase):
    def test_long_panel_messages_wrap_to_the_sidebar_width(self):
        text = "This scene was not opened from Tyrant; use Open in Blender in Tyrant."
        lines = panel.wrap_lines(text, width=220, ui_scale=1.0)
        self.assertGreater(len(lines), 1)
        self.assertEqual(" ".join(lines), text)
        self.assertTrue(all(len(line) <= panel.chars_per_line(220, 1.0) for line in lines))
        self.assertEqual(panel.wrap_lines("Short.", width=400, ui_scale=1.0), ["Short."])
        self.assertGreater(len(panel.wrap_lines(text, width=220, ui_scale=2.0)), len(lines))  # bigger UI, fewer characters fit
