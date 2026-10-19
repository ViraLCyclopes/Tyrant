using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class AnimalLooksTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Dictionary<string, MaterialOverride> Look(float strength) => new()
    {
        ["Body"] = new MaterialOverride { Iridescence = new IridescenceSettings { Strength = new VariantNumbers { Normal = strength } } },
    };

    [Fact]
    public void Looks_round_trip_per_park_and_prune_removed_animals()
    {
        var path = AnimalLooks.PathFor(TempDir(), "Park A")!;
        var looks = new AnimalLooks();
        looks.Set(1, Look(0.2f));
        looks.Set(2, Look(0.4f));
        looks.Write(path);

        var problems = new List<string>();
        var read = AnimalLooks.Read(path, problems);
        Assert.Empty(problems);
        Assert.Equal(0.4f, read.Get(2)!["Body"].Iridescence!.Strength!.Normal);
        Assert.Equal(1, read.Prune(new ulong[] { 2 }));
        Assert.Null(read.Get(1));
        Assert.NotNull(read.Get(2));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_missing_file_is_an_empty_store_and_a_bad_animal_is_a_problem()
    {
        var dir = TempDir();
        var problems = new List<string>();
        Assert.Equal(0, AnimalLooks.Read(Path.Combine(dir, "none.json"), problems).Count);
        Assert.Empty(problems);

        var path = Path.Combine(dir, "bad.json");
        File.WriteAllText(path, """{"format":1,"animals":{"x":{},"5":{"Body":{"iridescence":{"strength":"high"}}},"6":{"Body":{"settings":{"_Smoothness":0.5}}}}}""");
        var read = AnimalLooks.Read(path, problems);
        Assert.Equal(2, problems.Count);
        Assert.Equal(1, read.Count);
        Assert.NotNull(read.Get(6));
    }

    [Fact]
    public void Two_parks_keep_separate_files()
    {
        var dir = TempDir();
        Assert.NotEqual(AnimalLooks.PathFor(dir, "Park A"), AnimalLooks.PathFor(dir, "Park B"));
        Assert.Null(AnimalLooks.PathFor(dir, ""));
    }

    [Fact]
    public void No_park_or_no_id_means_this_animal_is_unavailable()
    {
        Assert.False(VanillaLooks.IsAvailable(LookScope.Animal, null, "Park"));
        Assert.False(VanillaLooks.IsAvailable(LookScope.Animal, 7, null));
        Assert.True(VanillaLooks.IsAvailable(LookScope.Animal, 7, "Park"));
        Assert.True(VanillaLooks.IsAvailable(LookScope.Skin, null, null));
    }
}
