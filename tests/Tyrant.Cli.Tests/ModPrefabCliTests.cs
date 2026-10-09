using Tyrant.Cli;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class ModPrefabCliTests
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

    [Fact]
    public void Lists_sets_resets_and_clears_prefab_settings()
    {
        using var game = new FakeGame();
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", ws, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "red-spot", "--name", "Red Spot", "-w", ws).Code);
        SkinDumps.Write(Path.Combine(ws, "data"));
        new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab], Fingerprint = GameFingerprint.Compute(new GameInstall(game.Root, null)) }
            .Save(Path.Combine(ws, "cache", AssetIndex.FileName));
        var previous = CliServices.AssetReader;
        CliServices.AssetReader = new FakeAssetReader
        {
            PrefabComponentsToReturn = new("Carch", [new PrefabObject("", [new PrefabComponent("AnimalPhysics", null, null, 0, """{"inputParams":{"waterlineHeight":2}}""")])]),
        };
        try
        {
            var set = Run("mod", "prefab", "red-spot", "Carcharodontosaurus", "--set", "|AnimalPhysics|inputParams.waterlineHeight=3.5", "-w", ws);
            Assert.True(set.Code == ExitCodes.Ok, set.Err);
            Assert.Contains("(root) | AnimalPhysics | inputParams.waterlineHeight = 2 -> 3.5", Run("mod", "prefab", "red-spot", "Carcharodontosaurus", "--overridden", "-w", ws).Out);

            var bad = Run("mod", "prefab", "red-spot", "Carcharodontosaurus", "--set", "|AnimalPhysics|inputParams.waterlineHeight=deep", "-w", ws);
            Assert.NotEqual(ExitCodes.Ok, bad.Code);
            Assert.Contains("needs a finite number", bad.Err);

            var file = Path.Combine(ws, "mods", "red-spot", "prefabs", "carcharodontosaurus.json");
            Assert.Equal(ExitCodes.Ok, Run("mod", "prefab", "red-spot", "Carcharodontosaurus", "--reset", "|AnimalPhysics|inputParams.waterlineHeight", "-w", ws).Code);
            Assert.False(File.Exists(file));

            Run("mod", "prefab", "red-spot", "Carcharodontosaurus", "--set", "|AnimalPhysics|inputParams.waterlineHeight=4", "-w", ws);
            Assert.True(File.Exists(file));
            Assert.Equal(ExitCodes.Ok, Run("mod", "prefab", "red-spot", "Carcharodontosaurus", "--clear", "-w", ws).Code);
            Assert.False(File.Exists(file));
            var again = Run("mod", "prefab", "red-spot", "Carcharodontosaurus", "--clear", "-w", ws);
            Assert.Equal(ExitCodes.Ok, again.Code);
            Assert.Contains("has no prefab settings in 'red-spot'", again.Out);
        }
        finally
        {
            CliServices.AssetReader = previous;
        }
    }
}
