using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class PrefabFieldsTests
{
    private static Workspace Ws(out FakeGame game)
    {
        game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        SkinDumps.Write(ws.DataDir);
        return ws;
    }

    [Fact]
    public void Reads_a_targets_fields_once_and_then_from_the_cache()
    {
        var ws = Ws(out var game);
        using var _ = game;
        var install = new GameInstall(game.Root, null);
        var index = new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] };
        var species = SpeciesSkinsReader.Load(ws);
        var reader = new FakeAssetReader
        {
            PrefabComponentsToReturn = new("Carch", [new PrefabObject("", [new PrefabComponent("AnimalPhysics", null, null, 0, """{"inputParams":{"waterlineHeight":2}}""")])]),
        };

        var first = PrefabFields.Read(ws, install, index, species, reader, "Carcharodontosaurus");
        reader.PrefabComponentsToReturn = null; // a second read must not touch the reader
        var second = PrefabFields.Read(ws, install, index, species, reader, "Carcharodontosaurus");

        Assert.Equal("inputParams.waterlineHeight", Assert.Single(first).Field);
        Assert.Equal(first.Select(f => f with { }).ToList(), second.ToList());
    }

    [Fact]
    public void The_cache_keeps_enum_names_and_values()
    {
        var info = new PrefabFieldInfo("", "C", "mode", "enum", "1", true, ["A", "B"], [0L, 1L]);
        var back = JsonSerializer.Deserialize<List<PrefabFieldInfo>>(JsonSerializer.Serialize(new List<PrefabFieldInfo> { info }))!.Single();

        Assert.Equal(["A", "B"], back.EnumNames);
        Assert.Equal([0L, 1L], back.EnumValues);
    }
}
