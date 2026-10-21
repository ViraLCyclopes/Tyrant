using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Dumper.Serialization;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModSizesTests
{
    private static readonly ModChecker Checker = new(_ => (4, 4));

    private static readonly IReadOnlyList<SpeciesSkins> Species =
    [
        new("Alpha", false, [new VanillaSkin(0, "Default", new Dictionary<string, string>(), new Dictionary<string, string>())]),
        new("Big", false, [new VanillaSkin(0, "Default", new Dictionary<string, string>(), new Dictionary<string, string>())]),
    ];

    /// <summary>Alpha (two skins, one with larger sexes) and Big (grows to 1.5), shaped like the real dump, and the global parameters.</summary>
    private static (FakeGame Game, Workspace Ws, ModProject Mod) Setup(bool parameters = true)
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(TestTemp.Root, Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        var result = new DumpResult();
        void Add(string type, string name, long id, string json) => result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo(type, name, id), json));
        Add("PrehistoricKingdom.AnimalData", "Alpha", 1, """
            {"$type":"PrehistoricKingdom.AnimalData","$name":"Alpha","$id":1,"speciesID":"Alpha","cost":100,
             "scaleGrowthCurve":{"keys":[{"time":0,"value":0.5,"inTangent":1,"outTangent":1},{"time":1,"value":1,"inTangent":1,"outTangent":1}]},
             "skinsData":[{"skinName":"Default","maleSizeMultiplier":1,"femaleSizeMultiplier":1},{"skinName":"Big","maleSizeMultiplier":1.2,"femaleSizeMultiplier":1.1}],
             "animalLengthHeightWidthRaw":{"x":4,"y":2,"z":1}}
            """);
        Add("PrehistoricKingdom.AnimalData", "Big", 2, """
            {"$type":"PrehistoricKingdom.AnimalData","$name":"Big","$id":2,"speciesID":"Big",
             "scaleGrowthCurve":{"keys":[{"time":0,"value":0.6,"inTangent":1,"outTangent":1},{"time":1,"value":1.5,"inTangent":1,"outTangent":1}]},
             "skinsData":[{"skinName":"Default","maleSizeMultiplier":1,"femaleSizeMultiplier":1}]}
            """);
        if (parameters)
            Add("PrehistoricKingdom.AnimalsV2.Data.GlobalAnimalParameters", "GlobalAnimalParameters", 3, """
                {"$type":"PrehistoricKingdom.AnimalsV2.Data.GlobalAnimalParameters","$name":"GlobalAnimalParameters","$id":3,
                 "incubationGeneticSizeMinMax":{"x":0.925,"y":1.075},"breedingGeneticSizeMinMax":{"x":0.85,"y":1.15}}
                """);
        DumpWriter.Write(ws.DataDir, result, [], new DumpManifest { RequestId = "r" });
        return (game, ws, ModProject.Create(ws, "size-pack", null, null));
    }

    private static ModCheckResult Check(Workspace ws, ModProject mod) =>
        Checker.Check(mod, null, Species, sizeFacts: SpeciesSizes.Facts(ws, mod));

    [Fact]
    public void SetSizes_writes_and_deletes()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;

        mod.SetSizes("Alpha", new SizeSettings { Adult = 1.2, Baby = 0.4 });
        mod.SetSizes("Big", new SizeSettings { Male = 1.1 });

        var again = ModProject.Open(ws, "size-pack");
        Assert.Equal(["Alpha", "Big"], again.SizeTargets);
        Assert.Equal(1.2, again.Sizes("alpha")!.Adult);
        Assert.Equal(0.4, again.Sizes("Alpha")!.Baby);
        Assert.Null(again.Sizes("Alpha")!.Male);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "sizes", "alpha.json")));
        Assert.DoesNotContain("Alpha", File.ReadAllText(Path.Combine(mod.Dir, "mod.json")));

        mod.SetSizes("Alpha", new SizeSettings()); // every value unset
        Assert.Null(mod.Sizes("Alpha"));
        Assert.False(File.Exists(Path.Combine(mod.Dir, "sizes", "alpha.json")));
        mod.SetSizes("Big", null);
        Assert.Empty(mod.SizeTargets);
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "sizes")));

        var bad = Assert.Throws<TyrantException>(() => mod.SetSizes("Alpha", new SizeSettings { Adult = 9 }));
        Assert.Equal(TyrantErrorCode.ModInvalid, bad.Code);
        Assert.Empty(mod.SizeTargets);

        mod.SetSizeRanges(new SizeRanges { Breeding = [0.9, 1.1] });
        Assert.Equal([0.9, 1.1], ModProject.Open(ws, "size-pack").Manifest.SizeRanges!.Breeding!);
        Assert.Throws<TyrantException>(() => mod.SetSizeRanges(new SizeRanges { Nursery = [0, 1] }));
        mod.SetSizeRanges(null);
        Assert.Null(ModProject.Open(ws, "size-pack").Manifest.SizeRanges);
    }

    [Fact]
    public void Original_reads_template_plus_field_edits()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var store = DataStore.Open(ws);

        var vanilla = SpeciesSizes.Original(store, "Alpha", null)!;
        Assert.Equal([(0f, 0.5f), (1f, 1f)], vanilla.Curve);
        Assert.Equal([(1.0, 1.0), (1.2, 1.1)], vanilla.Skins);
        Assert.Equal([4f, 2f, 1f], vanilla.LengthHeightWidth!);
        Assert.Null(SpeciesSizes.Original(store, "Missing", null));

        var entry = mod.AddSpecies(Species, "mini", "Alpha", "Mini", "Tyrantus", "mini");
        var key = entry.Key(mod.Id);
        Assert.Equal(vanilla.Curve, SpeciesSizes.Original(store, key, mod)!.Curve);
        entry.Fields.Add(new AnimalField { Path = "/scaleGrowthCurve/keys/0/value", Kind = "number", Value = "0.25", TemplateValue = "0.5", Editable = true });
        entry.Fields.Add(new AnimalField { Path = "/skinsData/1/femaleSizeMultiplier", Kind = "number", Value = "0.9", TemplateValue = "1.1", Editable = true });

        var custom = SpeciesSizes.Original(store, key, mod)!;
        Assert.Equal([(0f, 0.25f), (1f, 1f)], custom.Curve);
        Assert.Equal([(1.0, 1.0), (1.2, 0.9)], custom.Skins);
        Assert.Equal(0.5f, SpeciesSizes.Original(store, "Alpha", mod)!.Curve[0].Value); // the template itself is untouched
    }

    [Fact]
    public void Unknown_size_target_is_an_error()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod.SetSizes("Nope", new SizeSettings { Adult = 1.2 });
        mod.SetSizes("Alpha", new SizeSettings { Adult = 1.2 });

        var result = Check(ws, mod);

        var error = Assert.Single(result.Errors);
        Assert.Contains("sizes/nope.json", error);
        Assert.Contains("Nope", error);
    }

    [Fact]
    public void An_unreadable_size_file_is_an_error()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        Directory.CreateDirectory(Path.Combine(mod.Dir, "sizes"));
        File.WriteAllText(Path.Combine(mod.Dir, "sizes", "alpha.json"), """{"format":1,"target":"Alpha","adult":9}""");

        Assert.Contains(Check(ws, mod).Errors, e => e.Contains("sizes/alpha.json") && e.Contains("adult"));
    }

    [Fact]
    public void Extremes_over_cap_is_an_error()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod.SetSizes("Big", new SizeSettings { Adult = 1.5 }); // 1.5 x 1.5 x the random range is past 2

        var result = Check(ws, mod);

        Assert.Contains(result.Errors, e => e.Contains("Big") && e.Contains("0.05") && e.Contains("2"));

        mod.SetSizes("Big", new SizeSettings { Adult = 1.1 });
        Assert.DoesNotContain(Check(ws, mod).Errors, e => e.Contains("Big"));
    }

    [Fact]
    public void Extreme_adult_sizes_warn_about_the_size_class()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod.SetSizes("Alpha", new SizeSettings { Adult = 0.6 });
        mod.SetSizes("Big", new SizeSettings { Adult = 1.2 });

        var warnings = Check(ws, mod).Warnings;

        Assert.Contains(warnings, w => w.Contains("Alpha") && w.Contains("the species keeps its size class (paths, enclosure filters); a much bigger or smaller animal may not fit them"));
        Assert.DoesNotContain(warnings, w => w.Contains("size class") && w.Contains("Big"));
    }

    [Fact]
    public void Field_edit_on_size_paths_warns()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var entry = mod.AddSpecies(Species, "mini", "Alpha", "Mini", "Tyrantus", "mini");
        entry.Fields.Add(new AnimalField { Path = "/scaleGrowthCurve/keys/0/value", Kind = "number", Value = "0.25", TemplateValue = "0.5", Editable = true });
        entry.Fields.Add(new AnimalField { Path = "/skinsData/0/maleSizeMultiplier", Kind = "number", Value = "1", TemplateValue = "1", Editable = true }); // unchanged
        entry.Fields.Add(new AnimalField { Path = "/skinsData/1/femaleSizeMultiplier", Kind = "number", Value = "0.9", TemplateValue = "1.1", Editable = true });

        var warnings = Check(ws, mod).Warnings.Where(w => w.Contains("Set sizes in Size instead")).ToList();

        var warning = Assert.Single(warnings);
        Assert.Contains("mini", warning);
        Assert.Contains("/scaleGrowthCurve/keys/0/value", warning);
        Assert.Contains("/skinsData/1/femaleSizeMultiplier", warning);
        Assert.DoesNotContain("/skinsData/0/", warning);
    }

    [Fact]
    public void Size_paths_hidden_from_field_editor()
    {
        var (game, ws, _) = Setup();
        using var __ = game;

        var fields = SpeciesDatabase.Template(ws, "Alpha")!.Fields;

        Assert.DoesNotContain(fields, f => SpeciesSizes.IsSizePath(f.Path));
        Assert.DoesNotContain(fields, f => f.Path.Contains("scaleGrowthCurve") || f.Path.Contains("SizeMultiplier"));
        Assert.Contains(fields, f => f.Path == "/animalLengthHeightWidthRaw/x");
        Assert.Contains(fields, f => f.Path == "/skinsData/1/skinName");
    }

    [Fact]
    public void Ranges_warn_for_vanilla_species_past_cap()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        Assert.DoesNotContain(Check(ws, mod).Warnings, w => w.Contains("genetic size"));

        mod.SetSizeRanges(new SizeRanges { Breeding = [0.5, 1.5] }); // Big: 1.5 x 1.5 = 2.25
        var warnings = Check(ws, mod).Warnings.Where(w => w.Contains("genetic size")).ToList();

        var warning = Assert.Single(warnings);
        Assert.Contains("Big", warning);
        Assert.DoesNotContain("Alpha", warning);

        mod.SetSizes("Big", new SizeSettings { Adult = 0.5 }); // sized species are judged by their own error, not this warning
        Assert.DoesNotContain(Check(ws, mod).Warnings, w => w.Contains("genetic size"));
    }

    [Fact]
    public void Facts_fall_back_to_the_vanilla_ranges_when_the_dump_has_none()
    {
        var (game, ws, mod) = Setup(parameters: false);
        using var _ = game;

        var facts = SpeciesSizes.Facts(ws, mod)!;

        Assert.Equal((0.925, 1.075), facts.Incubation);
        Assert.Equal((0.85, 1.15), facts.Breeding);
    }
}
