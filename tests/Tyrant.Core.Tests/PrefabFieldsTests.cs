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

    private static (Workspace Ws, GameInstall Install, AssetIndex Index, IReadOnlyList<SpeciesSkins> Species, FakeAssetReader Reader, FakeGame Game) Arrange()
    {
        var ws = Ws(out var game);
        return (ws, new GameInstall(game.Root, null), new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] }, SpeciesSkinsReader.Load(ws),
            new FakeAssetReader { PrefabComponentsToReturn = new("Carch", [new PrefabObject("", [new PrefabComponent("AnimalPhysics", null, null, 0, """{"a":1}""")])]) }, game);
    }

    private static string CacheFile(Workspace ws) => Directory.GetFiles(Path.Combine(ws.CacheDir, "prefab-fields"), "*.json").Single();

    [Fact]
    public void A_cache_file_holding_null_is_a_miss()
    {
        var (ws, install, index, species, reader, game) = Arrange();
        using var _ = game;
        PrefabFields.Read(ws, install, index, species, reader, "Carcharodontosaurus");
        File.WriteAllText(CacheFile(ws), "null");
        reader.PrefabComponentsToReturn = null;

        Assert.Throws<InvalidOperationException>(() => PrefabFields.Read(ws, install, index, species, reader, "Carcharodontosaurus")); // the reader was asked again
    }

    [Fact]
    public void A_cache_that_cannot_be_written_still_returns_the_tree()
    {
        var (ws, install, index, species, reader, game) = Arrange();
        using var _ = game;
        PrefabFields.Read(ws, install, index, species, reader, "Carcharodontosaurus");
        var file = CacheFile(ws);
        File.Delete(file);
        Directory.CreateDirectory(file); // the Move onto it fails

        var tree = PrefabFields.Read(ws, install, index, species, reader, "Carcharodontosaurus");

        Assert.Equal("a", Assert.Single(tree).Field);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.tmp-*"));
    }
}
