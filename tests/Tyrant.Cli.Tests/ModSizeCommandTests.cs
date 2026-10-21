using Tyrant.Cli;
using Tyrant.Core.Dumping;
using Tyrant.Core.Tests;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Cli.Tests;

public class ModSizeCommandTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    private static string Workspace(FakeGame game)
    {
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        var init = Run("workspace", "init", ws, "--game", game.Root);
        Assert.True(init.Code == ExitCodes.Ok, init.Err);
        var made = Run("mod", "new", "size-pack", "--name", "Size pack", "-w", ws);
        Assert.True(made.Code == ExitCodes.Ok, made.Err);
        var objects = new DumpResult();
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Alpha", 1), """
            {"speciesID":"Alpha","scaleGrowthCurve":{"keys":[{"time":0,"value":0.5},{"time":1,"value":1}]},
             "skinsData":[{"skinName":"Default","maleSizeMultiplier":1,"femaleSizeMultiplier":1}],"animalLengthHeightWidthRaw":{"x":4,"y":2,"z":1}}
            """));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
        return ws;
    }

    [Fact]
    public void Sets_prints_and_clears_sizes()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        var file = Path.Combine(ws, "mods", "size-pack", "sizes", "alpha.json");

        var set = Run("mod", "size", "size-pack", "Alpha", "--adult", "1.5", "--baby", "0.4", "-w", ws);
        Assert.True(set.Code == ExitCodes.Ok, set.Err);
        Assert.Contains("adult 1.5", set.Out);
        Assert.Contains("original: length 4", set.Out);
        Assert.True(File.Exists(file));

        var more = Run("mod", "size", "size-pack", "Alpha", "--male", "1.1", "-w", ws); // keeps the earlier values
        Assert.Contains("adult 1.5", more.Out);
        Assert.Contains("male 1.1", more.Out);
        Assert.Contains("final length 6", more.Out);

        Assert.Equal(ExitCodes.Ok, Run("mod", "size", "size-pack", "Alpha", "--clear", "-w", ws).Code);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Check_judges_a_species_range_against_its_limit()
    {
        using var game = new FakeGame();
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", ws, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "wide-nursery", "--name", "Wide nursery", "-w", ws).Code);
        var objects = new DumpResult();
        // The dumped Allosaurus Anax (curve and its one skin's sex sizes) and the game's genetic ranges.
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus Anax", 1), """
            {"speciesID":"Allosaurus Anax",
             "scaleGrowthCurve":{"keys":[{"time":0,"value":0.16434139,"inTangent":0.588403165,"outTangent":0.588403165},
                                         {"time":1,"value":1,"inTangent":-0.05258916,"outTangent":-0.05258916}]},
             "skinsData":[{"skinName":"Pyroclastic","maleSizeMultiplier":1.3,"femaleSizeMultiplier":1.35}]}
            """));
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalsV2.Data.GlobalAnimalParameters", "GlobalAnimalParameters", 2),
            """{"incubationGeneticSizeMinMax":{"x":0.925,"y":1.075},"breedingGeneticSizeMinMax":{"x":0.85,"y":1.15}}"""));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });

        var set = Run("mod", "size", "wide-nursery", "Allosaurus Anax", "--nursery", "0.5,1.5", "-w", ws); // females: 1.35 x 1.5 = 2.03
        Assert.True(set.Code == ExitCodes.Ok, set.Err);
        var check = Run("mod", "check", "wide-nursery", "-w", ws);

        var lines = (check.Out + check.Err).Split(Environment.NewLine);
        Assert.Contains(lines, l => l.Contains("Allosaurus Anax would be scaled from") && l.Contains("the game allows 0.05 to 2"));
        Assert.DoesNotContain(lines, l => l.Contains("genetic size ranges"));
    }

    [Fact]
    public void Shows_and_removes_old_size_record_edits()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        var workspace = Tyrant.Core.Workspaces.Workspace.Open(ws);
        var mod = Tyrant.Core.Mods.ModProject.Open(workspace, "size-pack");
        var entry = mod.AddSpecies(Tyrant.Core.Mods.SpeciesSkinsReader.Load(workspace), "mini", "Alpha", "Mini", "Tyrantus", "mini");
        entry.Fields.Add(new Tyrant.Framework.Core.AnimalField { Path = "/scaleGrowthCurve/keys/0/value", Kind = "number", Value = "0.25", TemplateValue = "0.5", Editable = true });
        Tyrant.Framework.Core.AnimalDatabaseFile.Save(mod.DatabasePath, mod.Manifest.Species);
        var key = entry.Key("size-pack");

        var shown = Run("mod", "size", "size-pack", key, "-w", ws);
        Assert.Contains("old record edits: /scaleGrowthCurve/keys/0/value", shown.Out);
        Assert.Contains("--remove-record-edits", shown.Out);

        var removed = Run("mod", "size", "size-pack", key, "--remove-record-edits", "-w", ws);
        Assert.True(removed.Code == ExitCodes.Ok, removed.Err);
        Assert.Contains("removed the old record edits: /scaleGrowthCurve/keys/0/value", removed.Out);
        Assert.DoesNotContain("old record edits:", Run("mod", "size", "size-pack", key, "-w", ws).Out);
    }

    [Fact]
    public void A_bad_value_prints_the_message_and_fails()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);

        var bad = Run("mod", "size", "size-pack", "Alpha", "--adult", "21", "-w", ws);

        Assert.NotEqual(ExitCodes.Ok, bad.Code);
        Assert.Contains("\"adult\" must be from 0.05 to 20", bad.Err);
        Assert.False(File.Exists(Path.Combine(ws, "mods", "size-pack", "sizes", "alpha.json")));
    }

    [Fact]
    public void Sets_prints_and_clears_a_species_ranges_and_limit()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        var file = Path.Combine(ws, "mods", "size-pack", "sizes", "alpha.json");

        var set = Run("mod", "size", "size-pack", "Alpha", "--nursery", "0.9,1.1", "--breeding", "0.8,1.2", "-w", ws);
        Assert.True(set.Code == ExitCodes.Ok, set.Err);
        Assert.Contains("nursery 0.9 to 1.1", set.Out);
        Assert.Contains("breeding 0.8 to 1.2", set.Out);
        Assert.Contains("limit the game's (0.05 to 2)", set.Out);
        Assert.Contains("\"nursery\"", File.ReadAllText(file));
        Assert.DoesNotContain("sizeRanges", File.ReadAllText(Path.Combine(ws, "mods", "size-pack", "mod.json")));

        var bad = Run("mod", "size", "size-pack", "Alpha", "--nursery", "0,1", "-w", ws);
        Assert.NotEqual(ExitCodes.Ok, bad.Code);
        Assert.Contains("\"nursery\" must be [min, max]", bad.Err);

        var shape = Run("mod", "size", "size-pack", "Alpha", "--nursery", "0.9", "-w", ws);
        Assert.NotEqual(ExitCodes.Ok, shape.Code);
        Assert.Contains("takes two numbers", shape.Err);

        var limit = Run("mod", "size", "size-pack", "Alpha", "--limit", "0.05,20", "--adult", "4", "-w", ws);
        Assert.True(limit.Code == ExitCodes.Ok, limit.Err);
        Assert.Contains("limit 0.05 to 20", limit.Out);
        Assert.Contains("adult 4", limit.Out);
        Assert.Contains("nursery 0.9 to 1.1", limit.Out); // the other values are kept
        var tooBig = Run("mod", "size", "size-pack", "Alpha", "--limit", "0.05,25", "-w", ws);
        Assert.NotEqual(ExitCodes.Ok, tooBig.Code);
        Assert.Contains("\"limit\" must be [min, max] with 0.01 <= min <= 0.05 and 2 <= max <= 20", tooBig.Err);
        Assert.Contains("takes two numbers", Run("mod", "size", "size-pack", "Alpha", "--limit", "20", "-w", ws).Err);

        Assert.Equal(ExitCodes.Ok, Run("mod", "size", "size-pack", "Alpha", "--clear", "-w", ws).Code);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void The_mod_wide_size_ranges_command_is_gone()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);

        var gone = Run("mod", "size-ranges", "size-pack", "--limit", "0.05,20", "-w", ws);

        Assert.NotEqual(ExitCodes.Ok, gone.Code);
        Assert.DoesNotContain("sizeRanges", File.ReadAllText(Path.Combine(ws, "mods", "size-pack", "mod.json")));
    }
}
