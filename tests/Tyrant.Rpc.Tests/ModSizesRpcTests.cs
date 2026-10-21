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

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.Equal("MOD_INVALID", range.DataCode);
    }

    [Fact]
    public async Task Check_judges_a_species_against_tyrants_20x()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus Anax", 1), AnaxData));
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalsV2.Data.GlobalAnimalParameters", "GlobalAnimalParameters", 2), GameRanges));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
        await h.Call("mods.create", new { id = "wide-nursery" });
        var detail = await h.Call("mods.get", new { id = "wide-nursery" });

        // The species' own Nursery range: females 1.35 x 15 = 20.25, past Tyrant's 20.
        var set = await h.Call("mods.setSizes", new { id = "wide-nursery", revision = Rev(detail), target = "Allosaurus Anax", nursery = new[] { 0.5, 15.0 } });
        var check = await h.Call("mods.check", new { id = "wide-nursery" });
        Assert.Contains(check.GetProperty("errors").EnumerateArray(), e => e.GetString() ==
            "sizes/allosaurus anax.json: The biggest Allosaurus Anax can reach 20.25× a normal Allosaurus Anax (adult 1 × female 1.35 × Nursery slider up to 15), more than Tyrant's 20× limit; lower the adult size or the size ranges.");
        Assert.False(set.TryGetProperty("notes", out _)); // nothing is raised: the limit is automatic

        // Within 20: only the past-2x caution.
        await h.Call("mods.setSizes", new { id = "wide-nursery", revision = Rev(set), target = "Allosaurus Anax", nursery = new[] { 0.5, 1.5 } });
        check = await h.Call("mods.check", new { id = "wide-nursery" });
        Assert.DoesNotContain(check.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("Allosaurus Anax"));
        Assert.Contains(check.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("Allosaurus Anax can grow past the game's normal 2× limit"));
    }

    [Fact]
    public async Task Sizes_carry_the_species_ranges_names_and_curve_with_the_games()
    {
        using var game = new FakeGame();
        var (h, _) = await WithAlpha(game);
        var detail = await h.Call("mods.get", new { id = "size-pack" });
        static IEnumerable<double> Pair(JsonElement e, string name) => e.GetProperty(name).EnumerateArray().Select(x => Math.Round(x.GetDouble(), 6));

        // Unset: null, with the game's values to show (the dump has no global parameters here: the vanilla ones). No limit: it is automatic.
        var unset = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });
        Assert.True(IsNull(unset, "nursery"));
        Assert.True(IsNull(unset, "breeding"));
        Assert.False(unset.TryGetProperty("limit", out _));
        Assert.False(unset.TryGetProperty("gameLimit", out _));
        Assert.Equal([0.925, 1.075], Pair(unset, "gameNursery"));
        Assert.Equal([0.85, 1.15], Pair(unset, "gameBreeding"));
        Assert.Equal("Alpha", unset.GetProperty("name").GetString());
        Assert.Equal("Alpha", unset.GetProperty("baseName").GetString());
        // The game's curve as Tyrant reads it, so the card can count the smallest and biggest as the sizes change.
        var curve = unset.GetProperty("original").GetProperty("curve").EnumerateArray().Select(k => (k[0].GetDouble(), k[1].GetDouble())).ToList();
        Assert.Equal((0.0, 0.5), curve[0]);
        Assert.Equal((1.0, 1.0), curve[^1]);

        var set = await h.Call("mods.setSizes", new
        {
            id = "size-pack", revision = Rev(detail), target = "Alpha", adult = 1.2, nursery = new[] { 0.5, 3.0 }, breeding = new[] { 0.8, 1.2 },
        });
        var sizes = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });
        Assert.Equal([0.5, 3], Pair(sizes, "nursery"));
        Assert.Equal([0.8, 1.2], Pair(sizes, "breeding"));
        Assert.Equal(1.2, sizes.GetProperty("adult").GetDouble());

        // Leaving a key out clears it (the card's Reset).
        await h.Call("mods.setSizes", new { id = "size-pack", revision = Rev(set), target = "Alpha", adult = 1.2, nursery = new[] { 0.5, 3.0 } });
        var reset = await h.Call("mods.sizes", new { id = "size-pack", target = "Alpha" });
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
}
