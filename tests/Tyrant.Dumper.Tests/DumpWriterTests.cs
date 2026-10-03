using System.Text.Json;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Dumper.Tests;

public class DumpWriterTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "out");

    private static DumpResult Sample()
    {
        var db = new TestDatabase { name = "AnimalDatabase", id = 1 };
        db.animals.Add(new TestAnimalData { name = "Stego", id = 2 });
        db.animals.Add(new TestAnimalData { name = "Stego", id = 3 });   // duplicate name
        db.animals.Add(new TestAnimalData { name = "a/b:c", id = 4 });   // unsafe name
        var result = new DumpCollector(new FakeAdapter()).Collect([db]);
        result.Errors.Add("something odd");
        return result;
    }

    [Fact]
    public void Writes_objects_schema_localization_and_manifest()
    {
        var dir = TempDir();
        var manifest = new DumpManifest { RequestId = "req1", BuildGuid = "build", DumperVersion = "1.0.0", CreatedUtc = "2026-10-02T00:00:00Z" };
        LanguageTable[] languages = [new("en", "English", new() { ["ANIMAL/STEGO"] = "Stegosaurus" })];

        DumpWriter.Write(dir, Sample(), languages, manifest);

        var typeDir = Path.Combine(dir, "objects", "Tyrant.Dumper.Tests.TestAnimalData");
        Assert.True(File.Exists(Path.Combine(typeDir, "Stego.json")));
        Assert.True(File.Exists(Path.Combine(typeDir, "Stego_2.json")));
        Assert.True(File.Exists(Path.Combine(typeDir, "a_b_c.json")));
        Assert.True(File.Exists(Path.Combine(dir, "objects", "Tyrant.Dumper.Tests.TestDatabase", "AnimalDatabase.json")));

        var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "schema", "Tyrant.Dumper.Tests.TestAnimalData.json"))).RootElement;
        var fields = schema.GetProperty("fields").EnumerateArray().ToList();
        Assert.Contains(fields, f => f.GetProperty("name").GetString() == "cost" && f.GetProperty("type").GetString() == "System.Int32");
        var period = fields.Single(f => f.GetProperty("name").GetString() == "period");
        Assert.Equal(new[] { "Jurassic", "Cretaceous" }, period.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));

        var en = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "localization", "en.json"))).RootElement;
        Assert.Equal("Stegosaurus", en.GetProperty("terms").GetProperty("ANIMAL/STEGO").GetString());

        var m = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, DumpWriter.ManifestFile))).RootElement;
        Assert.Equal(1, m.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("req1", m.GetProperty("requestId").GetString());
        Assert.Equal(3, m.GetProperty("counts").GetProperty("Tyrant.Dumper.Tests.TestAnimalData").GetInt32());
        Assert.Equal("en", m.GetProperty("languages")[0].GetString());
        Assert.Contains("something odd", m.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData("Stego", "Stego")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("..", "_")]
    [InlineData("", "_")]
    public void Safe_names_are_valid_file_names(string input, string expected)
    {
        Assert.Equal(expected, DumpWriter.SafeName(input));
    }

    [Fact]
    public void Very_long_names_are_shortened()
    {
        Assert.True(DumpWriter.SafeName(new string('x', 500)).Length <= 120);
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_reported_and_the_manifest_is_still_written()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "objects", "Tyrant.Dumper.Tests.TestAnimalData", "Stego.json")); // folder in the way

        DumpWriter.Write(dir, Sample(), [], new DumpManifest { RequestId = "req" });

        var m = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, DumpWriter.ManifestFile))).RootElement;
        Assert.Contains(m.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("Stego"));
        Assert.True(File.Exists(Path.Combine(dir, "objects", "Tyrant.Dumper.Tests.TestAnimalData", "a_b_c.json")));
    }

    [Theory]
    [InlineData("CON", "CON_")]
    [InlineData("nul", "nul_")]
    [InlineData("Com1", "Com1_")]
    [InlineData("LPT9", "LPT9_")]
    public void Reserved_device_names_are_made_safe(string input, string expected)
    {
        Assert.Equal(expected, DumpWriter.SafeName(input));
    }

    [Fact]
    public void Objects_with_the_same_name_get_the_same_file_names_in_every_run()
    {
        // Unity instance ids change between runs; the file a duplicate lands in must not.
        static Dictionary<string, string> FilesByContent(int idA, int idB, bool swapOrder)
        {
            var a = new DumpObject(typeof(object), new EngineObjectInfo("T", "Same", idA), $$"""{"$type":"T","$name":"Same","$id":{{idA}},"v":1}""");
            var b = new DumpObject(typeof(object), new EngineObjectInfo("T", "Same", idB), $$"""{"$type":"T","$name":"Same","$id":{{idB}},"v":2}""");
            var result = new DumpResult();
            result.Objects.AddRange(swapOrder ? new[] { b, a } : new[] { a, b });
            var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
            DumpWriter.Write(dir, result, [], new DumpManifest { RequestId = "r" });
            return Directory.GetFiles(Path.Combine(dir, "objects", "T")).ToDictionary(f => File.ReadAllText(f).Contains("\"v\":1") ? "v1" : "v2", f => Path.GetFileName(f)!);
        }

        var first = FilesByContent(111, 222, swapOrder: false);
        var second = FilesByContent(-9876, 5, swapOrder: true);

        Assert.Equal(first, second);
        Assert.DoesNotContain("111", string.Join(",", first.Values));
    }
}
