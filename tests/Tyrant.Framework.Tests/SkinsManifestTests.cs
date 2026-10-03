using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SkinsManifestTests
{
    private const string WithSkins = """
        {
          "format": 1,
          "id": "red-spot-carcharo",
          "skins": [
            {
              "id": "red-spot",
              "species": "Carcharodontosaurus",
              "name": "Red spot",
              "base": "Alt 1",
              "thumbnail": "skins/red-spot/thumbnail.png",
              "male": { "diffuse": "skins/red-spot/male_D.png", "Normal": "skins/red-spot/male_N.png" },
              "female": { "diffuse": "skins/red-spot/female_D.png" }
            },
            { "id": "male-only", "species": "Carcharodontosaurus", "male": { "infantDiffuse": "skins/m/infant_D.png" } }
          ]
        }
        """;

    [Fact]
    public void Reads_skins_with_canonical_slot_names_and_defaults()
    {
        var m = ModManifest.Parse(WithSkins);

        var red = m.Skins[0];
        Assert.Equal(("red-spot", "Carcharodontosaurus", "Red spot", "Alt 1", "skins/red-spot/thumbnail.png"), (red.Id, red.Species, red.Name, red.Base, red.Thumbnail));
        Assert.Equal("skins/red-spot/male_N.png", red.Male!["normal"]); // "Normal" read as the canonical "normal"
        Assert.Equal("skins/red-spot/female_D.png", red.Female!["diffuse"]);
        var maleOnly = m.Skins[1];
        Assert.Equal(("male-only", "0", null as object), (maleOnly.Name, maleOnly.Base, (object?)maleOnly.Female));
        Assert.Equal("red-spot-carcharo/red-spot", red.Key(m.Id));
    }

    [Theory]
    [InlineData("""{ "id": "x", "species": "S", "male": { "diffuse": "a.png" } }""", "\"id\"")]
    [InlineData("""{ "id": "ok-id", "male": { "diffuse": "a.png" } }""", "\"species\"")]
    [InlineData("""{ "id": "ok-id", "species": "S" }""", "\"male\" or \"female\"")]
    [InlineData("""{ "id": "ok-id", "species": "S", "male": { "colour": "a.png" } }""", "unknown slot \"colour\"")]
    [InlineData("""{ "id": "ok-id", "species": "S", "male": { "diffuse": 3 } }""", "file path")]
    public void Skin_problems_are_explained(string skin, string expected)
    {
        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse($$"""{ "format": 1, "id": "mod-id", "skins": [ {{skin}} ] }"""));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Two_skins_with_one_id_are_refused()
    {
        var skin = """{ "id": "same", "species": "S", "male": { "diffuse": "a.png" } }""";

        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse($$"""{ "format": 1, "id": "mod-id", "skins": [ {{skin}}, {{skin}} ] }"""));

        Assert.Contains("same", ex.Message);
    }

    [Fact]
    public void Written_skins_read_back_the_same()
    {
        var m = ModManifest.Parse(WithSkins);

        Assert.Equal(m.ToJson(), ModManifest.Parse(m.ToJson()).ToJson());
    }

    [Fact]
    public void Slots_map_to_the_shader_properties_and_kinds()
    {
        Assert.Equal(10, SkinSlotNames.All.Count);
        Assert.Equal(("_AdultDiffuse", SlotKind.Color), (SkinSlotNames.Property("diffuse"), SkinSlotNames.KindOf("diffuse")));
        Assert.Equal(("_InfantNormal", SlotKind.Normal), (SkinSlotNames.Property("infantNormal"), SkinSlotNames.KindOf("infantNormal")));
        Assert.Equal(("_AdultPatternMask", SlotKind.Data), (SkinSlotNames.Property("pattern"), SkinSlotNames.KindOf("pattern")));
        Assert.Equal("_InfantFurMask", SkinSlotNames.Property("infantFur"));
        Assert.Equal("_InfantExtraMap", SkinSlotNames.Property("infantExtra"));
        Assert.Null(SkinSlotNames.Canonical("colour"));
    }

    [Fact]
    public void The_framework_version_is_0_3_0()
    {
        Assert.Equal("0.3.0", FrameworkInfo.Version);
    }
}
