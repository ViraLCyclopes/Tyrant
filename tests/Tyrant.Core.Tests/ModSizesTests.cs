using System.Text.Json;
using Tyrant.Core.Blender;
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
        new("Rex", false, [new VanillaSkin(0, "Default", new Dictionary<string, string>(), new Dictionary<string, string>())]),
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
        // Shaped like a T. rex (its newborn an eighth of the adult, its sexes a little under 1): adult 2.63 reaches 2.96.
        Add("PrehistoricKingdom.AnimalData", "Rex", 4, """
            {"$type":"PrehistoricKingdom.AnimalData","$name":"Rex","$id":4,"speciesID":"Rex","displayName":"Tyrannosaurus",
             "scaleGrowthCurve":{"keys":[{"time":0,"value":0.12,"inTangent":0.88,"outTangent":0.88},{"time":1,"value":1,"inTangent":0.88,"outTangent":0.88}]},
             "skinsData":[{"skinName":"Default","maleSizeMultiplier":0.95,"femaleSizeMultiplier":0.98}]}
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

        var bad = Assert.Throws<TyrantException>(() => mod.SetSizes("Alpha", new SizeSettings { Adult = 21 }));
        Assert.Equal(TyrantErrorCode.ModInvalid, bad.Code);
        Assert.Empty(mod.SizeTargets);

        // The ranges are the species' own, in the same file; one alone keeps the file. A limit is never written: it is automatic.
        mod.SetSizes("Alpha", new SizeSettings { Nursery = [0.5, 3], Breeding = [0.9, 1.1], Limit = [0.05, 5] });
        var ranged = ModProject.Open(ws, "size-pack").Sizes("Alpha")!;
        Assert.Equal([0.5, 3], ranged.Nursery!);
        Assert.Equal([0.9, 1.1], ranged.Breeding!);
        Assert.Null(ranged.Limit);
        Assert.Null(ranged.Adult);
        Assert.DoesNotContain("sizeRanges", File.ReadAllText(Path.Combine(mod.Dir, "mod.json")));
        mod.SetSizes("Alpha", new SizeSettings { Limit = [0.05, 5] }); // a limit alone is nothing: the file goes
        Assert.Null(mod.Sizes("Alpha"));
        var range = Assert.Throws<TyrantException>(() => mod.SetSizes("Alpha", new SizeSettings { Nursery = [0, 1] }));
        Assert.Equal(TyrantErrorCode.ModInvalid, range.Code);
        Assert.Contains("Alpha: \"nursery\" must be [min, max] with 0 < min <= max <= 20", range.Message);
        Assert.Null(mod.Sizes("Alpha")); // a refused change writes nothing
    }

    [Fact]
    public void SizeRanges_in_mod_json_is_an_error_that_says_where_it_moved()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var path = Path.Combine(mod.Dir, "mod.json");
        File.WriteAllText(path, File.ReadAllText(path).TrimEnd().TrimEnd('}') + ",\"sizeRanges\":{\"limit\":[0.05,20]}}");

        var ex = Assert.Throws<TyrantException>(() => ModProject.Open(ws, "size-pack"));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains("\"sizeRanges\" moved to each species' Size card (sizes/<species>.json); remove it from mod.json.", ex.Message);
    }

    [Fact]
    public void Original_reads_template_plus_field_edits()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var store = DataStore.Open(ws);

        var vanilla = SpeciesSizes.Original(store, "Alpha", null)!;
        Assert.Equal(SizeMath.CurveSamples, vanilla.Curve.Length); // the curve sampled as the game reads it (see Original_curve_is_read_as_the_game_reads_it)
        Assert.Equal((0f, 0.5f), vanilla.Curve[0]);
        Assert.Equal((1f, 1f), vanilla.Curve[^1]);
        Assert.Equal([(1.0, 1.0), (1.2, 1.1)], vanilla.Skins);
        Assert.Equal([4f, 2f, 1f], vanilla.LengthHeightWidth!);
        Assert.Null(SpeciesSizes.Original(store, "Missing", null));

        var entry = mod.AddSpecies(Species, "mini", "Alpha", "Mini", "Tyrantus", "mini");
        var key = entry.Key(mod.Id);
        Assert.Equal(vanilla.Curve, SpeciesSizes.Original(store, key, mod)!.Curve);
        entry.Fields.Add(new AnimalField { Path = "/scaleGrowthCurve/keys/0/value", Kind = "number", Value = "0.25", TemplateValue = "0.5", Editable = true });
        entry.Fields.Add(new AnimalField { Path = "/skinsData/1/femaleSizeMultiplier", Kind = "number", Value = "0.9", TemplateValue = "1.1", Editable = true });

        var custom = SpeciesSizes.Original(store, key, mod)!;
        Assert.Equal((0f, 0.25f), custom.Curve[0]);
        Assert.Equal((1f, 1f), custom.Curve[^1]);
        Assert.Equal([(1.0, 1.0), (1.2, 0.9)], custom.Skins);
        Assert.Equal(0.5f, SpeciesSizes.Original(store, "Alpha", mod)!.Curve[0].Value); // the template itself is untouched
    }

    // The dumped growth curves of two species, tangents and all: Hermite curves that lines between their keys misread by up to 25%.
    private const string VelociraptorCurve = """
        {"keys":[{"time":0,"value":0.393908322,"inTangent":1.73721325,"outTangent":1.73721325,"weightedMode":"None"},
                 {"time":1,"value":1,"inTangent":0.0356990322,"outTangent":0.0356990322,"weightedMode":"None"}]}
        """;
    private const string BrachiosaurusCurve = """
        {"keys":[{"time":0,"value":0.07884467,"inTangent":0.6078889,"outTangent":0.6078889,"weightedMode":"None"},
                 {"time":0.491450667,"value":0.6044929,"inTangent":1.6740396,"outTangent":1.6740396,"weightedMode":"None"},
                 {"time":1,"value":1,"inTangent":0.03651018,"outTangent":0.03651018,"weightedMode":"None"}]}
        """;

    /// <summary>A workspace whose dump has Velociraptor and Brachiosaurus with their game curves, and a mod "curves".</summary>
    private static (FakeGame Game, Workspace Ws, ModProject Mod) GameCurves()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(TestTemp.Root, Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        var result = new DumpResult();
        var id = 1;
        foreach (var (name, curve) in new[] { ("Velociraptor", VelociraptorCurve), ("Brachiosaurus", BrachiosaurusCurve) })
            result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", name, id), $$"""
                {"$type":"PrehistoricKingdom.AnimalData","$name":"{{name}}","$id":{{id++}},"speciesID":"{{name}}","scaleGrowthCurve":{{curve}},
                 "skinsData":[{"skinName":"Default","maleSizeMultiplier":1,"femaleSizeMultiplier":1}]}
                """));
        DumpWriter.Write(ws.DataDir, result, [], new DumpManifest { RequestId = "r" });
        return (game, ws, ModProject.Create(ws, "curves", null, null));
    }

    [Theory]
    [InlineData("Velociraptor", 0.5f)]
    [InlineData("Brachiosaurus", 0.25f)]
    public void Original_curve_is_read_as_the_game_reads_it(string species, float m)
    {
        var (game, ws, _) = GameCurves();
        using var _ = game;
        var expected = UnityCurve.Evaluate(JsonDocument.Parse(species == "Velociraptor" ? VelociraptorCurve : BrachiosaurusCurve).RootElement, m);

        var curve = SpeciesSizes.Original(DataStore.Open(ws), species, null)!.Curve;

        Assert.InRange(SizeMath.Evaluate(curve, m) / expected, 0.99, 1.01);
        Assert.InRange(SizeMath.Evaluate(SizeMath.Rebuild(curve, 1, null), m) / expected, 0.99, 1.01); // identity sizes
        Assert.InRange(SizeMath.Evaluate(SizeMath.Rebuild(curve, 1.2, null), m) * (1 + 0.2 * SizeMath.Fade(m)) / (1.2 * expected), 0.99, 1.01);
    }

    [Fact]
    public void A_custom_species_curve_follows_its_tangent_edits()
    {
        var (game, ws, mod) = GameCurves();
        using var _ = game;
        var entry = mod.AddSpecies([new("Velociraptor", false, [new VanillaSkin(0, "Default", new Dictionary<string, string>(), new Dictionary<string, string>())])],
            "flat", "Velociraptor", "Flat", "Tyrantus", "flat");
        entry.Fields.Add(new AnimalField { Path = "/scaleGrowthCurve/keys/0/outTangent", Kind = "number", Value = "0", TemplateValue = "1.73721325", Editable = true });
        entry.Fields.Add(new AnimalField { Path = "/scaleGrowthCurve/keys/1/inTangent", Kind = "number", Value = "0", TemplateValue = "0.0356990322", Editable = true });

        var curve = SpeciesSizes.Original(DataStore.Open(ws), entry.Key(mod.Id), mod)!.Curve;

        // Flat tangents: smoothstep from 0.394 to 1 (a quarter of the way in, 0.15625 of the rise; lines would give 0.25 of it).
        Assert.Equal(0.393908322f + (1f - 0.393908322f) * 0.15625f, SizeMath.Evaluate(curve, 0.25f), 4);
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
        File.WriteAllText(Path.Combine(mod.Dir, "sizes", "alpha.json"), """{"format":1,"target":"Alpha","adult":21}""");

        Assert.Contains(Check(ws, mod).Errors, e => e.Contains("sizes/alpha.json") && e.Contains("adult"));
    }

    [Fact]
    public void Extreme_adult_sizes_warn_about_the_size_class()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        mod.SetSizes("Alpha", new SizeSettings { Adult = 0.6 });
        mod.SetSizes("Big", new SizeSettings { Adult = 1.1 });

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
    public void Old_size_record_edits_can_be_removed()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var entry = mod.AddSpecies(Species, "mini", "Alpha", "Mini", "Tyrantus", "mini");
        var key = entry.Key(mod.Id);
        entry.Fields.Add(new AnimalField { Path = "/scaleGrowthCurve/keys/0/value", Kind = "number", Value = "0.25", TemplateValue = "0.5", Editable = true });
        entry.Fields.Add(new AnimalField { Path = "/skinsData/0/maleSizeMultiplier", Kind = "number", Value = "1", TemplateValue = "1", Editable = true }); // unchanged
        entry.Fields.Add(new AnimalField { Path = "/animalLengthHeightWidthRaw/x", Kind = "number", Value = "5", TemplateValue = "4", Editable = true }); // not Size's
        AnimalDatabaseFile.Save(mod.DatabasePath, mod.Manifest.Species);
        mod = ModProject.Open(ws, "size-pack");
        Assert.Equal(["/scaleGrowthCurve/keys/0/value"], mod.SizeRecordEdits(key));
        Assert.Contains(Check(ws, mod).Warnings, w => w.Contains("Set sizes in Size instead") && w.Contains("Remove old size edits"));

        var removed = mod.RemoveSizeRecordEdits(key);

        Assert.Equal(["/scaleGrowthCurve/keys/0/value"], removed);
        var again = ModProject.Open(ws, "size-pack");
        var fields = again.Manifest.Species.Single().Fields;
        Assert.DoesNotContain(fields, f => SpeciesSizes.IsSizePath(f.Path));
        Assert.Contains(fields, f => f.Path == "/animalLengthHeightWidthRaw/x" && f.Value == "5");
        Assert.Empty(again.SizeRecordEdits(key));
        Assert.DoesNotContain(Check(ws, again).Warnings, w => w.Contains("Set sizes in Size instead"));
        Assert.Equal((0f, 0.5f), SpeciesSizes.Original(DataStore.Open(ws), key, again)!.Curve[0]); // the template's curve again
        Assert.Empty(again.SizeRecordEdits("Alpha")); // a game species has none
        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() => again.RemoveSizeRecordEdits("Alpha")).Code);
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

    private const string Past2x = "can grow past the game's normal 2× limit; the size class, camera framing, foot placement and enclosure needs were tuned for normal sizes.";

    [Fact]
    public void Facts_fall_back_to_the_vanilla_ranges_when_the_dump_has_none()
    {
        var (game, ws, mod) = Setup(parameters: false);
        using var _ = game;

        var facts = SpeciesSizes.Facts(ws, mod)!;

        Assert.Equal((0.925, 1.075), facts.Incubation);
        Assert.Equal((0.85, 1.15), facts.Breeding);
    }

    // The size limit is automatic: each species gets the limit its sizes need, up to Tyrant's own 0.01 to 20.

    private static string Text(double value, string format) => value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void The_limit_follows_the_sizes()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var facts = SpeciesSizes.Facts(ws, mod)!;
        var rex = facts.Original("Rex")!;

        Assert.Equal(SizeLimit.Game, SpeciesSizes.LimitOf(rex, new SizeSettings(), facts));
        Assert.Equal(new SizeLimit(0.05f, 3f), SpeciesSizes.LimitOf(rex, new SizeSettings { Adult = 2.63 }, facts)); // 0.98 x 2.63 x 1.15 = 2.96
        Assert.Equal(new SizeLimit(0.05f, SizeLimit.OuterMax), SpeciesSizes.LimitOf(rex, new SizeSettings { Adult = 20 }, facts)); // no further than 20 (its outermost)
        // The names Check and the card use: the species as the game shows it, and the animal it is a size of.
        Assert.Equal(("Tyrannosaurus", "Tyrannosaurus"), (rex.Name, rex.BaseName));
        var key = mod.AddSpecies(Species, "maximus", "Rex", "Tyranno Maximus", "Tyrannus", "maximus").Key(mod.Id);
        var custom = SpeciesSizes.Facts(ws, mod)!.Original(key)!;
        Assert.Equal(("Tyranno Maximus", "Tyrannosaurus"), (custom.Name, custom.BaseName));
        Assert.Equal(("Big", "Big"), (facts.Original("Big")!.Name, facts.Original("Big")!.BaseName)); // no display name: its id
    }

    [Fact]
    public void Sizes_up_to_20_are_no_error_and_past_2_warn()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;

        mod.SetSizes("Rex", new SizeSettings { Adult = 2.63, Nursery = [0.725, 5] }); // the user's: up to 12.9, past the game's 2
        mod.SetSizes("Big", new SizeSettings { Breeding = [0.5, 1.5] }); // 1.5 x 1.5 = 2.25
        mod.SetSizes("Alpha", new SizeSettings { Adult = 1.1 }); // 1.2 x 1.1 x 1.15 = 1.52
        var result = Check(ws, mod);

        Assert.DoesNotContain(result.Errors, e => e.StartsWith("sizes/", StringComparison.Ordinal));
        Assert.Contains("sizes/rex.json: Rex " + Past2x, result.Warnings);
        Assert.Contains("sizes/big.json: Big " + Past2x, result.Warnings);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Alpha " + Past2x));
    }

    [Fact]
    public void Past_20_is_an_error_that_names_the_species_and_why()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var key = mod.AddSpecies(Species, "maximus", "Rex", "Tyranno Maximus", "Tyrannus", "maximus").Key(mod.Id);
        var sizes = new SizeSettings { Adult = 2.63, Nursery = [0.725, 10] };
        mod.SetSizes(key, sizes);
        var facts = SpeciesSizes.Facts(ws, mod)!;
        var max = SpeciesSizes.Extremes(facts.Original(key)!, sizes, facts)!.Value.Max; // the female 0.98 x 2.63 x 10 = 25.8

        var errors = Check(ws, mod).Errors.Where(e => e.StartsWith("sizes/", StringComparison.Ordinal)).ToList();

        // The factors multiply out to the number: the skin's own sex size is in it, and the sex named is the one that gives it.
        Assert.Equal("25.77", Text(max, "0.##"));
        Assert.Equal([$"sizes/{mod.SizeFileOf(key)}: The biggest Tyranno Maximus can reach 25.77× a normal Tyrannosaurus "
            + "(adult 2.63 × female 0.98 × Nursery slider up to 10), more than Tyrant's 20× limit; lower the adult size or the size ranges."], errors);

        // Big grows to 1.5 of its model: that is a factor too. A male multiplier that makes him the bigger sex is named.
        mod.SetSizes("Big", new SizeSettings { Breeding = [0.5, 15] }); // 1.5 x 15 = 22.5
        Assert.Contains("sizes/big.json: The biggest Big can reach 22.5× a normal Big (grown size 1.5 × adult 1 × male 1 × breeding up to 15), more than Tyrant's 20× limit; lower the adult size or the size ranges.",
            Check(ws, mod).Errors);
        mod.SetSizes("Rex", new SizeSettings { Adult = 10, Male = 2 }); // the male 0.95 x 2 = 1.9 beats the female 0.98
        var rex = SpeciesSizes.Extremes(facts.Original("Rex")!, mod.Sizes("Rex")!, facts)!.Value.Max;
        Assert.Contains($"sizes/rex.json: The biggest Tyrannosaurus can reach {Text(rex, "0.##")}× a normal Tyrannosaurus (adult 10 × male 1.9 × breeding up to 1.15), "
            + "more than Tyrant's 20× limit; lower the adult size or the size ranges.", Check(ws, mod).Errors);
        Assert.Equal(Text(10 * 1.9 * 1.15, "0.##"), Text(rex, "0.##"));
    }

    [Fact]
    public void Under_001_is_an_error_worded_the_same_way()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var sizes = new SizeSettings { Adult = 0.05, Baby = 0.1 }; // a hatchling of 0.005
        mod.SetSizes("Alpha", sizes);
        var facts = SpeciesSizes.Facts(ws, mod)!;
        var min = SpeciesSizes.Extremes(facts.Original("Alpha")!, sizes, facts)!.Value.Min;

        // A hatchling's size is no plain product (the game fades the genetic size in): the inputs are listed, not multiplied.
        Assert.Contains($"sizes/alpha.json: The smallest Alpha can be {Text(min, "0.###")}× a normal Alpha (a hatchling, with adult 0.05, baby 0.1, male 1 "
            + "and breeding down to 0.85), less than Tyrant's 0.01× limit; raise the adult size, the baby size or the size ranges.", Check(ws, mod).Errors);

        // 0.05 x 0.5 at birth (0.025, with the smallest bred young 0.02) is under the game's 0.05 but within Tyrant's 0.01: no error.
        mod.SetSizes("Alpha", new SizeSettings { Adult = 0.05 });
        Assert.DoesNotContain(Check(ws, mod).Errors, e => e.StartsWith("sizes/alpha.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Just_inside_20_and_001_is_no_error_and_its_limit_holds_it()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var facts = SpeciesSizes.Facts(ws, mod)!;
        var big = new SizeSettings { Adult = 19.99 / 1.725 }; // Big: 1.5 x adult x 1.15 = 19.99
        double lo = 0.1, hi = 1;
        for (var i = 0; i < 60; i++)
        {
            var baby = (lo + hi) / 2;
            if (SpeciesSizes.Extremes(facts.Original("Alpha")!, new SizeSettings { Adult = 0.05, Baby = baby }, facts)!.Value.Min < 0.010005f) lo = baby; else hi = baby;
        }
        var alpha = new SizeSettings { Adult = 0.05, Baby = hi };
        mod.SetSizes("Big", big);
        mod.SetSizes("Alpha", alpha);

        var max = SpeciesSizes.Extremes(facts.Original("Big")!, big, facts)!.Value.Max;
        var min = SpeciesSizes.Extremes(facts.Original("Alpha")!, alpha, facts)!.Value.Min;
        Assert.InRange(max, 19.981f, 20f);
        Assert.InRange(min, 0.01f, 0.010009f);
        Assert.DoesNotContain(Check(ws, mod).Errors, e => e.StartsWith("sizes/", StringComparison.Ordinal));
        var bigLimit = SpeciesSizes.LimitOf(facts.Original("Big")!, big, facts)!.Value;
        var alphaLimit = SpeciesSizes.LimitOf(facts.Original("Alpha")!, alpha, facts)!.Value;
        Assert.True(max <= bigLimit.SafeMax, $"{bigLimit} holds {max}"); // the game does not back it off
        Assert.True(min >= alphaLimit.SafeMin, $"{alphaLimit} holds {min}");
    }

    [Fact]
    public void An_old_limit_warns_and_is_dropped_on_the_next_save()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        Directory.CreateDirectory(Path.Combine(mod.Dir, "sizes"));
        var file = Path.Combine(mod.Dir, "sizes", "big.json");
        File.WriteAllText(file, """{"format":1,"target":"Big","adult":1.5,"limit":[0.05,2]}"""); // 2.59, past the old limit's 2
        File.WriteAllText(Path.Combine(mod.Dir, "sizes", "alpha.json"), """{"format":1,"target":"Alpha","limit":[0.05,5]}"""); // nothing else

        var result = Check(ws, mod);

        Assert.DoesNotContain(result.Errors, e => e.StartsWith("sizes/", StringComparison.Ordinal)); // the limit is automatic: 2.59 fits
        Assert.Contains("sizes/big.json: The size limit is automatic now; this file's limit is ignored and is removed the next time you save.", result.Warnings);
        Assert.Contains("sizes/alpha.json: The size limit is automatic now; this file's limit is ignored and is removed the next time you save.", result.Warnings);

        mod.SetSizes("Big", mod.Sizes("Big"));
        Assert.DoesNotContain("limit", File.ReadAllText(file));
        Assert.Equal(1.5, mod.Sizes("Big")!.Adult);
        Assert.DoesNotContain(Check(ws, mod).Warnings, w => w.StartsWith("sizes/big.json: The size limit", StringComparison.Ordinal));
    }
}
