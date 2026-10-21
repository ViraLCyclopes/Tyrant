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

    private static LoadedMod Mod(string id, string? sizes)
    {
        var dir = Dir();
        if (sizes != null) File.WriteAllText(Path.Combine(dir, "sizes", "x.json"), sizes);
        return new LoadedMod(ModManifest.Parse($$"""{"format":1,"id":"{{id}}"}"""), dir);
    }

    [Fact]
    public void Parses_all_keys_and_round_trips()
    {
        var s = SizeSettings.Parse("""
            { "format": 1, "target": "Tyrannosaurus", "adult": 0.85, "baby": 0.1, "male": 1.0, "female": 1.1,
              "nursery": [0.5, 3], "breeding": [0.85, 1.15], "limit": [0.05, 5] }
            """);

        Assert.Equal("Tyrannosaurus", s.Target);
        Assert.Equal((0.85, 0.1, 1.0, 1.1), (s.Adult, s.Baby, s.Male, s.Female));
        Assert.Equal([0.5, 3], s.Nursery!);
        Assert.Equal([0.85, 1.15], s.Breeding!);
        Assert.Equal([0.05, 5], s.Limit!); // an old file's limit is read (Check names it), never written again
        var again = SizeSettings.Parse(s.ToJson());
        Assert.Equal((s.Target, s.Adult, s.Baby, s.Male, s.Female), (again.Target, again.Adult, again.Baby, again.Male, again.Female));
        Assert.Equal(s.Nursery, again.Nursery!);
        Assert.Equal(s.Breeding, again.Breeding!);
        Assert.Null(again.Limit);
        Assert.Equal("tyrannosaurus.json", SizeSettings.FileName("Tyrannosaurus"));
    }

    [Fact]
    public void Missing_keys_mean_unchanged()
    {
        var s = SizeSettings.Parse("""{ "format": 1, "target": "Tyrannosaurus", "baby": 0.5 }""");

        Assert.Null(s.Adult);
        Assert.Null(s.Male);
        Assert.Null(s.Female);
        Assert.Null(s.Nursery);
        Assert.Null(s.Breeding);
        Assert.Null(s.Limit);
        Assert.Equal(0.5, s.Baby);
        Assert.DoesNotContain("adult", s.ToJson());
        Assert.DoesNotContain("nursery", s.ToJson());
        Assert.DoesNotContain("limit", s.ToJson());

        // A file with only a range is not empty: it changes that species. One with only an old limit is: the limit is automatic now.
        Assert.True(SizeSettings.Parse("""{ "format": 1, "target": "A", "limit": [0.05, 5] }""").IsEmpty);
        Assert.False(SizeSettings.Parse("""{ "format": 1, "target": "A", "nursery": [0.5, 3] }""").IsEmpty);
        Assert.True(SizeSettings.Parse("""{ "format": 1, "target": "A" }""").IsEmpty);
    }

    [Theory]
    [InlineData("adult", "0.04")]
    [InlineData("adult", "20.1")]
    [InlineData("baby", "0")]
    [InlineData("baby", "1.01")]
    [InlineData("male", "0.09")]
    [InlineData("female", "20.1")]
    [InlineData("male", "20.1")]
    public void Rejects_out_of_range(string key, string value)
    {
        var ex = Assert.Throws<ManifestException>(() => SizeSettings.Parse($$"""{ "format": 1, "target": "A", "{{key}}": {{value}} }"""));

        Assert.Contains($"\"{key}\"", ex.Message);
    }

    [Theory]
    [InlineData("nursery", "[1.2, 1.1]")]
    [InlineData("nursery", "[0, 1]")]
    [InlineData("nursery", "[1, 20.1]")]
    [InlineData("nursery", "[1]")]
    [InlineData("breeding", "[1.2, 1.1]")]
    [InlineData("breeding", "[0, 1]")]
    [InlineData("breeding", "1")]
    public void Range_needs_min_le_max_and_bounds(string key, string range)
    {
        var ex = Assert.Throws<ManifestException>(() => SizeSettings.Parse($$"""{ "format": 1, "target": "A", "{{key}}": {{range}} }"""));

        Assert.Contains($"\"{key}\" must be [min, max]", ex.Message);
    }

    [Fact]
    public void Raised_maximums_reach_the_highest_limit()
    {
        var s = SizeSettings.Parse("""{ "format": 1, "target": "A", "adult": 20, "male": 20, "female": 20, "nursery": [0.25, 20], "breeding": [1, 20] }""");

        Assert.Equal((20.0, 20.0, 20.0), (s.Adult!.Value, s.Male!.Value, s.Female!.Value));
        Assert.Equal([0.25, 20], s.Nursery!);
        Assert.Equal([1, 20], s.Breeding!);
    }

    [Fact]
    public void An_old_limit_is_read_and_dropped_when_written()
    {
        var s = SizeSettings.Parse("""{ "format": 1, "target": "A", "adult": 2, "limit": [0.01, 20] }""");

        Assert.Equal([0.01, 20], s.Limit!);
        Assert.DoesNotContain("limit", s.ToJson());
        Assert.Null(SizeSettings.Parse(s.ToJson()).Limit);
    }

    [Theory]
    [InlineData("[0.009, 20]")]
    [InlineData("[0.06, 20]")]
    [InlineData("[0.05, 1.9]")]
    [InlineData("[0.05, 20.1]")]
    [InlineData("[0.05]")]
    public void Limit_has_absolute_bounds(string limit)
    {
        var ex = Assert.Throws<ManifestException>(() => SizeSettings.Parse("{ \"format\": 1, \"target\": \"A\", \"limit\": " + limit + " }"));

        Assert.Contains("\"limit\" must be [min, max] with 0.01 <= min <= 0.05 and 2 <= max <= 20", ex.Message);
    }

    [Fact]
    public void SizeRanges_in_mod_json_moved_to_the_size_card()
    {
        const string json = """{"format":1,"id":"mod-a","sizeRanges":{"limit":[0.05,20]}}""";

        // Check and the app: an error that says where it went.
        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse(json));
        Assert.Equal("\"sizeRanges\" moved to each species' Size card (sizes/<species>.json); remove it from mod.json.", ex.Message);

        // The game: ignored, with one message; the rest of the mod loads.
        var messages = new List<string>();
        var manifest = ModManifest.Parse(json, messages);
        Assert.Equal("mod-a", manifest.Id);
        Assert.Equal("\"sizeRanges\" was ignored: it moved to each species' Size card (sizes/<species>.json); remove it from mod.json.", Assert.Single(messages));
        Assert.DoesNotContain("sizeRanges", manifest.ToJson());
    }

    [Fact]
    public void Old_limits_are_ignored_in_game()
    {
        var a = Mod("mod-a", """{"format":1,"target":"Tyrannosaurus","limit":[0.02,5]}""");
        var b = Mod("mod-b", """{"format":1,"target":"tyrannosaurus","limit":[0.05,20],"nursery":[0.5,1.5]}""");

        var table = SizeTable.Build([a, b], out _);

        Assert.Null(table.For("Tyrannosaurus")!.Limit); // the limit is automatic: derived from the sizes (SizePlan)
        Assert.Equal([0.5, 1.5], table.For("Tyrannosaurus")!.Nursery!);
        Assert.False(SizeTable.Build([a], out _).HasAny);
        Assert.False(SizeTable.Build([Mod("mod-e", null)], out _).HasAny);
    }

    [Fact]
    public void Unknown_key_is_an_error()
    {
        var ex = Assert.Throws<ManifestException>(() => SizeSettings.Parse("""{ "format": 1, "target": "A", "adlut": 1 }"""));
        Assert.Contains("adlut", ex.Message);
        Assert.Contains("nursery, breeding, limit", ex.Message);
        Assert.Throws<ManifestException>(() => SizeSettings.Parse("""{ "format": 1, "target": "A", "nurseyr": [1, 2] }"""));
    }

    [Fact]
    public void Later_mod_wins_per_key()
    {
        var a = Mod("mod-a", """{"format":1,"target":"Tyrannosaurus","adult":0.8,"baby":0.2,"nursery":[0.5,1.5],"breeding":[0.7,1.3]}""");
        var b = Mod("mod-b", """{"format":1,"target":"tyrannosaurus","adult":1.2,"breeding":[0.9,1.1]}""");

        var table = SizeTable.Build([a, b], out var messages);

        var merged = table.For("Tyrannosaurus")!;
        Assert.Equal((1.2, 0.2), (merged.Adult, merged.Baby));
        Assert.Null(merged.Male);
        Assert.Equal([0.5, 1.5], merged.Nursery!);
        Assert.Equal([0.9, 1.1], merged.Breeding!);
        Assert.Null(merged.Limit);
        Assert.Null(table.For("Allosaurus"));
        Assert.True(table.HasAny);
        Assert.Empty(messages);
    }

    [Fact]
    public void A_broken_or_duplicate_file_is_skipped_and_noted()
    {
        var dir = Dir();
        const string t = """{"format":1,"target":"A","adult":1}""";
        File.WriteAllText(Path.Combine(dir, "sizes", "a.json"), t);
        File.WriteAllText(Path.Combine(dir, "sizes", "b.json"), t);
        File.WriteAllText(Path.Combine(dir, "sizes", "c.json"), """{"format":1,"target":"C","adult":21}""");
        var problems = new List<string>();

        var read = SizeSettings.ReadFolder(dir, problems);

        Assert.Equal(["a.json"], read.Select(r => r.File));
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("b.json") && p.Contains("a.json"));
        Assert.Contains(problems, p => p.Contains("c.json") && p.Contains("\"adult\""));
        Assert.Empty(SizeSettings.ReadFolder(Dir() + "-missing", null));
    }
}
