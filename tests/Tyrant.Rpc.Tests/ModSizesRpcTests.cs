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

    /// <summary>The dumped Allosaurus Anax (its curve, tangents and all, and its one skin's sex sizes) and the game's genetic ranges.</summary>
    private const string AnaxData = """
        {"speciesID":"Allosaurus Anax",
         "scaleGrowthCurve":{"keys":[{"time":0,"value":0.16434139,"inTangent":0.588403165,"outTangent":0.588403165},
                                     {"time":1,"value":1,"inTangent":-0.05258916,"outTangent":-0.05258916}]},
         "skinsData":[{"skinName":"Pyroclastic","maleSizeMultiplier":1.3,"femaleSizeMultiplier":1.35}]}
        """;
    private const string GameRanges = """{"incubationGeneticSizeMinMax":{"x":0.925,"y":1.075},"breedingGeneticSizeMinMax":{"x":0.85,"y":1.15}}""";

    [Fact]
    public async Task Check_judges_a_species_ranges_against_its_own_limit()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus Anax", 1), AnaxData));
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalsV2.Data.GlobalAnimalParameters", "GlobalAnimalParameters", 2), GameRanges));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
        await h.Call("mods.create", new { id = "wide-nursery" });
        var detail = await h.Call("mods.get", new { id = "wide-nursery" });

        // The species' own Nursery range: females 1.35 x 1.5 = 2.03, past the game's 2 (set here as its limit, so it is not raised).
        var set = await h.Call("mods.setSizes", new { id = "wide-nursery", revision = Rev(detail), target = "Allosaurus Anax", nursery = new[] { 0.5, 1.5 }, limit = new[] { 0.05, 2.0 } });
        var check = await h.Call("mods.check", new { id = "wide-nursery" });
        Assert.Contains(check.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("Allosaurus Anax would be scaled from") && e.GetString()!.Contains("the game allows 0.05 to 2"));
        Assert.DoesNotContain(check.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("genetic size ranges"));

        // Its own limit lets it: then only the past-2x caution.
        await h.Call("mods.setSizes", new { id = "wide-nursery", revision = Rev(set), target = "Allosaurus Anax", nursery = new[] { 0.5, 1.5 }, limit = new[] { 0.05, 3.0 } });
        check = await h.Call("mods.check", new { id = "wide-nursery" });
        Assert.DoesNotContain(check.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("Allosaurus Anax"));
        Assert.Contains(check.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("Allosaurus Anax can grow past the game's normal 2× limit"));
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

        Assert.Equal("alpha.json", sizes.GetProperty("file").GetString()); // Check's messages about it start with sizes/alpha.json
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
        Assert.Equal("missing.json", none.GetProperty("file").GetString()); // the file its sizes would go in
        Assert.True(IsNull(none, "adult"));
        Assert.True(IsNull(none, "finalAdultLength"));
        Assert.True(IsNull(none.GetProperty("original"), "adultLength"));

        var cleared = await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(set), target = "Alpha" });
        Assert.False(File.Exists(Path.Combine(ws, "mods", "size-pack", "sizes", "alpha.json")));
        Assert.Empty(cleared.GetProperty("sizeFiles").EnumerateObject());
    }

    [Fact]
    public async Task Old_size_record_edits_show_and_can_be_removed()
    {
        using var game = new FakeGame();
        var (h, ws) = await WithAlpha(game);
        var workspace = Tyrant.Core.Workspaces.Workspace.Open(ws);
        var mod = Tyrant.Core.Mods.ModProject.Open(workspace, "size-pack");
        var entry = mod.AddSpecies(Tyrant.Core.Mods.SpeciesSkinsReader.Load(workspace), "mini", "Alpha", "Mini", "Tyrantus", "mini");
        entry.Fields.Add(new Tyrant.Framework.Core.AnimalField { Path = "/skinsData/1/femaleSizeMultiplier", Kind = "number", Value = "0.9", TemplateValue = "1.1", Editable = true });
        Tyrant.Framework.Core.AnimalDatabaseFile.Save(mod.DatabasePath, mod.Manifest.Species);
        var key = entry.Key("size-pack");

        var sizes = await h.Call("mods.sizes", new { id = "size-pack", target = key });
        Assert.Equal(["/skinsData/1/femaleSizeMultiplier"], sizes.GetProperty("recordEdits").EnumerateArray().Select(e => e.GetString()));
        var detail = await h.Call("mods.get", new { id = "size-pack" });

        await h.Call("mods.removeSizeEdits", new { id = "size-pack", revision = Rev(detail), target = key });

        Assert.Empty((await h.Call("mods.sizes", new { id = "size-pack", target = key })).GetProperty("recordEdits").EnumerateArray());
        Assert.Empty((await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" })).GetProperty("recordEdits").EnumerateArray());
    }

    [Fact]
    public async Task A_bad_size_is_refused()
    {
        using var game = new FakeGame();
        var (h, _) = await WithAlpha(game);
        var detail = await h.Call("mods.get", new { id = "size-pack" });

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(detail), target = "Alpha", adult = 21 }));
        var range = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(detail), target = "Alpha", nursery = new[] { 0.0, 1.0 } }));
        var limit = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(detail), target = "Alpha", limit = new[] { 0.05, 25.0 } }));

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.Equal("MOD_INVALID", range.DataCode);
        Assert.Equal("MOD_INVALID", limit.DataCode);
        Assert.Contains("\"limit\" must be [min, max] with 0.01 <= min <= 0.05 and 2 <= max <= 20", limit.Message);
    }

    [Fact]
    public async Task Sizes_carry_the_species_ranges_and_limit_with_the_games()
    {
        using var game = new FakeGame();
        var (h, _) = await WithAlpha(game);
        var detail = await h.Call("mods.get", new { id = "size-pack" });
        static IEnumerable<double> Pair(JsonElement e, string name) => e.GetProperty(name).EnumerateArray().Select(x => Math.Round(x.GetDouble(), 6));

        // Unset: null, with the game's values to show (the dump has no global parameters here: the vanilla ones).
        var unset = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });
        Assert.True(IsNull(unset, "nursery"));
        Assert.True(IsNull(unset, "breeding"));
        Assert.True(IsNull(unset, "limit"));
        Assert.Equal([0.925, 1.075], Pair(unset, "gameNursery"));
        Assert.Equal([0.85, 1.15], Pair(unset, "gameBreeding"));
        Assert.Equal([0.05, 2], Pair(unset, "gameLimit"));

        var set = await h.Call("mods.setSizes", new
        {
            id = "size-pack", revision = Rev(detail), target = "Alpha", adult = 1.2,
            nursery = new[] { 0.5, 3.0 }, breeding = new[] { 0.8, 1.2 }, limit = new[] { 0.02, 20.0 },
        });
        var sizes = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });
        Assert.Equal([0.5, 3], Pair(sizes, "nursery"));
        Assert.Equal([0.8, 1.2], Pair(sizes, "breeding"));
        Assert.Equal([0.02, 20], Pair(sizes, "limit"));
        Assert.Equal(1.2, sizes.GetProperty("adult").GetDouble());
        Assert.True(IsNull(await h.Call("mods.sizes", new { id = "size-pack", target = "Beta" }), "limit")); // another species' is its own

        // Leaving a key out clears it (the card's Reset).
        await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(set), target = "Alpha", adult = 1.2, nursery = new[] { 0.5, 3.0 } });
        var reset = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });
        Assert.True(IsNull(reset, "limit"));
        Assert.True(IsNull(reset, "breeding"));
        Assert.Equal([0.5, 3], Pair(reset, "nursery"));
    }

    [Fact]
    public async Task The_mod_wide_size_range_methods_are_gone()
    {
        using var game = new FakeGame();
        var (h, _) = await WithAlpha(game);
        var detail = await h.Call("mods.get", new { id = "size-pack" });

        var get = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.sizeRanges", new { id = "size-pack" }));
        var set = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setSizeRanges", new { id = "size-pack", revision = Rev(detail), limit = new[] { 0.05, 20.0 } }));

        Assert.Equal(Tyrant.Rpc.Protocol.RpcErrorCodes.MethodNotFound, get.Code);
        Assert.Equal(Tyrant.Rpc.Protocol.RpcErrorCodes.MethodNotFound, set.Code);
    }

    [Fact]
    public async Task Undo_puts_back_the_size_files()
    {
        using var game = new FakeGame();
        var (h, ws) = await WithAlpha(game);
        var before = await h.Call("mods.get", new { id = "size-pack" });
        var afterSizes = await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(before), target = "Alpha", adult = 1.5 });
        var afterRanges = await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(afterSizes), target = "Alpha", adult = 1.5, breeding = new[] { 0.9, 1.1 } });
        var file = Path.Combine(ws, "mods", "size-pack", "sizes", "alpha.json");

        // Undo both: the files and mod.json of the first state.
        var undone = await h.Call("mods.restoreSnapshot", new
        {
            id = "size-pack", revision = Rev(afterRanges), manifest = before.GetProperty("manifestJson").GetString(),
            prefabs = new Dictionary<string, string>(), sizes = new Dictionary<string, string>(),
        });
        Assert.False(File.Exists(file));
        Assert.Empty(undone.GetProperty("sizeFiles").EnumerateObject());

        // Redo: sizes come back from the snapshot of the later state, ranges and all.
        var redone = await h.Call("mods.restoreSnapshot", new
        {
            id = "size-pack", revision = Rev(undone), manifest = afterRanges.GetProperty("manifestJson").GetString(),
            prefabs = new Dictionary<string, string>(),
            sizes = afterRanges.GetProperty("sizeFiles").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!),
        });
        Assert.True(File.Exists(file));
        var sizes = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });
        Assert.Equal(1.5, sizes.GetProperty("adult").GetDouble());
        Assert.Equal([0.9, 1.1], sizes.GetProperty("breeding").EnumerateArray().Select(x => x.GetDouble()));
        Assert.DoesNotContain("sizeRanges", redone.GetProperty("manifestJson").GetString());

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

    private static string[] Notes(JsonElement detail) =>
        detail.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array ? [.. notes.EnumerateArray().Select(n => n.GetString()!)] : [];

    [Fact]
    public async Task SetSizes_raises_the_limit_to_fit_and_says_so()
    {
        using var game = new FakeGame();
        var (h, _) = await WithAlpha(game);
        var detail = await h.Call("mods.get", new { id = "size-pack" });
        Assert.Empty(Notes(detail));

        // Alpha's bigger skin: 1.2 x 2 x 1.15 = 2.76, past the game's 2: the limit goes to 3 in the same save.
        var set = await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(detail), target = "Alpha", adult = 2.0 });
        Assert.Equal(["Raised this species' Size limit to 3×."], Notes(set));
        var sizes = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });
        Assert.Equal([0.05, 3.0], sizes.GetProperty("limit").EnumerateArray().Select(x => x.GetDouble()));

        // A save that changes the limit itself keeps it, even when the sizes pass it (Check says so).
        var own = await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(set), target = "Alpha", adult = 2.0, limit = new[] { 0.05, 2.0 } });
        Assert.Empty(Notes(own));
        sizes = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });
        Assert.Equal([0.05, 2.0], sizes.GetProperty("limit").EnumerateArray().Select(x => x.GetDouble()));

        // The card sends the limit it has with every save: kept as it is, it is not set by this save, so the raise applies.
        var again = await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(own), target = "Alpha", adult = 2.1, limit = new[] { 0.05, 2.0 } });
        Assert.Equal(["Raised this species' Size limit to 3×."], Notes(again));
    }
}
