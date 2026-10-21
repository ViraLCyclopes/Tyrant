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
    public void A_bad_value_prints_the_message_and_fails()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);

        var bad = Run("mod", "size", "size-pack", "Alpha", "--adult", "9", "-w", ws);

        Assert.NotEqual(ExitCodes.Ok, bad.Code);
        Assert.Contains("\"adult\" must be from 0.05 to 5", bad.Err);
        Assert.False(File.Exists(Path.Combine(ws, "mods", "size-pack", "sizes", "alpha.json")));
    }

    [Fact]
    public void Sets_prints_and_clears_size_ranges()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);

        var set = Run("mod", "size-ranges", "size-pack", "--nursery", "0.9,1.1", "--breeding", "0.8,1.2", "-w", ws);
        Assert.True(set.Code == ExitCodes.Ok, set.Err);
        Assert.Contains("nursery 0.9 to 1.1", set.Out);
        Assert.Contains("breeding 0.8 to 1.2", set.Out);
        Assert.Contains("sizeRanges", File.ReadAllText(Path.Combine(ws, "mods", "size-pack", "mod.json")));

        var bad = Run("mod", "size-ranges", "size-pack", "--nursery", "0,1", "-w", ws);
        Assert.NotEqual(ExitCodes.Ok, bad.Code);
        Assert.Contains("sizeRanges.nursery must be [min, max]", bad.Err);

        var shape = Run("mod", "size-ranges", "size-pack", "--nursery", "0.9", "-w", ws);
        Assert.NotEqual(ExitCodes.Ok, shape.Code);
        Assert.Contains("takes two numbers", shape.Err);

        Assert.Equal(ExitCodes.Ok, Run("mod", "size-ranges", "size-pack", "--clear", "-w", ws).Code);
        Assert.DoesNotContain("sizeRanges", File.ReadAllText(Path.Combine(ws, "mods", "size-pack", "mod.json")));
    }
}
