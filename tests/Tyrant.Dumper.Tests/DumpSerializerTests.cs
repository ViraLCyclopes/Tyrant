using System.Text.Json;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Dumper.Tests;

public class DumpSerializerTests
{
    private static JsonElement Serialize(object root, List<object>? dumpables = null) =>
        JsonDocument.Parse(new DumpSerializer(new FakeAdapter(), o => dumpables?.Add(o)).SerializeRoot(root)).RootElement;

    private static TestAnimalData Stego()
    {
        var stego = new TestAnimalData
        {
            name = "Stegosaurus", id = 7, speciesID = "stego", period = Period.Jurassic, diet = Diet.Plants | Diet.Meat,
            size = new Float3 { x = 9, y = 4, z = 2.5f }, excavations = [1, 2], weights = new() { ["adult"] = 0.75f },
            preferences = new Preferences { min = 10, max = 30 }, heroRender = new FakeTexture { name = "Hero", id = 99 },
            description = new FakeLocalization { name = "LOCALDB_stego", id = 3, english = "A plated dinosaur." },
            ratio = 0.1f, Points = 5,
        };
        stego.SetCost(1200);
        stego.AddOdin(Period.Cretaceous, 4);
        return stego;
    }

    [Fact]
    public void Writes_header_and_unity_and_odin_serialized_fields_only()
    {
        var json = Serialize(Stego());

        Assert.Equal("Tyrant.Dumper.Tests.TestAnimalData", json.GetProperty("$type").GetString());
        Assert.Equal("Stegosaurus", json.GetProperty("$name").GetString());
        Assert.Equal(7, json.GetProperty("$id").GetInt64());
        Assert.Equal("stego", json.GetProperty("speciesID").GetString());
        Assert.Equal(1200, json.GetProperty("cost").GetInt32());
        Assert.Equal(4, json.GetProperty("odinOnly").GetProperty("Cretaceous").GetInt32());
        Assert.Equal(5, json.GetProperty("Points").GetInt32());
        Assert.False(json.TryGetProperty("secret", out _));
        Assert.False(json.TryGetProperty("runtimeOnly", out _));
        Assert.False(json.TryGetProperty("StaticValue", out _));
        Assert.False(json.TryGetProperty("name", out _)); // engine base-class fields are not data
    }

