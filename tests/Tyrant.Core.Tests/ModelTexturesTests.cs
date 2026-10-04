using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class ModelTexturesTests
{
    private const string Prefabs = "StandaloneWindows64/acro_assets_assets/acro.prefab.bundle";

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "model");

    private static PrefabModel WithMaterials(params MaterialModel[] materials)
    {
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(skinned: false, withShape: false), skinned: false);
        return prefab with { Renderers = [prefab.Renderers[0] with { Materials = materials }] };
    }

    [Fact]
    public void A_texture_that_cannot_be_read_leaves_its_material_plain_and_is_reported()
    {
        using var game = new FakeGame(); // no bundles at all, so every texture read fails
        using var session = new AssetSession(new GameInstall(game.Root, null));
        var index = new AssetIndex { Assets = [new AssetRecord(Prefabs, 5, "Texture2D", "T_Acro_D", null, null, null)] };
        var prefab = WithMaterials(new MaterialModel("Acro", [new TextureSlot("_AdultDiffuse", null, 5)]));

        var textures = ModelTextures.Write(session, index, Prefabs, prefab, TempDir());

        Assert.Equal(new GltfMaterial("Acro"), Assert.Single(textures.For(prefab.Renderers[0])!));
        Assert.StartsWith("T_Acro_D: ", Assert.Single(textures.Failures));
        Assert.Contains(textures.Notes, n => n.Contains("T_Acro_D"));
        Assert.Equal("T_Acro_D", Assert.Single(textures.Materials).BaseColor?.Name);
    }

    [Fact]
    public void An_animal_materials_extra_and_pattern_maps_are_written_next_to_the_diffuse()
    {
        using var game = new FakeGame(); // no bundles: every write fails, so Failures shows exactly which textures were written
        using var session = new AssetSession(new GameInstall(game.Root, null));
        string[] names = ["T_Acro_D", "T_Acro_N", "T_Acro_E", "T_Acro_P", "T_Acro_F"];
        string[] slots = ["_AdultDiffuse", "_AdultNormal", "_AdultExtraMap", "_AdultPatternMask", "_AdultFurMask"];
        var index = new AssetIndex { Assets = names.Select((n, i) => new AssetRecord(Prefabs, i + 1, "Texture2D", n, null, null, null)).ToList() };
        var prefab = WithMaterials(new MaterialModel("Acro", slots.Select((s, i) => new TextureSlot(s, null, i + 1)).ToList()));

        var textures = ModelTextures.Write(session, index, Prefabs, prefab, TempDir());

        Assert.Equal(["T_Acro_D", "T_Acro_E", "T_Acro_N", "T_Acro_P"], textures.Failures.Select(f => f[..f.IndexOf(':')]).Order()); // fur is not used by the viewer
        Assert.Empty(textures.TextureFiles);
    }

    [Fact]
    public void A_scenery_materials_mask_maps_are_not_written()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));
        var index = new AssetIndex { Assets = [new AssetRecord(Prefabs, 1, "Texture2D", "Fence_D", null, null, null), new AssetRecord(Prefabs, 2, "Texture2D", "Fence_Mix", null, null, null)] };
        var prefab = WithMaterials(new MaterialModel("Fence", [new TextureSlot("_DiffuseTex", null, 1), new TextureSlot("_MixTex", null, 2)]));

        var textures = ModelTextures.Write(session, index, Prefabs, prefab, TempDir());

        Assert.Equal(["Fence_D"], textures.Failures.Select(f => f[..f.IndexOf(':')]));
    }

    [Fact]
    public void A_material_without_a_name_is_named_after_its_mesh_and_left_out_of_the_list()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));
        var prefab = WithMaterials(new MaterialModel("", []), new MaterialModel("Eyes", []));

        var textures = ModelTextures.Write(session, new AssetIndex(), Prefabs, prefab, TempDir());

        Assert.Equal(new[] { new GltfMaterial("Tri"), new GltfMaterial("Eyes") }, textures.For(prefab.Renderers[0]));
        Assert.Equal("Eyes", Assert.Single(textures.Materials).Name);
        Assert.Empty(textures.Failures);
    }

    [Fact]
    public void An_index_without_archives_notes_that_textures_need_a_new_index()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));
        var prefab = WithMaterials(new MaterialModel("Acro", [new TextureSlot("_AdultDiffuse", "archive:/CAB-tex/CAB-tex", 17)]));

        var textures = ModelTextures.Write(session, new AssetIndex(), Prefabs, prefab, TempDir());

        Assert.Contains(textures.Notes, n => n.Contains("Index assets"));
    }

    [Fact]
    public void A_model_whose_textures_all_resolve_has_no_notes()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));
        var prefab = WithMaterials(new MaterialModel("Eyes", []));

        Assert.Empty(ModelTextures.Write(session, new AssetIndex(), Prefabs, prefab, TempDir()).Notes);
    }
}
