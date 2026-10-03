using System.Text.Json;
using Tyrant.Core.Tests;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Rpc.Tests;

public class DataMethodsTests
{
    private static DumpObject Animal(string name, string fields) =>
        new(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", name, 1),
            $$"""{"$type":"PrehistoricKingdom.AnimalData","$name":"{{name}}","$id":1,{{fields}}}""");

    private static void WriteDump(string ws, string requestId, IEnumerable<DumpObject> objects, IEnumerable<LanguageTable>? languages = null)
    {
        var data = Path.Combine(ws, "data");
        if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        var result = new DumpResult();
        result.Objects.AddRange(objects);
        DumpWriter.Write(data, result, languages ?? [], new DumpManifest { RequestId = requestId, BuildGuid = "b", CreatedUtc = "2026-10-02T10:00:00Z" });
    }

    private static async Task<(RpcHarness Harness, string Workspace)> WithDump(FakeGame game, IEnumerable<DumpObject> objects, IEnumerable<LanguageTable>? languages = null)
    {
        var harness = new RpcHarness(TestStudio.Options());
        var ws = TestStudio.TempDir();
        await harness.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        WriteDump(ws, "r1", objects, languages);
        return (harness, ws);
    }

    private static IEnumerable<DumpObject> ManyAnimals(int count) =>
        Enumerable.Range(0, count).Select(i => Animal($"Animal{i:00}",
            string.Join(",", Enumerable.Range(0, 15).Select(f => $"\"field{f:00}\":{i * f}"))));

    private static List<string> Names(JsonElement result) =>
        result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToList();