    [Fact]
    public void Writes_enums_structs_lists_dictionaries_and_floats()
    {
        var json = Serialize(Stego());

        Assert.Equal("Jurassic", json.GetProperty("period").GetString());
        Assert.Equal("Plants, Meat", json.GetProperty("diet").GetString());
        Assert.Equal(2.5, json.GetProperty("size").GetProperty("z").GetDouble());
        Assert.Equal(new[] { 1, 2 }, json.GetProperty("excavations").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(0.75, json.GetProperty("weights").GetProperty("adult").GetDouble());
        Assert.Equal(30, json.GetProperty("preferences").GetProperty("max").GetDouble());
        Assert.Equal("0.1", json.GetProperty("ratio").GetRawText());
    }

    [Fact]
    public void Engine_objects_become_references_and_data_assets_are_reported()
    {
        var dumpables = new List<object>();
        var stego = Stego();
        stego.relative = new TestAnimalData { name = "Kentrosaurus", id = 8 };

        var json = Serialize(stego, dumpables);

        var hero = json.GetProperty("heroRender").GetProperty("$ref");
        Assert.Equal("Hero", hero.GetProperty("name").GetString());
        Assert.Equal(99, hero.GetProperty("id").GetInt64());
        Assert.Equal("Kentrosaurus", json.GetProperty("relative").GetProperty("$ref").GetProperty("name").GetString());
        Assert.Same(stego.relative, Assert.Single(dumpables));
    }

    [Fact]
    public void Inline_assets_are_embedded()
    {
        var description = Serialize(Stego()).GetProperty("description");
        Assert.Equal("LOCALDB_stego", description.GetProperty("$name").GetString());
        Assert.Equal("A plated dinosaur.", description.GetProperty("english").GetString());
    }

    [Fact]
    public void Cycles_and_deep_nesting_are_cut_off()
    {
        var a = new Node { value = 1 };
        a.next = new Node { value = 2, next = a };
        var deep = new Node();
        var cursor = deep;
        for (var i = 0; i < 100; i++) cursor = cursor.next = new Node { value = i };

        var cyclic = JsonDocument.Parse(new DumpSerializer(new FakeAdapter(), _ => { }).SerializeRoot(new Wrapper(a))).RootElement;
        var tooDeep = new DumpSerializer(new FakeAdapter(), _ => { }).SerializeRoot(new Wrapper(deep));

        Assert.Equal("$cycle", cyclic.GetProperty("node").GetProperty("next").GetProperty("next").GetString());
        Assert.Contains("$truncated", tooDeep);
        JsonDocument.Parse(tooDeep);
    }

    [Fact]
    public void Destroyed_engine_objects_and_special_values_are_handled()
    {
        var stego = Stego();
        stego.heroRender = new FakeTexture { name = "Gone", id = 5, destroyed = true };
        stego.ratio = float.NaN;

        var json = Serialize(stego);

        Assert.Equal("(destroyed)", json.GetProperty("heroRender").GetProperty("$ref").GetProperty("name").GetString());
        Assert.Equal("NaN", json.GetProperty("ratio").GetString());
    }

    [Fact]
    public void Strings_are_escaped_into_valid_json()
    {
        var json = Serialize(new TestAnimalData { name = "q\"uote\\\n\u0001", speciesID = "tab\there" });
        Assert.Equal("q\"uote\\\n\u0001", json.GetProperty("$name").GetString());
        Assert.Equal("tab\there", json.GetProperty("speciesID").GetString());
    }

    [Fact]
    public void Collector_dumps_each_referenced_asset_once()
    {
        var shared = new TestAnimalData { name = "Shared", id = 2 };
        var db = new TestDatabase { name = "AnimalDatabase", id = 1, animals = [Stego(), shared] };
        db.animals[0].relative = shared;

        var result = new DumpCollector(new FakeAdapter()).Collect([db, db]);

        Assert.Equal(new[] { "AnimalDatabase", "Stegosaurus", "Shared" }, result.Objects.Select(o => o.Name));
        Assert.Empty(result.Errors);
        Assert.Equal(typeof(TestAnimalData), result.Objects[1].ClrType);
    }

    [Fact]
    public void Collector_records_errors_and_stops_at_the_object_limit()
    {
        var db = new TestDatabase { name = "Big", id = 1 };
        for (var i = 0; i < 10; i++) db.animals.Add(new TestAnimalData { name = $"A{i}", id = 10 + i });

        var result = new DumpCollector(new FakeAdapter()).Collect([db], maxObjects: 4);

        Assert.Equal(4, result.Objects.Count);
        Assert.Contains(result.Errors, e => e.Contains("Stopped after 4"));
    }

    [Fact]
    public void A_collection_that_cannot_be_enumerated_fails_only_that_field()
    {
        var json = Serialize(new PartlyBroken { name = "GlobalAnimalParameters", id = 5 });

        Assert.Equal(1, json.GetProperty("before").GetInt32());
        Assert.StartsWith("$error: NotImplementedException", json.GetProperty("broken").GetString());
        Assert.Equal(2, json.GetProperty("after").GetProperty("ok").GetInt32());
    }

    public sealed class DiamondHolder(DiamondNode node) : FakeAsset
    {
        public DiamondNode node = node;
    }

    [Fact]
    public void Shared_references_cannot_blow_up_the_output()
    {
        DiamondNode? next = null;
        for (var i = 0; i < 40; i++) next = new DiamondNode { left = next, right = next };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var json = new DumpSerializer(new FakeAdapter(), _ => { }).SerializeRoot(new DiamondHolder(next!));

        Assert.Contains("$truncated", json);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"took {stopwatch.Elapsed}");
        JsonDocument.Parse(json);
    }

    public sealed class Wrapper(Node node) : FakeAsset
    {
        public Node node = node;
    }
}
