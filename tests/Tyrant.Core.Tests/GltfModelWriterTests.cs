using System.Numerics;
using Tyrant.Core.Models;
using SharpGLTF.Schema2;

namespace Tyrant.Core.Tests;

public class GltfModelWriterTests
{
    private static string TempGlb() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "model.glb");

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
    public void Duplicate_node_names_are_kept_as_in_the_game()
    {
        // Titanoboa's prefab has several transforms named "Titanoboa"; glTF allows duplicate names.
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle());
        var root = new SkeletonNode("Titanoboa", Vector3.Zero, Quaternion.Identity, Vector3.One);
        var hip = new SkeletonNode("Titanoboa", Vector3.UnitY, Quaternion.Identity, Vector3.One, root);
        root.Children.Add(hip);
        var tail = new SkeletonNode("Titanoboa", Vector3.UnitZ, Quaternion.Identity, Vector3.One, hip);
        hip.Children.Add(tail);
        var duplicate = new PrefabModel("Titanoboa", root, [new RendererModel("Body", prefab.Renderers[0].Mesh, [hip, tail], root)], []);
        var path = TempGlb();

        GltfModelWriter.WriteGlb(duplicate, duplicate.Renderers[0], path);

        Assert.Equal(3, ModelRoot.Load(path).LogicalNodes.Count(n => n.Name == "Titanoboa"));
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

    [Fact]
    public void Prefab_root_is_placed_at_the_origin()
    {
        // Some prefabs (e.g. Yi, Archaeopteryx) were saved ~1 km from the origin; Unity overrides the root on spawn.
        var mesh = ModelFixture.Triangle();
        var root = new SkeletonNode("Yi", new Vector3(-1056.9f, 93.3f, 125.2f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.2f), new Vector3(2, 2, 2));
        var hip = new SkeletonNode("Hip", Vector3.UnitY, Quaternion.Identity, Vector3.One, root);
        root.Children.Add(hip);
        var tail = new SkeletonNode("Tail", Vector3.UnitZ, Quaternion.Identity, Vector3.One, hip);
        hip.Children.Add(tail);
        var prefab = new PrefabModel("Yi", root, [new RendererModel("Body", mesh, [hip, tail], root)], []);
        var path = TempGlb();

        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path);
        var node = ModelRoot.Load(path).LogicalNodes.Single(n => n.Name == "Yi");

        Assert.Equal(Vector3.Zero, node.LocalTransform.Translation);
        Assert.Equal(Quaternion.Identity, node.LocalTransform.Rotation);
        Assert.Equal(new Vector3(2, 2, 2), node.LocalTransform.Scale);
    }

    /// <summary>The JSON chunk of a .glb (header 12 bytes, then chunk length, type, data).</summary>
    private static System.Text.Json.Nodes.JsonNode GlbJson(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return System.Text.Json.Nodes.JsonNode.Parse(bytes.AsSpan(20, BitConverter.ToInt32(bytes, 12)))!;
    }

    private static string Png(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        using var stream = File.Create(path);
        // Distinct pixels per file: SharpGLTF merges images with identical bytes.
        var pixels = Enumerable.Repeat((byte)name[^5], 2 * 2 * 4).ToArray();
        new StbImageWriteSharp.ImageWriter().WritePng(pixels, 2, 2, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }

    private static MeshData TwoSubMeshes()
    {
        var tri = ModelFixture.Triangle(skinned: false, withShape: false);
        return new MeshData
        {
            Name = "Body", Positions = tri.Positions, Normals = tri.Normals, Uv0 = tri.Uv0, Colors = [], Skin = [],
            Indices = [0, 1, 2, 0, 2, 1], SubMeshes = [new SubMesh(0, 3, 0), new SubMesh(3, 3, 0)], BindPoses = [], BlendShapes = [],
        };
    }

    private static string[] Names(System.Text.Json.Nodes.JsonNode json, string array) =>
        json[array]!.AsArray().Select(m => m!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Materials_are_not_metal_so_animals_do_not_render_dark()
    {
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(skinned: false, withShape: false), skinned: false);
        var path = TempGlb();

        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path);

        var pbr = GlbJson(path)["materials"]![0]!["pbrMetallicRoughness"]!;
        Assert.Equal(0, pbr["metallicFactor"]!.GetValue<double>());
        Assert.InRange(pbr["roughnessFactor"]!.GetValue<double>(), 0.5, 1);
    }

    [Fact]
    public void Each_sub_mesh_gets_its_material_with_textures_linked_beside_the_file()
    {
        var path = TempGlb();
        var textures = Path.Combine(Path.GetDirectoryName(path)!, GltfModelWriter.TexturesFolder);
        var prefab = ModelFixture.Prefab(TwoSubMeshes(), skinned: false);
        GltfMaterial[] materials = [new("Skin", Png(textures, "T_Skin_D.png"), Png(textures, "T_Skin_N.png")), new("Eyes")];

        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path, materials);

        var json = GlbJson(path);
        Assert.Equal(new[] { "Eyes", "Skin" }, Names(json, "materials"));
        Assert.Equal(2, json["meshes"]![0]!["primitives"]!.AsArray().Count);
        Assert.Equal(new[] { "textures/T_Skin_D.png", "textures/T_Skin_N.png" },
            json["images"]!.AsArray().Select(i => i!["uri"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray());
        Assert.All(json["images"]!.AsArray(), i => Assert.Null(i!["bufferView"])); // linked, not embedded
        var skin = json["materials"]!.AsArray().Single(m => m!["name"]!.GetValue<string>() == "Skin")!;
        Assert.NotNull(skin["normalTexture"]);
        Assert.NotNull(skin["pbrMetallicRoughness"]!["baseColorTexture"]);
        Assert.True(File.Exists(Path.Combine(textures, "T_Skin_D.png")));
    }

    [Fact]
    public void A_texture_already_beside_the_file_is_linked_without_being_written_again()
    {
        var path = TempGlb();
        var textures = Path.Combine(Path.GetDirectoryName(path)!, GltfModelWriter.TexturesFolder);
        var png = Png(textures, "T_Skin_D.png");
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(png, old);
        var prefab = ModelFixture.Prefab(TwoSubMeshes(), skinned: false);

        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path, [new("Skin", png), new("Eyes")]);
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], Path.ChangeExtension(path, ".lod1.glb"), [new("Skin", png), new("Eyes")]);

        Assert.Equal(old, File.GetLastWriteTimeUtc(png)); // each LOD links the PNG ModelTextures wrote once
        Assert.Equal("textures/T_Skin_D.png", GlbJson(path)["images"]![0]!["uri"]!.GetValue<string>());
    }

    [Fact]
    public void A_skinned_model_is_written_as_one_mesh_with_no_helper_shapes()
    {
        var path = TempGlb();
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(skinned: true, withShape: false), skinned: true);

        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path);

        Assert.Single(GlbJson(path)["meshes"]!.AsArray()); // Blender's "Icosphere" is its own bone display shape, made on import
    }

    [Fact]
    public void Sub_meshes_without_a_material_are_kept_plain()
    {
        var path = TempGlb();
        var prefab = ModelFixture.Prefab(TwoSubMeshes(), skinned: false);

        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], path, [new GltfMaterial("Skin")]);

        var json = GlbJson(path);
        Assert.Equal(2, json["meshes"]![0]!["primitives"]!.AsArray().Count);
        Assert.Equal(new[] { "Body", "Skin" }, Names(json, "materials")); // the second sub-mesh falls back to the mesh's name
    }
}
