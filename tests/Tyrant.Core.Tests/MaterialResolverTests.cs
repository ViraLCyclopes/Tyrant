using Tyrant.Core.Assets;
using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class MaterialResolverTests
{
    private const string Prefabs = "StandaloneWindows64/acro_assets_assets/acro.prefab.bundle";
    private const string Textures = "StandaloneWindows64/acro_assets_assets/t_acro_d.png.bundle";

    private static readonly AssetRecord Diffuse = new(Textures, 17, "Texture2D", "T_Acro_D", null, null, null);
    private static readonly AssetRecord Normal = new(Textures, 18, "Texture2D", "T_Acro_N", null, null, null);
    private static readonly AssetRecord Detail = new(Prefabs, 5, "Texture2D", "Detail_Skin", null, null, null);
    private static readonly AssetRecord Shader = new(Prefabs, 6, "Shader", "Animal", null, null, null);

    private static AssetIndex Index() => new() { Assets = [Diffuse, Normal, Detail, Shader], Archives = { ["CAB-tex"] = Textures } };

    [Fact]
    public void The_animal_shaders_adult_slots_give_the_picture_and_the_normal_map()
    {
        var material = new MaterialModel("Acro", [
            new TextureSlot("_DetailNormal", null, 5),
            new TextureSlot("_AdultNormal", "archive:/CAB-tex/CAB-tex", 18),
            new TextureSlot("_AdultDiffuse", "archive:/CAB-tex/CAB-tex", 17),
        ]);

        var resolved = MaterialResolver.Resolve(Index(), Prefabs, material);

        Assert.Equal(("Acro", Diffuse, "_AdultDiffuse", Normal), (resolved.Name, resolved.BaseColor, resolved.BaseColorSlot, resolved.Normal));
        Assert.Empty(resolved.Missing);
    }

    [Fact]
    public void Unitys_standard_slots_are_used_when_the_animal_slots_are_absent()
    {
        var material = new MaterialModel("Rock", [new TextureSlot("_MainTex", null, 5), new TextureSlot("_BumpMap", "archive:/CAB-tex/CAB-tex", 18)]);

        var resolved = MaterialResolver.Resolve(Index(), Prefabs, material);

        Assert.Equal((Detail, "_MainTex", Normal), (resolved.BaseColor, resolved.BaseColorSlot, resolved.Normal));
    }

    [Fact]
    public void Textures_in_unknown_files_or_of_the_wrong_type_are_reported_missing()
    {
        var material = new MaterialModel("Acro", [
            new TextureSlot("_AdultDiffuse", "archive:/CAB-gone/CAB-gone", 17),
            new TextureSlot("_MainTex", null, 6), // a shader, not a texture
            new TextureSlot("_AdultNormal", "archive:/CAB-tex/CAB-tex", 18),
        ]);

        var resolved = MaterialResolver.Resolve(Index(), Prefabs, material);

        Assert.Null(resolved.BaseColor);
        Assert.Equal(Normal, resolved.Normal);
        Assert.Equal(new[] { "_AdultDiffuse", "_MainTex" }, resolved.Missing);
    }
}
