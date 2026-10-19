using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaterialTableTests
{
    private static LoadedMod Mod(string id, string json, params (string Path, string Json)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), id);
        Directory.CreateDirectory(dir);
        foreach (var (path, content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, path))!);
            File.WriteAllText(Path.Combine(dir, path), content);
        }
        var manifest = ModManifest.Parse(json);
        MaterialFolders.Load(manifest, dir, new List<string>());
        return new LoadedMod(manifest, dir);
    }

    private static string Skin(string id) =>
        "{\"id\":\"" + id + "\",\"species\":\"Titanoboa\",\"name\":\"S\",\"base\":\"0\",\"male\":{\"diffuse\":\"a.png\"}}";

    private static (string, string) SkinFile(string id, string materials) =>
        ($"materials/skins/{id}/materials.json", "{\"format\":1,\"materials\":" + materials + "}");

    private static (string, string) SpeciesFile(string species, string materials) =>
        ($"materials/species/{species}/materials.json", "{\"format\":1,\"species\":\"" + species + "\",\"materials\":" + materials + "}");

    private static LoadedMod Species(string mod, string material, string smoothness, string target = "Titanoboa") =>
        Mod(mod, "{\"format\":1,\"id\":\"" + mod + "\"}",
            SpeciesFile(target, "{\"" + material + "\":{\"settings\":{\"_Smoothness\":" + smoothness + "}}}"));

    [Fact]
    public void Skin_beats_base_skin_beats_species_per_field()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("start") + "," + Skin("mine") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"iridescence":{"ramp":{"stops":["#ff0000","#0000ff"]},"scale":-0.25},"settings":{"_Smoothness":0.3}}}"""),
            SkinFile("start", """{"Titanoboa":{"iridescence":{"power":-0.3}}}"""),
            SkinFile("mine", """{"Titanoboa":{"iridescence":{"strength":0.35},"settings":{"_RimIntensity":1}}}"""));
        var table = MaterialTable.Build([a], out var messages);

        Assert.Empty(messages);
        Assert.True(table.HasAny);
        var merged = table.For("Titanoboa", "mod-a/mine", "mod-a/start", "Titanoboa")!;
        Assert.Equal(0.35f, merged.Iridescence!.Strength!.Normal);
        Assert.Equal(2, merged.Iridescence.Ramp!.Stops!.Count);
        Assert.Equal(-0.3f, merged.Iridescence.Power);
        Assert.Equal(-0.25f, merged.Iridescence.Scale);
        Assert.Equal(0.3f, merged.Settings["_Smoothness"].Number);
        Assert.Equal(1f, merged.Settings["_RimIntensity"].Number);
        Assert.Null(table.For("Titanoboa", "mod-a/mine", "mod-a/start", "Other"));
        Assert.Null(table.For("Other", null, null, "Titanoboa"));
    }

    [Fact]
    public void Files_resolve_against_their_own_mod()
    {
        var a = Mod("mod-a", """{"format":1,"id":"mod-a"}""", SpeciesFile("Titanoboa", """{"M":{"iridescence":{"mask":"m.png"}}}"""));
        var b = Mod("mod-b", "{\"format\":1,\"id\":\"mod-b\",\"skins\":[" + Skin("sss") + "]}",
            SkinFile("sss", """{"M":{"iridescence":{"ramp":"r.png"}},"N":{"iridescence":{"ramp":"../x.png"}}}"""));
        var table = MaterialTable.Build([a, b], out var messages);

        var merged = table.For("Titanoboa", "mod-b/sss", null, "M")!;
        Assert.Equal(Path.GetFullPath(Path.Combine(a.Directory, "m.png")), merged.Iridescence!.Mask!.File);
        Assert.Equal(Path.GetFullPath(Path.Combine(b.Directory, "r.png")), merged.Iridescence.Ramp!.File);
        Assert.Null(table.For("Titanoboa", "mod-b/sss", null, "N")?.Iridescence?.Ramp);
        Assert.Contains("mod-b: ../x.png points outside the mod folder; it is ignored.", messages);
        Assert.Equal("r.png", b.Manifest.Skins[0].Materials!["M"].Iridescence!.Ramp!.File); // the manifest is untouched
    }

    [Fact]
    public void The_later_mod_wins_a_species_material_and_the_clash_is_reported()
    {
        var a = Species("mod-a", "Titanoboa", "0.1");
        var b = Species("mod-b", "Titanoboa", "0.9");
        var table = MaterialTable.Build([a, b], out var messages);

        Assert.Equal(0.9f, table.For("Titanoboa", null, null, "Titanoboa")!.Settings["_Smoothness"].Number);
        var message = Assert.Single(messages);
        Assert.Contains("'mod-a'", message);
        Assert.Contains("'mod-b'", message);
        var clash = Assert.Single(MaterialTable.Clashes([("mod-a", a.Manifest), ("mod-b", b.Manifest)]));
        Assert.Equal(("mod-a", "mod-b", "Titanoboa", "Titanoboa"), (clash.Loser, clash.Winner, clash.Species, clash.Material));
        Assert.Equal("Titanoboa: its Titanoboa material settings are overridden by 'mod-b' (later in the load order).", clash.Message);

        var otherMaterial = Species("mod-c", "Eyes", "0.5");
        Assert.Empty(MaterialTable.Clashes([("mod-a", a.Manifest), ("mod-c", otherMaterial.Manifest)]));
        var otherSpecies = Species("mod-d", "Titanoboa", "0.5", "Other");
        Assert.Empty(MaterialTable.Clashes([("mod-a", a.Manifest), ("mod-d", otherSpecies.Manifest)]));
    }

    [Fact]
    public void Merge_does_not_change_its_inputs_and_variation_defaults()
    {
        var top = new MaterialOverride { Iridescence = new IridescenceSettings { Scale = 1f } };
        var low = new MaterialOverride { Iridescence = new IridescenceSettings { Strength = new VariantNumbers { Normal = 0.5f } } };
        var merged = MaterialTable.Merge(top, null, low)!;
        Assert.Equal(1f, merged.Iridescence!.Scale);
        Assert.Equal(0.5f, merged.Iridescence.Strength!.Normal);
        Assert.Null(top.Iridescence.Strength);
        Assert.Equal(IridescenceSettings.DefaultVariation, MaterialTable.EffectiveVariation(merged.Iridescence));
        Assert.Equal(0f, MaterialTable.EffectiveVariation(top.Iridescence));
        Assert.Null(MaterialTable.Merge(null, null));
    }

    [Fact]
    public void Merging_nothing_and_looking_up_an_animal_no_mod_covers_allocate_nothing()
    {
        // Every growth tick of every animal asks: the answer for the animals no mod covers must cost no garbage.
        var table = MaterialTable.Build([Species("mod-a", "Titanoboa", "0.1")], out _);
        var none = new MaterialOverride?[3];
        MaterialTable.Merge(none);
        table.For("Other", null, null, "Titanoboa");
        table.For("Titanoboa", null, null, "Eyes");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var merged = MaterialTable.Merge(none);
        var other = table.For("Other", null, null, "Titanoboa");
        var otherMaterial = table.For("Titanoboa", null, null, "Eyes");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Null(merged);
        Assert.Null(other);
        Assert.Null(otherMaterial);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void The_table_says_which_species_and_skins_it_covers_before_any_material_is_read()
    {
        var a = Mod("mod-a", "{\"format\":1,\"id\":\"mod-a\",\"skins\":[" + Skin("mine") + "," + Skin("bare") + "]}",
            SpeciesFile("Titanoboa", """{"Titanoboa":{"settings":{"_Smoothness":0.2}}}"""),
            SkinFile("mine", """{"Titanoboa":{"iridescence":{"strength":0.35}}}"""), SkinFile("bare", "{}"));
        var table = MaterialTable.Build([a], out _);

        Assert.True(table.CoversSpecies("Titanoboa"));
        Assert.False(table.CoversSpecies("Other"));
        Assert.False(table.CoversSpecies(null));
        Assert.True(table.CoversSkin("mod-a", "mine"));
        Assert.False(table.CoversSkin("mod-a", "bare"));
        Assert.False(table.CoversSkin("mod-b", "mine"));
    }
}