    [Fact]
    public async Task Types_lists_dumped_types_with_counts()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, ManyAnimals(3));

        var result = await h.Call("data.types");

        var type = Assert.Single(result.GetProperty("types").EnumerateArray());
        Assert.Equal("AnimalData", type.GetProperty("shortName").GetString());
        Assert.Equal(3, type.GetProperty("count").GetInt32());
        Assert.Equal("2026-10-02T10:00:00Z", result.GetProperty("createdUtc").GetString());
    }

    [Fact]
    public async Task Query_returns_default_columns_and_pages()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, ManyAnimals(30));

        var result = await h.Call("data.query", new { type = "AnimalData", page = 2, pageSize = 10 });

        Assert.Equal(16, result.GetProperty("allColumns").GetArrayLength());
        var columns = result.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Equal(12, columns.Count);
        Assert.Equal("$name", columns[0]);
        Assert.Equal(30, result.GetProperty("total").GetInt32());
        Assert.Equal("Animal20", Names(result)[0]);
        Assert.Equal(12, result.GetProperty("rows")[0].GetProperty("values").GetArrayLength());
    }

    [Fact]
    public async Task Query_past_the_last_page_is_empty_not_an_error()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, ManyAnimals(3));

        var result = await h.Call("data.query", new { type = "AnimalData", page = 9 });

        Assert.Empty(Names(result));
        Assert.Equal(3, result.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Query_filters_sorts_and_selects_columns()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, [
            Animal("Stego", "\"cost\":100,\"diet\":\"Herbivore\""),
            Animal("Ankylo", "\"cost\":9,\"diet\":\"Herbivore\""),
            Animal("Rex", "\"cost\":10,\"diet\":\"Carnivore\""),
        ]);

        var result = await h.Call("data.query", new { type = "AnimalData", filter = "herb", sort = "cost", columns = new[] { "cost", "nope" } });

        Assert.Equal(["Ankylo", "Stego"], Names(result));
        Assert.Equal(["$name", "cost"], result.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToArray());
        Assert.Equal("9", result.GetProperty("rows")[0].GetProperty("values")[1].GetString());
    }

    [Fact]
    public async Task Query_keeps_non_ascii_text()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, [Animal("Tri", "\"displayName\":\"Tricératops 三角龍\"")]);

        var result = await h.Call("data.query", new { type = "AnimalData", filter = "三角" });

        Assert.Equal("Tricératops 三角龍", result.GetProperty("rows")[0].GetProperty("values")[1].GetString());
    }

    [Fact]
    public async Task Query_without_a_dump_reports_data_missing_with_a_fix()
    {
        using var game = new FakeGame();
        var h = new RpcHarness(TestStudio.Options());
        await h.Call("workspace.create", new { dir = TestStudio.TempDir(), gamePath = game.Root });

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("data.query", new { type = "AnimalData" }));

        Assert.Equal("DATA_MISSING", ex.DataCode);
        Assert.Equal("REFRESH_WORKSPACE", ex.Fix);
    }

    [Fact]
    public async Task Objects_and_object_return_names_and_full_json()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, [Animal("Stego", "\"cost\":100"), Animal("Rex", "\"cost\":10")]);

        var names = await h.Call("data.objects", new { type = "AnimalData", filter = "ste" });
        var obj = await h.Call("data.object", new { type = "AnimalData", name = "Stego" });

        Assert.Equal(["Stego"], names.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToArray());
        Assert.Equal(100, obj.GetProperty("json").GetProperty("cost").GetInt32());
    }

    [Fact]
    public async Task Compare_can_show_only_the_differences()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, [Animal("Stego", "\"cost\":100,\"diet\":\"Herbivore\""), Animal("Ankylo", "\"cost\":9,\"diet\":\"Herbivore\"")]);

        var all = await h.Call("data.compare", new { type = "AnimalData", names = new[] { "Stego", "Ankylo" } });
        var diff = await h.Call("data.compare", new { type = "AnimalData", names = new[] { "Stego", "Ankylo" }, onlyDifferences = true });

        Assert.Equal(["cost", "diet"], all.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).ToArray());
        Assert.Equal(["cost"], diff.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).ToArray());
        Assert.Equal(["100", "9"], diff.GetProperty("values")[0].EnumerateArray().Select(v => v.GetString()!).ToArray());
    }

    [Fact]
    public async Task Compare_needs_at_least_two_objects()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, [Animal("Stego", "\"cost\":1")]);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("data.compare", new { type = "AnimalData", names = new[] { "Stego" } }));

        Assert.Equal(-32602, ex.Code);
    }

    [Fact]
    public async Task Localization_lists_languages_and_searches_terms_and_text()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, [Animal("Stego", "\"cost\":1")], [
            new LanguageTable("en", "English", new Dictionary<string, string> { ["Animals/Stego"] = "Stegosaurus", ["UI/Ok"] = "OK" }),
            new LanguageTable("fr", "Français", new Dictionary<string, string> { ["Animals/Stego"] = "Stégosaure" }),
        ]);

        var languages = await h.Call("data.languages");
        var search = await h.Call("data.localization", new { filter = "stégo", languages = new[] { "fr" } });

        Assert.Equal(2, languages.GetProperty("languages")[0].GetProperty("termCount").GetInt32());
        Assert.Equal(["fr"], search.GetProperty("languages").EnumerateArray().Select(l => l.GetString()!).ToArray());
        var row = Assert.Single(search.GetProperty("rows").EnumerateArray());
        Assert.Equal("Animals/Stego", row.GetProperty("term").GetString());
        Assert.Equal("Stégosaure", row.GetProperty("values")[0].GetString());
    }

    [Fact]
    public async Task Export_writes_into_exports_by_default()
    {
        using var game = new FakeGame();
        var (h, ws) = await WithDump(game, [Animal("Stego", "\"cost\":1")]);

        var result = await h.Call("data.export", new { type = "AnimalData", format = "csv" });

        Assert.Equal(Path.Combine(Path.GetFullPath(ws), "exports", "AnimalData.csv"), result.GetProperty("path").GetString());
        Assert.Equal(1, result.GetProperty("count").GetInt32());
        Assert.True(File.Exists(result.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task Export_into_the_game_folder_is_refused()
    {
        using var game = new FakeGame();
        var (h, _) = await WithDump(game, [Animal("Stego", "\"cost\":1")]);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() =>
            h.Call("data.export", new { type = "AnimalData", format = "csv", path = Path.Combine(game.Root, "x.csv") }));

        Assert.Equal("OUTPUT_IN_GAME_FOLDER", ex.DataCode);
    }

    [Fact]
    public async Task A_new_dump_replaces_cached_tables()
    {
        using var game = new FakeGame();
        var (h, ws) = await WithDump(game, [Animal("Stego", "\"cost\":1")]);
        Assert.Equal(1, (await h.Call("data.query", new { type = "AnimalData" })).GetProperty("total").GetInt32());

        WriteDump(ws, "r2", [Animal("Stego", "\"cost\":1"), Animal("Rex", "\"cost\":2")]);

        Assert.Equal(2, (await h.Call("data.query", new { type = "AnimalData" })).GetProperty("total").GetInt32());
    }
}
