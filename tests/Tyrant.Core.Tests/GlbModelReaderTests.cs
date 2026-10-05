using System.Numerics;
using Tyrant.Core.Errors;
using Tyrant.Core.Models;
using Tyrant.Core.ModelReplacements;

namespace Tyrant.Core.Tests;

public class GlbModelReaderTests
{
    private static string TempGlb(string name = "model.glb") => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), name);

    /// <summary>The read vertex at an original vertex's position (the glTF writer orders vertices as triangles add them).</summary>
    private static int Find(MeshData mesh, Vector3 position) =>
        Enumerable.Range(0, mesh.VertexCount).Single(i => Vector3.Distance(mesh.Positions[i], position) < 1e-5f);

    [Fact]
    public void A_tyrant_export_reads_back_as_the_same_mesh()
    {
        var path = TempGlb();
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path, [new GltfMaterial("Carch")]);

        var mesh = Assert.Single(GlbModelReader.Read(path));

        var original = prefab.Renderers[0].Mesh;
        Assert.Equal(0, mesh.Lod);
        Assert.Equal(original.VertexCount, mesh.Mesh.VertexCount);
        var map = Enumerable.Range(0, original.VertexCount).Select(i => Find(mesh.Mesh, original.Positions[i])).ToArray();
        for (var i = 0; i < original.VertexCount; i++)
        {
            Assert.True(Vector2.Distance(original.Uv0[i], mesh.Mesh.Uv0[map[i]]) < 1e-5f, $"uv {i}");
            Assert.True(Vector3.Distance(original.Normals[i], mesh.Mesh.Normals[map[i]]) < 1e-5f, $"normal {i}");
        }
        Assert.Equal(["Hip", "Tail"], mesh.JointNames);
        Assert.Equal(["Carch"], mesh.MaterialNames);
        Assert.Equal("Infant", Assert.Single(mesh.Mesh.BlendShapes).Name);
        // Unity's winding restored: the triangle's corners come back in the original order (up to rotation).
        var corners = mesh.Mesh.Indices.Select(i => Array.IndexOf(map, (int)i)).ToArray();
        var rotations = new[] { new[] { 0, 1, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 } };
        Assert.Contains(rotations, r => r.SequenceEqual(corners));
    }

    [Fact]
    public void Skin_weights_name_their_bones()
    {
        var path = TempGlb();
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path);

        var mesh = Assert.Single(GlbModelReader.Read(path));

        var third = mesh.Mesh.Skin[Find(mesh.Mesh, prefab.Renderers[0].Mesh.Positions[2])];
        Assert.Equal((0.5f, 0.5f), (third.W0, third.W1));
        Assert.Equal(["Hip", "Tail"], new[] { mesh.JointNames[third.I0], mesh.JointNames[third.I1] });
    }

    [Fact]
    public void A_shape_key_reads_back_with_its_offsets()
    {
        var path = TempGlb();
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path);

        var mesh = Assert.Single(GlbModelReader.Read(path)).Mesh;
        var shape = Assert.Single(mesh.BlendShapes);

        Assert.Equal([Find(mesh, prefab.Renderers[0].Mesh.Positions[1])], shape.VertexIndices);
        Assert.True(Vector3.Distance(new Vector3(0, 0.5f, 0), shape.PositionDeltas[0]) < 1e-5f);
    }

    [Theory]
    [InlineData("Carch_LOD00", 0)]
    [InlineData("Carch_LOD1", 1)]
    [InlineData("carch_lod02", 2)]
    [InlineData("Carch", 0)]
    [InlineData("Carch_LOD1.001", 1)]
    public void Lod_numbers_come_from_the_mesh_name(string name, int lod)
    {
        Assert.Equal(lod, GlbModelReader.LodOf(name));
    }

    [Fact]
    public void Two_main_meshes_are_both_read_for_the_builder_to_choose()
    {
        var path = TempGlb();
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Body"), skinned: true);
        var second = new RendererModel("Horn", ModelFixture.Triangle(name: "Horn"), prefab.Renderers[0].Bones, prefab.Renderers[0].Owner);
        GltfModelWriter.WriteGlbs(prefab with { Renderers = [prefab.Renderers[0], second] }, path);

        var meshes = GlbModelReader.Read(path);

        Assert.Equal(2, meshes.Count(m => m.Lod == 0)); // ModelBuilder.PickMeshes keeps one, or asks to join them
    }

    [Fact]
    public void Own_lod_meshes_are_found_by_name()
    {
        var path = TempGlb();
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch"), skinned: true);
        var lod1 = new RendererModel("Carch_LOD1", ModelFixture.Triangle(name: "Carch_LOD1"), prefab.Renderers[0].Bones, prefab.Renderers[0].Owner);
        GltfModelWriter.WriteGlbs(prefab with { Renderers = [prefab.Renderers[0], lod1] }, path);

        Assert.Equal([0, 1], GlbModelReader.Read(path).Select(m => m.Lod));
    }

    [Fact]
    public void Blenders_duplicate_name_suffix_is_ignored_on_materials()
    {
        var path = TempGlb();
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path, [new GltfMaterial("Carch.001")]); // Blender renames on a second import

        Assert.Equal(["Carch"], Assert.Single(GlbModelReader.Read(path)).MaterialNames);
    }

    [Fact]
    public void A_file_that_is_not_a_glb_is_a_clear_error()
    {
        var path = TempGlb();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a model");

        Assert.Contains("could not be read", Assert.Throws<TyrantException>(() => GlbModelReader.Read(path)).Message);
    }

    [Fact]
    public void A_glb_without_a_skinned_mesh_is_a_clear_error()
    {
        var path = TempGlb();
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Rock", skinned: false, withShape: false), skinned: false);
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path);

        Assert.Contains("no mesh skinned to an armature", Assert.Throws<TyrantException>(() => GlbModelReader.Read(path)).Message);
    }
}
