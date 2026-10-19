using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaterialSettingsTests
{
    private static ModManifest WithMaterials(string materials) => ModManifest.Parse($$"""
        { "format": 1, "id": "mod-id", "skins": [ { "id": "rainbow", "species": "S", "male": { "diffuse": "a.png" }, "materials": {{materials}} } ] }
        """);

    private static MaterialOverride Titanoboa(string materials) => WithMaterials(materials).Skins[0].Materials!["Titanoboa"];

    [Fact]
    public void Reads_iridescence_and_settings()
    {
        var m = Titanoboa("""
            { "Titanoboa": { "iridescence": { "strength": { "normal": 0.35, "albino": 0.2 }, "variation": 0.1,
                "ramp": { "thinFilm": { "thickness": 420 } }, "mask": { "from": "extra", "channel": "g", "invert": true },
                "tiling": [2, 2], "scale": -0.25, "power": -0.3, "bias": 1.3 },
              "settings": { "_Smoothness": 0.55, "_FresnelColor": "#40c0ff", "_DetailScale": [4, 4] } } }
            """);
        var i = m.Iridescence!;

        Assert.Equal(0.35f, i.Strength!.For(0));
        Assert.Equal(0.2f, i.Strength.For(1));
        Assert.Equal(0.35f, i.Strength.For(2));
        Assert.Equal(0.1f, i.Variation);
        Assert.Equal(420, i.Ramp!.ThinFilm!.Thickness);
        Assert.Equal(1.56, i.Ramp.ThinFilm.Index);
        Assert.Equal(("extra", 'g', true), (i.Mask!.From, i.Mask.Channel, i.Mask.Invert));
        Assert.Equal(new[] { 2f, 2f }, i.Tiling);
        Assert.Equal(-0.25f, i.Scale);
        Assert.Equal(0.55f, m.Settings["_Smoothness"].Number);
        Assert.Equal("#40c0ff", m.Settings["_FresnelColor"].Color.ToString());
        Assert.Equal(new[] { 4f, 4f, 0f, 0f }, m.Settings["_DetailScale"].Vector);
    }

    [Fact]
    public void A_single_strength_applies_to_every_variant()
    {
        var s = Titanoboa("""{ "Titanoboa": { "iridescence": { "strength": 0.3 } } }""").Iridescence!.Strength!;

        Assert.All(new[] { 0, 1, 2, 3 }, t => Assert.Equal(0.3f, s.For(t)));
    }

    [Fact]
    public void Ramp_forms_are_a_file_stops_or_thin_film()
    {
        Assert.Equal("materials/r.png", Titanoboa("""{ "Titanoboa": { "iridescence": { "ramp": "materials/r.png" } } }""").Iridescence!.Ramp!.File);
        Assert.Equal(2, Titanoboa("""{ "Titanoboa": { "iridescence": { "ramp": { "stops": ["#ff0000", "#0000ff"] } } } }""").Iridescence!.Ramp!.Stops!.Count);
    }

    [Fact]
    public void Mask_forms_are_everywhere_a_file_or_a_channel()
    {
        Assert.True(Titanoboa("""{ "Titanoboa": { "iridescence": { "mask": "everywhere" } } }""").Iridescence!.Mask!.Everywhere);
        Assert.Equal("m.png", Titanoboa("""{ "Titanoboa": { "iridescence": { "mask": "m.png" } } }""").Iridescence!.Mask!.File);
        var channel = Titanoboa("""{ "Titanoboa": { "iridescence": { "mask": { "from": "fur" } } } }""").Iridescence!.Mask!;
        Assert.Equal(("fur", 'r', false), (channel.From, channel.Channel, channel.Invert));
    }

    [Theory]
    [InlineData("""{ "Titanoboa": { "glow": 1 } }""", "unknown key \"materials.Titanoboa.glow\"")]
    [InlineData("""{ "Titanoboa": { "settings": { "_Shine": 1 } } }""", "\"_Shine\" is not a setting Tyrant can change")]
    [InlineData("""{ "Titanoboa": { "settings": { "_Smoothness": 2 } } }""", "from 0 to 1")]
    [InlineData("""{ "Titanoboa": { "settings": { "_FresnelColor": 3 } } }""", "colour like \"#3060ff\"")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "strength": 1.5 } } }""", "from 0 to 1")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "strength": { "shiny": 0.2 } } } }""", "normal, albino, melanistic, leucistic")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "variation": 2 } } }""", "from 0 to 1")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "ramp": { "stops": [] } } } }""", "1 to 32 colours")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "ramp": { "thinFilm": { "thickness": -5 } } } } }""", "from 0 to 2000")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "mask": { "from": "normal" } } } }""", "extra, pattern or fur")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "mask": { "from": "extra", "channel": "x" } } } }""", "r, g, b or a")]
    public void Material_problems_are_explained(string materials, string expected)
    {
        var ex = Assert.Throws<ManifestException>(() => WithMaterials(materials));

        Assert.Contains(expected, ex.Message);
        Assert.Contains("rainbow", ex.Message);
    }

    [Fact]
    public void A_species_entry_has_no_mask_from_a_slot()
    {
        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse("""
            { "format": 1, "id": "mod-id", "models": [ { "target": "Titanoboa", "materials": { "Titanoboa": { "iridescence": { "mask": { "from": "extra" } } } } } ] }
            """));
        Assert.Contains("works only on a skin", ex.Message);

        var entry = ModManifest.Parse("""
            { "format": 1, "id": "mod-id", "models": [ { "target": "Titanoboa", "materials": { "Titanoboa": { "settings": { "_Smoothness": 0.5 } } } } ] }
            """).Models[0];
        Assert.Equal("", entry.File);
        Assert.Null(entry.Rig);
        Assert.False(entry.HasModelOrRig);
        Assert.NotNull(entry.Materials);
    }

    [Fact]
    public void Written_materials_read_back_the_same_and_empty_blocks_write_nothing()
    {
        var m = WithMaterials("""
            { "Titanoboa": { "iridescence": { "strength": { "normal": 0.35, "leucistic": 0.1 }, "ramp": { "thinFilm": { "thickness": 420, "index": 1.8 } },
                "mask": { "from": "pattern", "channel": "b", "invert": true }, "offset": [0.1, 0.2], "bias": 1.3 },
              "settings": { "_Smoothness": 0.55, "_FresnelColor": "#40c0ff", "_DetailScale": [4, 4, 1] } },
              "Empty": { "settings": {} } }
            """);

        var json = m.ToJson();

        Assert.Equal(json, ModManifest.Parse(json).ToJson());
        Assert.DoesNotContain("Empty", json);
        Assert.DoesNotContain("materials", ModManifest.Parse("""{ "format": 1, "id": "mod-id", "skins": [ { "id": "plain", "species": "S", "male": { "diffuse": "a.png" } } ] }""").ToJson());
        Assert.Null(WithMaterials("""{ "Empty": {} }""").Skins[0].Materials);
    }
}
