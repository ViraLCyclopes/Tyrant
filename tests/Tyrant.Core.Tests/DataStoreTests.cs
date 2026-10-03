using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Core.Tests;

public class DataStoreTests
{
    private static string DumpDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        var result = new DumpResult();
        void Add(string type, string name, string json) => result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo(type, name, 1), json));
        Add("PrehistoricKingdom.AnimalData", "Stegosaurus",
            """{"$type":"PrehistoricKingdom.AnimalData","$name":"Stegosaurus","$id":1,"cost":1200,"displayName":"Stego, \"plated\"","growth":{"min":1,"max":2},"tags":[1,2],"heroRender":{"$ref":{"type":"UnityEngine.Sprite","name":"Hero","id":9}}}""");
        Add("PrehistoricKingdom.AnimalData", "Gallimimus",
            """{"$type":"PrehistoricKingdom.AnimalData","$name":"Gallimimus","$id":2,"cost":800,"notes":"line1\nline2"}""");
        Add("PrehistoricKingdom.AnimalDatabase", "AnimalDatabase", """{"$type":"PrehistoricKingdom.AnimalDatabase","$name":"AnimalDatabase","$id":3}""");
        Add("Other.AnimalData", "Clash", """{"$type":"Other.AnimalData","$name":"Clash","$id":4}""");
        DumpWriter.Write(dir, result, [], new DumpManifest { RequestId = "r" });
        return dir;
    }

    [Fact]
    public void Lists_types_with_counts_and_short_names()
    {
        var types = DataStore.OpenDirectory(DumpDir()).Types();

        var animal = types.Single(t => t.FullName == "PrehistoricKingdom.AnimalData");
        Assert.Equal("AnimalData", animal.ShortName);
        Assert.Equal(2, animal.Count);
    }

    [Fact]
    public void Finds_types_by_full_or_unique_short_name()
    {
        var store = DataStore.OpenDirectory(DumpDir());

        Assert.Equal("PrehistoricKingdom.AnimalDatabase", store.FindType("animaldatabase").FullName);
        Assert.Equal("PrehistoricKingdom.AnimalData", store.FindType("PrehistoricKingdom.AnimalData").FullName);
        Assert.Equal(TyrantErrorCode.AssetAmbiguous, Assert.Throws<TyrantException>(() => store.FindType("AnimalData")).Code);
        Assert.Equal(TyrantErrorCode.AssetNotFound, Assert.Throws<TyrantException>(() => store.FindType("Nope")).Code);
    }

    [Fact]
    public void Loads_objects_by_name()
    {
        var store = DataStore.OpenDirectory(DumpDir());
        var type = store.FindType("PrehistoricKingdom.AnimalData");

        Assert.Equal(new[] { "Gallimimus", "Stegosaurus" }, store.ObjectNames(type));
        Assert.Equal(1200, store.Load(type, "stegosaurus").GetProperty("cost").GetInt32());
        Assert.Equal(TyrantErrorCode.AssetNotFound, Assert.Throws<TyrantException>(() => store.Load(type, "Trex")).Code);
    }

    [Fact]
    public void Csv_flattens_nested_values_and_escapes_text()
    {
        var store = DataStore.OpenDirectory(DumpDir());
        var csv = DataStore.ToCsv(store.LoadAll(store.FindType("PrehistoricKingdom.AnimalData")));
        var lines = csv.Split("\r\n");

        Assert.Equal("$name,cost,notes,displayName,growth.min,growth.max,tags,heroRender", lines[0]);
        Assert.Contains("\"Stego, \"\"plated\"\"\"", csv);
        Assert.Contains("\"line1\nline2\"", csv);
        Assert.Contains("Hero", csv);              // references show the referenced object's name
        Assert.Contains("\"[1,2]\"", csv);         // arrays become compact JSON
    }

    [Fact]
    public void Missing_dump_reports_data_missing()
    {
        var empty = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        var ex = Assert.Throws<TyrantException>(() => DataStore.OpenDirectory(empty));
        Assert.Equal(TyrantErrorCode.DataMissing, ex.Code);
        Assert.Contains("tyrant dump run", ex.Message);
    }

    [Fact]
    public void Csv_keeps_non_ascii_text_readable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        var result = new DumpResult();
        result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("T.Animal", "Tri", 1),
            """{"$type":"T.Animal","$name":"Tri","$id":1,"name":"Tricératops","tags":["日本","<b>"]}"""));
        DumpWriter.Write(dir, result, [], new DumpManifest { RequestId = "r" });
        var store = DataStore.OpenDirectory(dir);

        var csv = DataStore.ToCsv(store.LoadAll(store.FindType("T.Animal")));

        Assert.Contains("Tricératops", csv);
        Assert.Contains("日本", csv);
        Assert.Contains("<b>", csv);
    }

    [Fact]
    public void Table_puts_name_first_and_flattens_like_the_csv()
    {
        var table = DataTable.From([
            ("Stego", JsonDocument.Parse("""{"$type":"T","$name":"Stego","$id":1,"cost":10,"stats":{"hp":5}}""").RootElement),
            ("Rex", JsonDocument.Parse("""{"$type":"T","$name":"Rex","$id":2,"cost":20,"diet":"Carnivore"}""").RootElement),
        ]);

        Assert.Equal(["$name", "cost", "stats.hp", "diet"], table.Columns);
        Assert.Equal("Rex", table.Rows[1].Get("$name"));
        Assert.Equal("", table.Rows[0].Get("diet"));
        Assert.StartsWith("$name,cost,stats.hp,diet\r\nStego,10,5,\r\n", table.ToCsv());
    }

    [Fact]
    public void Localization_tables_are_read_from_the_dump()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        DumpWriter.Write(dir, new DumpResult(),
            [new LanguageTable("fr", "Français", new Dictionary<string, string> { ["Animals/Stego"] = "Stégosaure" })],
            new DumpManifest { RequestId = "r" });

        var table = Assert.Single(DataStore.OpenDirectory(dir).LoadLocalization());

        Assert.Equal("fr", table.Code);
        Assert.Equal("Français", table.Name);
        Assert.Equal("Stégosaure", table.Terms["Animals/Stego"]);
    }

    [Fact]
    public void Export_writes_csv_with_a_bom_or_json_and_rejects_other_formats()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        var result = new DumpResult();
        result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("T.Animal", "Tri", 1), """{"$type":"T.Animal","$name":"Tri","$id":1,"name":"Tricératops"}"""));
        DumpWriter.Write(dir, result, [], new DumpManifest { RequestId = "r" });
        var store = DataStore.OpenDirectory(dir);
        var type = store.FindType("Animal");
        var csv = Path.Combine(dir, "..", "out", "a.csv");
        var json = Path.Combine(dir, "..", "out", "a.json");

        Assert.Equal(1, store.Export(type, "csv", csv));
        Assert.Equal(1, store.Export(type, "json", json));

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(csv).Take(3).ToArray());
        Assert.Contains("Tricératops", File.ReadAllText(json));
        Assert.Throws<ArgumentException>(() => store.Export(type, "xml", Path.Combine(dir, "..", "out", "a.xml")));
    }
}
