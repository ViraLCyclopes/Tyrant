using System.Text.Json;
using PK.Dumper.Serialization;

namespace PK.Dumper.Tests;

public class DumpWriterTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"), "out");

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

        var typeDir = Path.Combine(dir, "objects", "PK.Dumper.Tests.TestAnimalData");
        Assert.True(File.Exists(Path.Combine(typeDir, "Stego.json")));
        Assert.True(File.Exists(Path.Combine(typeDir, "Stego_3.json")));
        Assert.True(File.Exists(Path.Combine(typeDir, "a_b_c.json")));
        Assert.True(File.Exists(Path.Combine(dir, "objects", "PK.Dumper.Tests.TestDatabase", "AnimalDatabase.json")));

        var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "schema", "PK.Dumper.Tests.TestAnimalData.json"))).RootElement;
        var fields = schema.GetProperty("fields").EnumerateArray().ToList();
        Assert.Contains(fields, f => f.GetProperty("name").GetString() == "cost" && f.GetProperty("type").GetString() == "System.Int32");
        var period = fields.Single(f => f.GetProperty("name").GetString() == "period");
        Assert.Equal(new[] { "Jurassic", "Cretaceous" }, period.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));

        var en = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "localization", "en.json"))).RootElement;
        Assert.Equal("Stegosaurus", en.GetProperty("terms").GetProperty("ANIMAL/STEGO").GetString());

        var m = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, DumpWriter.ManifestFile))).RootElement;
        Assert.Equal(1, m.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("req1", m.GetProperty("requestId").GetString());
        Assert.Equal(3, m.GetProperty("counts").GetProperty("PK.Dumper.Tests.TestAnimalData").GetInt32());
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
}
