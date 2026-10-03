using AssetsTools.NET;
using SharpGLTF.Schema2;
using Tyrant.Core.Assets;
using Tyrant.Core.Models;
using static Tyrant.Core.Tests.MeshFields;

namespace Tyrant.Core.Tests;

public class AssetReaderTests
{
    private static AssetTypeValueField PPtr(string name, int fileId, long pathId) =>
        Obj(name, I32("m_FileID", fileId), Prim("m_PathID", AssetValueType.Int64, pathId));

    [Fact]
    public void References_are_collected_with_their_field_paths()
    {
        var root = Obj("Base",
            Str("m_Name", "AnimalData_Stego"),
            PPtr("m_Script", 1, 99),
            PPtr("prefab", 0, 0),
            Obj("art", PPtr("diffuse", 0, 7)),
            Vector("skins", PPtr("data", 0, 12), PPtr("data", 2, 34)));

        var references = AssetReferences.Collect(root);

        Assert.Equal(
            [new AssetReference("m_Script", 1, 99), new AssetReference("art.diffuse", 0, 7), new AssetReference("skins[0]", 0, 12), new AssetReference("skins[1]", 2, 34)],
            references);
    }

    [Fact]
    public void Collection_stops_at_the_limit()
    {
        var root = Obj("Base", Vector("many", Enumerable.Range(1, 50).Select(i => PPtr("data", 0, i)).ToArray()));

        Assert.Equal(10, AssetReferences.Collect(root, max: 10).Count);
    }

    [Fact]
    public void Texture_facts_come_from_the_texture_fields()
    {
        var texture = Obj("Base", I32("m_Width", 2048), I32("m_Height", 1024), I32("m_TextureFormat", 12), I32("m_MipCount", 12));

        Assert.Equal(new TextureFacts(2048, 1024, "DXT5", 12), TextureFacts.Read(texture));
    }

    [Fact]
    public void Texture_without_a_mip_count_has_one_level()
    {
        var texture = Obj("Base", I32("m_Width", 4), I32("m_Height", 4), I32("m_TextureFormat", 4));

        Assert.Equal(1, TextureFacts.Read(texture).MipCount);
    }

    [Fact]
    public void A_standalone_mesh_becomes_a_static_glb()
    {
        var model = StandaloneMesh.ToPrefab(ModelFixture.Triangle("Body", skinned: true, withShape: false));
        var path = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "body.glb");

        GltfModelWriter.WriteGlb(model, model.Renderers[0], path);

        var gltf = ModelRoot.Load(path);
        Assert.Single(gltf.LogicalMeshes);
        Assert.Empty(gltf.LogicalSkins);
        Assert.Equal("Body", model.Name);
        Assert.False(model.Renderers[0].IsSkinned);
    }
}
