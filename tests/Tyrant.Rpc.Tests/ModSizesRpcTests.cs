using System.Text.Json;
using Tyrant.Core.Dumping;
using Tyrant.Core.Tests;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Rpc.Tests;

public class ModSizesRpcTests
{
    private static string Rev(JsonElement detail) => detail.GetProperty("revision").GetString()!;

    /// <summary>A mod "size-pack" in a workspace whose dump has Alpha (two skins, 4 long, a hatchling half the adult's size).</summary>
    private static async Task<(RpcHarness H, string Ws)> WithAlpha(FakeGame game)
    {
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Alpha", 1), """
            {"speciesID":"Alpha","scaleGrowthCurve":{"keys":[{"time":0,"value":0.5},{"time":1,"value":1}]},
             "skinsData":[{"skinName":"Default","maleSizeMultiplier":1,"femaleSizeMultiplier":1},{"skinName":"Big","maleSizeMultiplier":1.2,"femaleSizeMultiplier":1.1}],
             "animalLengthHeightWidthRaw":{"x":4,"y":2,"z":1}}
            """));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
        await h.Call("mods.create", new { id = "size-pack" });
        return (h, ws);
    }

    private static bool IsNull(JsonElement e, string name) => !e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null;

    [Fact]
    public async Task Sizes_round_trip_with_the_original_and_the_final_length()
    {
        using var game = new FakeGame();
        var (h, ws) = await WithAlpha(game);
        var detail = await h.Call("mods.get", new { id = "size-pack" });

        var set = await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(detail), target = "Alpha", adult = 1.5, baby = 0.4, male = 1.1 });
        var sizes = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });

        Assert.Equal(1.5, sizes.GetProperty("adult").GetDouble());
        Assert.Equal(0.4, sizes.GetProperty("baby").GetDouble());
        Assert.Equal(1.1, sizes.GetProperty("male").GetDouble());
        Assert.True(IsNull(sizes, "female"));
        var original = sizes.GetProperty("original");
        Assert.Equal(4, original.GetProperty("adultLength").GetDouble());
        Assert.Equal(0.5, original.GetProperty("babyShare").GetDouble());
        Assert.Equal(2, original.GetProperty("skins").GetArrayLength());
        Assert.Equal(1.2, original.GetProperty("skins")[1].GetProperty("male").GetDouble(), 6);
        Assert.Equal(6, sizes.GetProperty("finalAdultLength").GetDouble(), 6);
        Assert.Equal(["Alpha"], (await h.Call("mods.sizeTargets", new { id = "size-pack" })).GetProperty("targets").EnumerateArray().Select(t => t.GetString()));
        Assert.True(set.GetProperty("sizeFiles").TryGetProperty("alpha.json", out _));

        var none = await h.Call("mods.sizes", new { id = "size-pack", target = "Missing" });
        Assert.True(IsNull(none, "adult"));
        Assert.True(IsNull(none, "finalAdultLength"));
        Assert.True(IsNull(none.GetProperty("original"), "adultLength"));

        var cleared = await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(set), target = "Alpha" });
        Assert.False(File.Exists(Path.Combine(ws, "mods", "size-pack", "sizes", "alpha.json")));
        Assert.Empty(cleared.GetProperty("sizeFiles").EnumerateObject());
    }

    [Fact]
    public async Task A_bad_size_is_refused()
    {
        using var game = new FakeGame();
        var (h, _) = await WithAlpha(game);
        var detail = await h.Call("mods.get", new { id = "size-pack" });

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(detail), target = "Alpha", adult = 9 }));
        var range = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setSizeRanges", new { id = "size-pack", revision = Rev(detail), nursery = new[] { 0.0, 1.0 } }));

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.Equal("MOD_INVALID", range.DataCode);
    }

    [Fact]
    public async Task Size_ranges_round_trip()
    {
        using var game = new FakeGame();
        var (h, _) = await WithAlpha(game);
        var detail = await h.Call("mods.get", new { id = "size-pack" });

        var set = await h.Call("mods.setSizeRanges", new { id = "size-pack", revision = Rev(detail), nursery = new[] { 0.9, 1.1 }, breeding = new[] { 0.8, 1.2 } });
        var ranges = await h.Call("mods.sizeRanges", new { id = "size-pack" });

        Assert.Equal([0.9, 1.1], ranges.GetProperty("nursery").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal([0.8, 1.2], ranges.GetProperty("breeding").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal(0.925, ranges.GetProperty("gameNursery")[0].GetDouble());

        await h.Call("mods.setSizeRanges", new { id = "size-pack", revision = Rev(set) });
        Assert.True(IsNull(await h.Call("mods.sizeRanges", new { id = "size-pack" }), "nursery"));
    }

    [Fact]
    public async Task Undo_puts_back_the_size_files_and_the_size_ranges()
    {
        using var game = new FakeGame();
        var (h, ws) = await WithAlpha(game);
        var before = await h.Call("mods.get", new { id = "size-pack" });
        var afterSizes = await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(before), target = "Alpha", adult = 1.5 });
        var afterRanges = await h.Call("mods.setSizeRanges", new { id = "size-pack", revision = Rev(afterSizes), breeding = new[] { 0.9, 1.1 } });
        var file = Path.Combine(ws, "mods", "size-pack", "sizes", "alpha.json");

        // Undo both: the files and mod.json of the first state.
        var undone = await h.Call("mods.restoreSnapshot", new
        {
            id = "size-pack", revision = Rev(afterRanges), manifest = before.GetProperty("manifestJson").GetString(),
            prefabs = new Dictionary<string, string>(), sizes = new Dictionary<string, string>(),
        });
        Assert.False(File.Exists(file));
        Assert.DoesNotContain("sizeRanges", undone.GetProperty("manifestJson").GetString());
        Assert.Empty(undone.GetProperty("sizeFiles").EnumerateObject());

        // Redo: sizes come back from the snapshot of the later state.
        var redone = await h.Call("mods.restoreSnapshot", new
        {
            id = "size-pack", revision = Rev(undone), manifest = afterRanges.GetProperty("manifestJson").GetString(),
            prefabs = new Dictionary<string, string>(),
            sizes = afterRanges.GetProperty("sizeFiles").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!),
        });
        Assert.True(File.Exists(file));
        Assert.Equal(1.5, (await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" })).GetProperty("adult").GetDouble());
        Assert.Contains("sizeRanges", redone.GetProperty("manifestJson").GetString());

        // A snapshot without sizes leaves the size files as they are.
        await h.Call("mods.restoreSnapshot", new { id = "size-pack", revision = Rev(redone), manifest = redone.GetProperty("manifestJson").GetString(), prefabs = new Dictionary<string, string>() });
        Assert.True(File.Exists(file));

        var bad = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.restoreSnapshot", new
        {
            id = "size-pack", revision = Rev(redone), manifest = redone.GetProperty("manifestJson").GetString(),
            prefabs = new Dictionary<string, string>(), sizes = new Dictionary<string, string> { ["../x.json"] = "{}" },
        }));
        Assert.Equal("MOD_INVALID", bad.DataCode);
    }
}
