using System.Text.Json;
using PK.Core.Data;
using PK.Core.Errors;
using PK.Dumper.Serialization;

namespace PK.Core.Tests;

public class DataStoreTests
{
    private static string DumpDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"), "data");
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
        Assert.Equal(PkErrorCode.AssetAmbiguous, Assert.Throws<PkException>(() => store.FindType("AnimalData")).Code);
        Assert.Equal(PkErrorCode.AssetNotFound, Assert.Throws<PkException>(() => store.FindType("Nope")).Code);
    }

    [Fact]
    public void Loads_objects_by_name()
    {
        var store = DataStore.OpenDirectory(DumpDir());
        var type = store.FindType("PrehistoricKingdom.AnimalData");

        Assert.Equal(new[] { "Gallimimus", "Stegosaurus" }, store.ObjectNames(type));
        Assert.Equal(1200, store.Load(type, "stegosaurus").GetProperty("cost").GetInt32());
        Assert.Equal(PkErrorCode.AssetNotFound, Assert.Throws<PkException>(() => store.Load(type, "Trex")).Code);
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
        var empty = Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        var ex = Assert.Throws<PkException>(() => DataStore.OpenDirectory(empty));
        Assert.Equal(PkErrorCode.DataMissing, ex.Code);
        Assert.Contains("pk dump run", ex.Message);
    }

    [Fact]
    public void Csv_keeps_non_ascii_text_readable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"), "data");
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
}
