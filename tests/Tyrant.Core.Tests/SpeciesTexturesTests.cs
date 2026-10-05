using Tyrant.Core.Assets;
using Tyrant.Core.Mods;

namespace Tyrant.Core.Tests;

public class SpeciesTexturesTests
{
    private static AssetRecord Texture(string name, string guid) => new("b.bundle", 1, "Texture2D", name, $"Assets/{name}.png", guid, null);

    private static readonly AssetIndex Index = new()
    {
        Assets = [Texture("T_anax_D", "g-d"), Texture("T_anax_N", "g-n"), Texture("T_anax_infant_D", "g-id"), Texture("FurMaskEmpty", "g-fur"), Texture("T_fragilis_D", "g-fd")],
    };

    private static Dictionary<string, string> Maps(params (string Slot, string Guid)[] maps) => maps.ToDictionary(m => m.Slot, m => m.Guid);

    private static readonly IReadOnlyList<SpeciesSkins> Species =
    [
        new("Allosaurus Anax", false, [new VanillaSkin(0, "Pyroclastic",
            Maps(("diffuse", "g-d"), ("normal", "g-n"), ("infantDiffuse", "g-id"), ("fur", "g-fur")),
            Maps(("diffuse", "g-d"), ("normal", "g-n"), ("unknownSlotGuid", "g-missing")))]),
        new("Allosaurus Fragilis", false, [new VanillaSkin(0, "Gembone", Maps(("diffuse", "g-fd"), ("fur", "g-fur")), Maps())]),
    ];

    [Fact]
    public void A_species_lists_each_of_its_textures_once_with_what_it_is_and_its_skins()
    {
        var textures = SpeciesTextures.For(Species, Index, "allosaurus anax");

        Assert.Equal(["T_anax_D", "T_anax_N", "FurMaskEmpty", "T_anax_infant_D"], textures.Select(t => t.Texture));
        var diffuse = textures[0];
        Assert.Equal("adult colour", diffuse.Slot);
        Assert.Equal(["Pyroclastic"], diffuse.Skins);
        Assert.Empty(diffuse.SharedWith);
        Assert.Equal("baby colour", textures.Single(t => t.Texture == "T_anax_infant_D").Slot);
    }

    [Fact]
    public void A_texture_other_species_use_too_says_which()
    {
        var fur = SpeciesTextures.For(Species, Index, "Allosaurus Anax").Single(t => t.Texture == "FurMaskEmpty");

        Assert.Equal(["Allosaurus Fragilis"], fur.SharedWith);
        Assert.Equal("adult fur", fur.Slot);
    }

    [Fact]
    public void An_unknown_species_has_no_textures()
    {
        Assert.Empty(SpeciesTextures.For(Species, Index, "Unicorn"));
    }
}
