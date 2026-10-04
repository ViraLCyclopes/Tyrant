using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Models;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class ShaderSlotsTests
{
    private static MaterialModel Animal(params string[] slots) => new("Stego", slots.Select((s, i) => new TextureSlot(s, null, i + 1)).ToList());

    [Fact]
    public void The_animal_materials_filled_slots_are_the_ones_shown()
    {
        var stego = Animal("_AdultDiffuse", "_AdultNormal", "_AdultExtraMap", "_AdultPatternMask", "_DetailNormal",
            "_InfantDiffuse", "_InfantNormal", "_InfantExtraMap", "_InfantPatternMask"); // no fur mask

        Assert.Equal(["diffuse", "normal", "extra", "pattern", "infantDiffuse", "infantNormal", "infantExtra", "infantPattern"],
            ShaderSlots.FromMaterials([new MaterialModel("Eyes", []), stego]));
    }

    [Fact]
    public void Without_an_animal_material_the_shader_is_unknown()
    {
        Assert.Null(ShaderSlots.FromMaterials([new MaterialModel("Rock", [new TextureSlot("_MainTex", null, 1)])]));
    }

    [Fact]
    public void The_base_skins_slots_are_narrowed_to_the_shaders()
    {
        string[] all = ["diffuse", "normal", "extra", "pattern", "fur", "infantDiffuse", "infantNormal", "infantExtra", "infantPattern", "infantFur"];
        string[] carch = ["diffuse", "normal", "extra", "fur", "infantDiffuse", "infantNormal", "infantExtra", "infantFur"]; // no pattern

        Assert.Equal(carch, ShaderSlots.Shown(all, carch));
        Assert.Equal(all, ShaderSlots.Shown(all, null));
        Assert.Equal(["diffuse", "normal"], ShaderSlots.Shown([], ["diffuse", "normal"]));
    }

    [Fact]
    public void Each_species_names_its_prefab()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        SkinDumps.Write(ws.DataDir);

        var species = SpeciesSkinsReader.Load(ws);

        Assert.Equal(SkinDumps.PrefabGuid, species.Single(s => s.SpeciesId == "Carcharodontosaurus").PrefabGuid);
        Assert.Null(species.Single(s => s.SpeciesId == "Frog").PrefabGuid);
    }
}
