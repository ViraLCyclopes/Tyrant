import os
import shutil
import tempfile
import unittest

import bpy

from tyrant_blender import importer, meshops, project


def fresh_project():
    """A copy of the C#-written fixture project, so each test can change it."""
    src = os.environ["TYRANT_TEST_PROJECT"]
    dst_dir = tempfile.mkdtemp(prefix="tyrant-addon-")
    shutil.copytree(os.path.dirname(src), dst_dir, dirs_exist_ok=True)
    return os.path.join(dst_dir, os.path.basename(src))


class ImportTests(unittest.TestCase):
    def setUp(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_import_tags_merges_and_marks_seams(self):
        path = fresh_project()
        arm = importer.import_project(path)
        self.assertEqual(arm.type, "ARMATURE")
        self.assertEqual(arm[project.TAG], path)
        meshes = [o for o in bpy.data.objects if o.type == "MESH"]
        self.assertEqual(len(meshes), 1)
        mesh = meshes[0]
        self.assertEqual(mesh[project.TAG], path)
        # The fixture quad has 4 corners written as 6 split vertices (two triangles, the diagonal split).
        self.assertEqual(len(mesh.data.vertices), 4)
        self.assertEqual([k.name for k in mesh.data.shape_keys.key_blocks], ["Basis", "Infant"])
        self.assertEqual(sum(1 for e in mesh.data.edges if e.use_seam), 1)  # the diagonal: UVs differ across it
        self.assertEqual(arm.data.display_type, "OCTAHEDRAL")
        self.assertTrue(all(b.custom_shape is None for b in arm.pose.bones))
        self.assertTrue(any(c.name.startswith("Tyrant · Carcharodontosaurus") for c in bpy.data.collections))

    def test_merge_keeps_shape_key_offsets(self):
        importer.import_project(fresh_project())
        mesh = next(o for o in bpy.data.objects if o.type == "MESH")
        basis = mesh.data.shape_keys.key_blocks["Basis"]
        infant = mesh.data.shape_keys.key_blocks["Infant"]
        moved = [i for i, (a, b) in enumerate(zip(basis.data, infant.data)) if (a.co - b.co).length > 1e-4]
        self.assertEqual(len(moved), 1)  # the fixture moves exactly one corner

    def test_merge_keeps_the_games_shading(self):
        importer.import_project(fresh_project())
        mesh = next(o for o in bpy.data.objects if o.type == "MESH").data
        self.assertTrue(mesh.has_custom_normals)
        first = mesh.corner_normals[0].vector.normalized()
        for n in mesh.corner_normals:
            self.assertAlmostEqual(abs(n.vector.normalized().dot(first)), 1.0, places=4)

    def test_bad_project_is_reported(self):
        path = fresh_project()
        with open(path, "w", encoding="utf-8") as f:
            f.write('{"version": 99}')
        with self.assertRaises(project.ProjectError) as caught:
            project.load(path)
        self.assertIn("open it again from Tyrant", str(caught.exception))

    def test_seams_follow_uv_islands_only(self):
        bpy.ops.mesh.primitive_plane_add()
        plane = bpy.context.active_object
        self.assertEqual(meshops.mark_uv_seams(plane), 0)  # one island: no inner seams; borders are not seams
