using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class StartingSkinManifestTests
{
    private const string Diffuse = """ "male": { "diffuse": "skins/x/male_D.png" } """;

    private static string Skin(string id, string species, string extra = "", string baseSkin = "0") =>
        $$"""{ "id": "{{id}}", "species": "{{species}}", "name": "{{id}}", "base": "{{baseSkin}}", {{Diffuse}} {{extra}} }""";

    private static ModManifest Manifest(params string[] skins)
    {
        var manifest = ModManifest.Parse($$"""{ "format": 1, "id": "pack", "skins": [{{string.Join(",", skins)}}] }""");
        manifest.Species.Add(new SpeciesEntry { Id = "mira", Base = "Allosaurus Anax", Name = "Mira", Genus = "Mira", Epithet = "bilis" });
        return manifest;
    }

    [Fact]
    public void Starting_and_copied_from_round_trip()
    {
        var m = ModManifest.Parse($$"""{ "format": 1, "id": "pack", "skins": [{{Skin("mira-start", "tyrant.pack.mira",
            """, "starting": true, "copiedFrom": { "species": "Carcharodontosaurus", "skin": 1 } """)}}, {{Skin("plain", "tyrant.pack.mira")}}] }""");

        var again = ModManifest.Parse(m.ToJson());

        var start = again.Skins[0];
        Assert.True(start.Starting);
        Assert.Equal(("Carcharodontosaurus", 1), (start.CopiedFrom!.Species, start.CopiedFrom.Skin));
        Assert.False(again.Skins[1].Starting);
        Assert.Null(again.Skins[1].CopiedFrom);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(m.ToJson(), "\"starting\""));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(m.ToJson(), "\"copiedFrom\""));
    }

    [Theory]
    [InlineData(""", "starting": "yes" """, "\"starting\"")]
    [InlineData(""", "copiedFrom": { "skin": 1 } """, "\"copiedFrom\"")]
    [InlineData(""", "copiedFrom": { "species": "Carcharodontosaurus", "skin": -1 } """, "\"copiedFrom\"")]
    public void Bad_starting_fields_are_refused(string extra, string message)
    {
        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse($$"""{ "format": 1, "id": "pack", "skins": [{{Skin("a-skin", "tyrant.pack.mira", extra)}}] }"""));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Problems_flag_other_species_second_starting_and_non_zero_base()
    {
        const string starting = """, "starting": true""";
        var m = Manifest(Skin("skin-a", "tyrant.pack.mira", starting), Skin("skin-b", "tyrant.pack.mira", starting),
            Skin("skin-c", "tyrant.other.x", starting), Skin("skin-d", "Carcharodontosaurus", starting), Skin("skin-e", "tyrant.pack.mira", starting, baseSkin: "1"),
            Skin("skin-f", "tyrant.pack.mira"));

        var problems = StartingSkins.Problems(m);

        Assert.Equal(new[] { "skin-b", "skin-c", "skin-d", "skin-e" }, problems.Select(p => p.Skin.Id));
        Assert.Contains("already has a starting skin", problems[0].Problem);
        Assert.Contains("not one of this mod's species", problems[1].Problem);
        Assert.Contains("not one of this mod's species", problems[2].Problem);
        Assert.Contains("base must be 0", problems[3].Problem);
        Assert.Equal("skin-a", StartingSkins.Of(m, "tyrant.pack.mira")?.Id);
        Assert.Null(StartingSkins.Of(m, "tyrant.other.x"));
    }

    [Fact]
    public void Inherits_from_the_starting_skin_only_with_base_zero_on_that_species()
    {
        var m = Manifest(Skin("skin-a", "tyrant.pack.mira", """, "starting": true"""), Skin("by-number", "tyrant.pack.mira"),
            Skin("by-name", "tyrant.pack.mira", baseSkin: "SKIN-A"), Skin("other-base", "tyrant.pack.mira", baseSkin: "1"));

        Assert.True(StartingSkins.InheritsFrom(m, m.Skins[1], out var starting));
        Assert.Equal("skin-a", starting.Id);
        Assert.True(StartingSkins.InheritsFrom(m, m.Skins[2], out _));
        Assert.False(StartingSkins.InheritsFrom(m, m.Skins[3], out _));
        Assert.False(StartingSkins.InheritsFrom(m, m.Skins[0], out _));
    }

    [Fact]
    public void The_game_ignores_a_bad_starting_flag_and_says_so()
    {
        var root = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "Mods");
        var dir = Path.Combine(root, "pack");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mod.json"), $$"""{ "format": 1, "id": "pack", "skins": [{{Skin("stray", "Carcharodontosaurus", """, "starting": true""")}}] }""");

        var plan = ModCatalog.Plan(ModCatalog.Discover(root), null, "0.1.0");

        var skin = Assert.Single(Assert.Single(plan.Mods).Manifest.Skins);
        Assert.False(skin.Starting); // still loads, as an added skin
        Assert.Contains(plan.Warnings, w => w.Contains("pack: skin \"stray\" is not used as a starting skin"));
    }
}
