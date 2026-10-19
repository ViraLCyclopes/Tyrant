using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaterialSettingsTests
{
    private const string SkinJson = """
        { "format": 1, "id": "mod-id", "skins": [ { "id": "rainbow", "species": "S", "male": { "diffuse": "a.png" } } ] }
        """;

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));

    private static void WriteFile(string dir, string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, relative))!);
        File.WriteAllText(Path.Combine(dir, relative), text);
    }

    /// <summary>The "rainbow" skin with this materials.json map, loaded from its folder; the problems are what Load reported.</summary>
    private static (ModManifest Manifest, List<string> Problems) WithMaterials(string materials)
    {
        var dir = TempDir();
        WriteFile(dir, "materials/skins/rainbow/materials.json", "{ \"format\": 1, \"materials\": " + materials + " }");
        var manifest = ModManifest.Parse(SkinJson);
        var problems = new List<string>();
        MaterialFolders.Load(manifest, dir, problems);
        return (manifest, problems);
    }

    private static MaterialOverride Titanoboa(string materials) => WithMaterials(materials).Manifest.Skins[0].Materials!["Titanoboa"];

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
    public void A_droop_direction_is_written_with_exactly_three_numbers_and_reads_back()
    {
        var m = new MaterialOverride();
        m.Settings["_DroopDirection"] = MaterialValue.OfVector(0, -10.3f, 8.11f);
        m.Settings["_DetailScale"] = MaterialValue.OfVector(4, 4);
        var json = Json.Write(MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride> { ["Titanoboa"] = m }));

        Assert.Contains("[0,-10.3,8.11]", string.Concat(json.Where(c => !char.IsWhiteSpace(c))));
        var read = MaterialOverrides.Parse(Json.Parse(json), "Skin", true)!["Titanoboa"];
        Assert.Equal(new[] { 0f, -10.3f, 8.11f, 0f }, read.Settings["_DroopDirection"].Vector);
    }

    [Fact]
    public void Fur_grow_reads_and_writes_back_on_its_own_or_beside_a_mask()
    {
        var grow = Titanoboa("""{ "Titanoboa": { "fur": { "grow": true } } }""").Fur!;
        Assert.True(grow.Grow);
        Assert.Null(grow.Mask);
        Assert.False(grow.IsEmpty);

        var off = Titanoboa("""{ "Titanoboa": { "fur": { "grow": false, "mask": "m.png" } } }""").Fur!;
        Assert.False(off.Grow); // false is kept: it says "do not force fur" over a lower layer's true
        Assert.Equal("m.png", off.Mask!.File);
        Assert.Null(Titanoboa("""{ "Titanoboa": { "fur": { "mask": "m.png" } } }""").Fur!.Grow); // left out: the game decides

        var json = Json.Write(MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride> { ["Titanoboa"] = new() { Fur = new FurMasks { Grow = true } } }));
        Assert.Contains("\"grow\":true", string.Concat(json.Where(c => !char.IsWhiteSpace(c))));
        Assert.True(MaterialOverrides.Parse(Json.Parse(json), "Skin", false)!["Titanoboa"].Fur!.Grow);
        Assert.DoesNotContain("grow", Json.Write(MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride>
            { ["Titanoboa"] = new() { Fur = new FurMasks { Mask = new MaskSource { File = "m.png" } } } })));
    }

    [Fact]
    public void Game_ramp_mask_and_fur_masks_read_and_write_back_at_any_level()
    {
        const string Block = """{ "Titanoboa": { "iridescence": { "ramp": "game", "mask": "game" }, "fur": { "mask": "game", "infantMask": "game" } } }""";
        var m = Titanoboa(Block);
        Assert.True(m.Iridescence!.Ramp!.Game);
        Assert.Null(m.Iridescence.Ramp.File);
        Assert.True(m.Iridescence.Mask!.Game);
        Assert.Null(m.Iridescence.Mask.File);
        Assert.True(m.Fur!.Mask!.Game);
        Assert.True(m.Fur.InfantMask!.Game);

        var json = Json.Write(MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride> { ["Titanoboa"] = m }));
        Assert.Equal(Json.Write(Json.Parse(Block)), json);
        var species = MaterialOverrides.Parse(Json.Parse(Block), "Species", skinLevel: false)!["Titanoboa"]; // a species may say "game" too
        Assert.True(species.Iridescence!.Mask!.Game);
    }

    [Fact]
    public void Species_ramp_and_mask_read_and_write_back_at_any_level()
    {
        const string Block = """{ "Titanoboa": { "iridescence": { "ramp": { "species": "Microraptor" }, "mask": { "species": "Velociraptor" } } } }""";
        var m = Titanoboa(Block);
        Assert.Equal("Microraptor", m.Iridescence!.Ramp!.Species);
        Assert.Null(m.Iridescence.Ramp.File);
        Assert.False(m.Iridescence.Ramp.Game);
        Assert.Equal("Velociraptor", m.Iridescence.Mask!.Species);
        Assert.Null(m.Iridescence.Mask.From);

        var json = Json.Write(MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride> { ["Titanoboa"] = m }));
        Assert.Equal(Json.Write(Json.Parse(Block)), json);
        var species = MaterialOverrides.Parse(Json.Parse(Block), "Species", skinLevel: false)!["Titanoboa"]; // valid on a species too
        Assert.Equal("Velociraptor", species.Iridescence!.Mask!.Species);
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
    [InlineData("""{ "Titanoboa": { "fur": { "grow": "yes" } } }""", "\"materials.Titanoboa.fur.grow\" must be true or false")]
    [InlineData("""{ "Titanoboa": { "fur": { "length": 1 } } }""", "use mask, infantMask, grow")]
    [InlineData("""{ "Titanoboa": { "fur": { "mask": "everywhere" } } }""", "\"materials.Titanoboa.fur.mask\" must be an image file path or \"game\"")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "ramp": { "species": "" } } } }""", "\"materials.Titanoboa.iridescence.ramp.species\" must be a species id")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "ramp": { "species": 3 } } } }""", "\"materials.Titanoboa.iridescence.ramp.species\" must be a species id")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "ramp": { "species": "Microraptor", "stops": ["#ffffff"] } } } }""", "needs exactly one of \"stops\", \"thinFilm\" or \"species\"")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "ramp": { } } } }""", "needs exactly one of \"stops\", \"thinFilm\" or \"species\"")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "mask": { "species": " " } } } }""", "\"materials.Titanoboa.iridescence.mask.species\" must be a species id")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "mask": { "species": "Microraptor", "channel": "g" } } } }""", "\"species\" alone, or \"from\"")]
    [InlineData("""{ "Titanoboa": { "iridescence": { "mask": { } } } }""", "needs \"from\" (extra, pattern or fur) or \"species\"")]
    public void Material_problems_are_explained(string materials, string expected)
    {
        var (manifest, problems) = WithMaterials(materials);

        var problem = Assert.Single(problems);
        Assert.Contains(expected, problem);
        Assert.Contains("rainbow", problem);
        Assert.StartsWith("materials/skins/rainbow/materials.json: ", problem);
        Assert.Null(manifest.Skins[0].Materials);
    }

    [Fact]
    public void A_species_file_has_no_mask_from_a_slot()
    {
        var dir = TempDir();
        WriteFile(dir, "materials/species/Titanoboa/materials.json",
            """{ "format": 1, "species": "Titanoboa", "materials": { "Titanoboa": { "iridescence": { "mask": { "from": "extra" } } } } }""");
        var manifest = ModManifest.Parse("""{ "format": 1, "id": "mod-id" }""");
        var problems = new List<string>();
        MaterialFolders.Load(manifest, dir, problems);
        Assert.Contains("works only on a skin", Assert.Single(problems));

        WriteFile(dir, "materials/species/Titanoboa/materials.json",
            """{ "format": 1, "species": "Titanoboa", "materials": { "Titanoboa": { "settings": { "_Smoothness": 0.5 } } } }""");
        problems.Clear();
        MaterialFolders.Load(manifest, dir, problems);
        Assert.Empty(problems);
        Assert.Empty(manifest.Models);
        Assert.Equal(0.5f, manifest.SpeciesMaterials["Titanoboa"]["Titanoboa"].Settings["_Smoothness"].Number);
    }

    [Fact]
    public void Written_materials_read_back_the_same_and_empty_blocks_write_nothing()
    {
        var (manifest, problems) = WithMaterials("""
            { "Titanoboa": { "iridescence": { "strength": { "normal": 0.35, "leucistic": 0.1 }, "ramp": { "thinFilm": { "thickness": 420, "index": 1.8 } },
                "mask": { "from": "pattern", "channel": "b", "invert": true }, "offset": [0.1, 0.2], "bias": 1.3 },
              "settings": { "_Smoothness": 0.55, "_FresnelColor": "#40c0ff", "_DetailScale": [4, 4, 1] } },
              "Empty": { "settings": {} } }
            """);
        Assert.Empty(problems);

        var dir = TempDir();
        MaterialFolders.Save(manifest, dir);
        var file = Path.Combine(dir, "materials", "skins", "rainbow", "materials.json");
        var json = File.ReadAllText(file);
        var again = ModManifest.Parse(SkinJson);
        MaterialFolders.Load(again, dir, problems);
        MaterialFolders.Save(again, dir);

        Assert.Empty(problems);
        Assert.Equal(json, File.ReadAllText(file));
        Assert.DoesNotContain("Empty", json);
        Assert.DoesNotContain("materials", manifest.ToJson());
        Assert.Null(WithMaterials("""{ "Empty": {} }""").Manifest.Skins[0].Materials);
    }
}
