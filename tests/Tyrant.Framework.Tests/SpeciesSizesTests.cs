using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SpeciesSizesTests
{
    private static string Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "mod");
        Directory.CreateDirectory(Path.Combine(dir, SizeSettings.Folder));
        return dir;
    }

    private static LoadedMod Mod(string id, string? sizes, string extra = "")
    {
        var dir = Dir();
        if (sizes != null) File.WriteAllText(Path.Combine(dir, "sizes", "x.json"), sizes);
        return new LoadedMod(ModManifest.Parse($$"""{"format":1,"id":"{{id}}"{{extra}}}"""), dir);
    }

    [Fact]
    public void Parses_all_keys_and_round_trips()
    {
        var s = SizeSettings.Parse("""{ "format": 1, "target": "Tyrannosaurus", "adult": 0.85, "baby": 0.1, "male": 1.0, "female": 1.1 }""");

        Assert.Equal("Tyrannosaurus", s.Target);
        Assert.Equal((0.85, 0.1, 1.0, 1.1), (s.Adult, s.Baby, s.Male, s.Female));
        var again = SizeSettings.Parse(s.ToJson());
        Assert.Equal((s.Target, s.Adult, s.Baby, s.Male, s.Female), (again.Target, again.Adult, again.Baby, again.Male, again.Female));
        Assert.Equal("tyrannosaurus.json", SizeSettings.FileName("Tyrannosaurus"));
    }

    [Fact]
    public void Missing_keys_mean_unchanged()
    {
        var s = SizeSettings.Parse("""{ "format": 1, "target": "Tyrannosaurus", "baby": 0.5 }""");

        Assert.Null(s.Adult);
        Assert.Null(s.Male);
        Assert.Null(s.Female);
        Assert.Equal(0.5, s.Baby);
        Assert.DoesNotContain("adult", s.ToJson());
    }

    [Theory]
    [InlineData("adult", "0.04")]
    [InlineData("adult", "5.1")]
    [InlineData("baby", "0")]
    [InlineData("baby", "1.01")]
    [InlineData("male", "0.09")]
    [InlineData("female", "5.1")]
    public void Rejects_out_of_range(string key, string value)
    {
        var ex = Assert.Throws<ManifestException>(() => SizeSettings.Parse($$"""{ "format": 1, "target": "A", "{{key}}": {{value}} }"""));

        Assert.Contains($"\"{key}\"", ex.Message);
    }

    [Theory]
    [InlineData("[1.2, 1.1]")]
    [InlineData("[0, 1]")]
    [InlineData("[1, 10.1]")]
    [InlineData("[1]")]
    public void Range_needs_min_le_max_and_bounds(string range)
    {
        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse("{\"format\":1,\"id\":\"mod-a\",\"sizeRanges\":{\"nursery\":" + range + "}}"));

        Assert.Contains("sizeRanges.nursery", ex.Message);
    }

    [Fact]
    public void Unknown_key_is_an_error()
    {
        var ex = Assert.Throws<ManifestException>(() => SizeSettings.Parse("""{ "format": 1, "target": "A", "adlut": 1 }"""));
        Assert.Contains("adlut", ex.Message);
        Assert.Throws<ManifestException>(() => ModManifest.Parse("""{"format":1,"id":"mod-a","sizeRanges":{"nurseyr":[1,2]}}"""));
    }

    [Fact]
    public void Later_mod_wins_per_key()
    {
        var a = Mod("mod-a", """{"format":1,"target":"Tyrannosaurus","adult":0.8,"baby":0.2}""");
        var b = Mod("mod-b", """{"format":1,"target":"tyrannosaurus","adult":1.2}""");

        var table = SizeTable.Build([a, b], out var messages);

        var merged = table.For("Tyrannosaurus")!;
        Assert.Equal((1.2, 0.2), (merged.Adult, merged.Baby));
        Assert.Null(merged.Male);
        Assert.Null(table.For("Allosaurus"));
        Assert.True(table.HasAny);
        Assert.Empty(messages);
    }

    [Fact]
    public void Ranges_merge_per_range()
    {
        var a = Mod("mod-a", null, ""","sizeRanges":{"nursery":[0.5,1.5],"breeding":[0.7,1.3]}""");
        var b = Mod("mod-b", null, ""","sizeRanges":{"breeding":[0.9,1.1]}""");

        var table = SizeTable.Build([a, b], out _);

        Assert.Equal([0.5, 1.5], table.Ranges.Nursery!);
        Assert.Equal([0.9, 1.1], table.Ranges.Breeding!);
        var again = ModManifest.Parse(a.Manifest.ToJson());
        Assert.Equal([0.5, 1.5], again.SizeRanges!.Nursery!);
        Assert.DoesNotContain("sizeRanges", ModManifest.Parse("""{"format":1,"id":"mod-c"}""").ToJson());
    }

    [Fact]
    public void A_broken_or_duplicate_file_is_skipped_and_noted()
    {
        var dir = Dir();
        const string t = """{"format":1,"target":"A","adult":1}""";
        File.WriteAllText(Path.Combine(dir, "sizes", "a.json"), t);
        File.WriteAllText(Path.Combine(dir, "sizes", "b.json"), t);
        File.WriteAllText(Path.Combine(dir, "sizes", "c.json"), """{"format":1,"target":"C","adult":9}""");
        var problems = new List<string>();

        var read = SizeSettings.ReadFolder(dir, problems);

        Assert.Equal(["a.json"], read.Select(r => r.File));
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("b.json") && p.Contains("a.json"));
        Assert.Contains(problems, p => p.Contains("c.json") && p.Contains("\"adult\""));
        Assert.Empty(SizeSettings.ReadFolder(Dir() + "-missing", null));
    }
}
