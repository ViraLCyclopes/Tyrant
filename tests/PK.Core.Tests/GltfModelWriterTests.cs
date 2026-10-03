using System.Numerics;
using PK.Core.Models;
using SharpGLTF.Schema2;

namespace PK.Core.Tests;

public class GltfModelWriterTests
{
    private static string TempGlb() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"), "model.glb");

    [Fact]
    public void Skinned_mesh_has_joints_morph_target_and_node_hierarchy()
    {
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle());
        var path = TempGlb();

        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path);
        var model = ModelRoot.Load(path);

        Assert.Equal(2, Assert.Single(model.LogicalSkins).JointsCount);
        var mesh = Assert.Single(model.LogicalMeshes);
        var primitive = Assert.Single(mesh.Primitives);
        Assert.Equal(1, primitive.MorphTargetsCount);
        Assert.Equal("Infant", mesh.Extras?["targetNames"]?[0]?.GetValue<string>());
        Assert.Equal(3, primitive.GetIndices().Count);
        Assert.Contains(model.LogicalNodes, n => n.Name == "Tail");
        Assert.NotNull(primitive.GetVertexAccessor("JOINTS_0"));
        Assert.NotNull(primitive.GetVertexAccessor("COLOR_0"));
    }

    [Fact]
    public void Winding_is_reversed_so_faces_agree_with_normals_after_mirroring()
    {
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(skinned: false, withShape: false), skinned: false);
        var path = TempGlb();

        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path);
        var primitive = ModelRoot.Load(path).LogicalMeshes[0].Primitives[0];
        var p = primitive.GetVertexAccessor("POSITION").AsVector3Array();
        var n = primitive.GetVertexAccessor("NORMAL").AsVector3Array();
        var i = primitive.GetIndices();
        var face = Vector3.Cross(p[(int)i[1]] - p[(int)i[0]], p[(int)i[2]] - p[(int)i[0]]);

        Assert.True(Vector3.Dot(face, n[(int)i[0]]) > 0, "triangle winding disagrees with its normal");
        Assert.Contains(p, v => Math.Abs(v.X + 1) < 1e-6); // X mirrored: (1,0,0) became (-1,0,0)
    }

    [Fact]
    public void Static_mesh_has_no_skin()
    {
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(skinned: false), skinned: false);
        var path = TempGlb();

        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path);
        var model = ModelRoot.Load(path);

        Assert.Empty(model.LogicalSkins);
        Assert.Contains(model.LogicalNodes, node => node.Mesh is not null && node.Name == "Animal");
    }

    [Fact]
    public void Bone_index_beyond_joints_is_rejected()
    {
        var bad = ModelFixture.Triangle();
        bad.Skin[0] = new BoneWeight4(5, 0, 0, 0, 1, 0, 0, 0);
        var prefab = ModelFixture.Prefab(bad);

        Assert.Throws<InvalidDataException>(() => GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], TempGlb()));
    }

    [Fact]
    public void Mesh_without_normals_is_not_supported()
    {
        var mesh = ModelFixture.Triangle(skinned: false);
        var noNormals = new MeshData
        {
            Name = mesh.Name, Positions = mesh.Positions, Normals = [], Uv0 = mesh.Uv0, Colors = [], Skin = [], Indices = mesh.Indices,
            SubMeshes = mesh.SubMeshes, BindPoses = [], BlendShapes = [],
        };
        var prefab = ModelFixture.Prefab(noNormals, skinned: false);

        Assert.Throws<NotSupportedException>(() => GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], TempGlb()));
    }

    [Fact]
    public void Matrix_conversion_matches_converting_TRS_parts()
    {
        var p = new Vector3(1.5f, -2f, 3f);
        var q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(0.3f, 1f, -0.4f)), 0.7f));
        var s = new Vector3(1f, 2f, 0.5f);
        Matrix4x4 Trs(Vector3 t, Quaternion r, Vector3 sc) => Matrix4x4.CreateScale(sc) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);

        var converted = UnityToGltf.Matrix(Trs(p, q, s));
        var expected = Trs(UnityToGltf.Position(p), UnityToGltf.Rotation(q), s);

        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 4; c++)
                Assert.Equal(expected[r, c], converted[r, c], 4);
        Assert.Equal(new Vector2(0.25f, 0.75f), UnityToGltf.Uv(new Vector2(0.25f, 0.25f)));
    }
}
