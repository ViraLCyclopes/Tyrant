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
    public void Looks_round_trip_per_save_and_prune_removed_animals()
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
    public void Two_saves_keep_separate_files_and_the_save_name_is_the_last_part_of_its_path()
    {
        var dir = TempDir();
        Assert.NotEqual(AnimalLooks.PathFor(dir, "Save A"), AnimalLooks.PathFor(dir, "Save B"));
        Assert.Null(AnimalLooks.PathFor(dir, ""));
        Assert.Null(AnimalLooks.PathFor(dir, null));
        Assert.Equal(Path.Combine(dir, "Looks", "My Park.json"), AnimalLooks.PathFor(dir, "My Park"));
        Assert.Equal(Path.Combine(dir, "Looks", "My Park"), AnimalLooks.FolderFor(dir, "My Park"));
        Assert.Equal(Path.Combine(dir, "Looks", "_.pending"), AnimalLooks.FolderFor(dir, ".pending")); // never the editor's own folder

        var saves = Path.Combine(dir, "Saves");
        Assert.Equal("My Park", AnimalLooks.SaveNameOf(Path.Combine(saves, "My Park")));
        Assert.Equal("My Park", AnimalLooks.SaveNameOf(Path.Combine(saves, "My Park") + Path.DirectorySeparatorChar));
        Assert.Equal("shared.pks", AnimalLooks.SaveNameOf(Path.Combine(saves, "shared.pks")));
        Assert.Null(AnimalLooks.SaveNameOf(null));
        Assert.Null(AnimalLooks.SaveNameOf("  "));
    }

    [Fact]
    public void No_save_or_no_id_means_this_animal_is_unavailable()
    {
        Assert.False(VanillaLooks.IsAvailable(LookScope.Animal, null, "Save"));
        Assert.False(VanillaLooks.IsAvailable(LookScope.Animal, 7, null));
        Assert.True(VanillaLooks.IsAvailable(LookScope.Animal, 7, "Save"));
        Assert.True(VanillaLooks.IsAvailable(LookScope.Skin, null, null));
    }

    private static Dictionary<string, MaterialOverride> FurLook(string file) => new()
    {
        ["Body"] = new MaterialOverride { Fur = new FurMasks { Mask = new MaskSource { File = file } } },
    };

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Writing_for_another_save_copies_the_animal_folders_and_points_the_store_at_the_copies()
    {
        var dir = TempDir();
        WriteFile(Path.Combine(dir, "Looks", "Save A", "5", "fur-mask.png"), "a5");
        WriteFile(Path.Combine(dir, "Looks", "Save A", "5", "other.png"), "o5");
        WriteFile(Path.Combine(dir, "Looks", "Save B", "5", "stale.png"), "old"); // an earlier save over B: replaced
        var looks = new AnimalLooks();
        looks.Set(5, FurLook("Looks/Save A/5/fur-mask.png"));
        looks.Set(6, FurLook("Masks/shared.png")); // a shared image stays where it is

        looks.WriteFor(dir, "Save B");

        Assert.Equal("Looks/Save B/5/fur-mask.png", looks.Get(5)!["Body"].Fur!.Mask!.File);
        Assert.Equal("Masks/shared.png", looks.Get(6)!["Body"].Fur!.Mask!.File);
        Assert.Equal("a5", File.ReadAllText(Path.Combine(dir, "Looks", "Save B", "5", "fur-mask.png")));
        Assert.Equal("o5", File.ReadAllText(Path.Combine(dir, "Looks", "Save B", "5", "other.png")));
        Assert.False(File.Exists(Path.Combine(dir, "Looks", "Save B", "5", "stale.png")));
        Assert.True(File.Exists(Path.Combine(dir, "Looks", "Save A", "5", "fur-mask.png"))); // the other save keeps its own
        var problems = new List<string>();
        var read = AnimalLooks.Read(AnimalLooks.PathFor(dir, "Save B")!, problems);
        Assert.Empty(problems);
        Assert.Equal("Looks/Save B/5/fur-mask.png", read.Get(5)!["Body"].Fur!.Mask!.File);
        Assert.False(File.Exists(AnimalLooks.PathFor(dir, "Save A")!)); // never written by a save to B
    }

    [Fact]
    public void Writing_for_the_same_save_moves_nothing()
    {
        var dir = TempDir();
        WriteFile(Path.Combine(dir, "Looks", "Save A", "5", "fur-mask.png"), "a5");
        var looks = new AnimalLooks();
        looks.Set(5, FurLook("Looks/Save A/5/fur-mask.png"));

        looks.WriteFor(dir, "Save A");

        Assert.Equal("Looks/Save A/5/fur-mask.png", looks.Get(5)!["Body"].Fur!.Mask!.File);
        Assert.Equal("a5", File.ReadAllText(Path.Combine(dir, "Looks", "Save A", "5", "fur-mask.png")));
    }

    [Fact]
    public void Writing_with_the_live_animals_deletes_the_folders_of_animals_gone_and_nothing_of_other_saves()
    {
        var dir = TempDir();
        WriteFile(Path.Combine(dir, "Looks", "Save B", "5", "fur-mask.png"), "kept");
        WriteFile(Path.Combine(dir, "Looks", "Save B", "8", "fur-mask.png"), "gone"); // pruned: no look, not in the park
        WriteFile(Path.Combine(dir, "Looks", "Save B", "9", "mine.png"), "alive"); // no look yet, but the animal is there
        WriteFile(Path.Combine(dir, "Looks", "Save A", "8", "fur-mask.png"), "other save");
        WriteFile(AnimalLooks.PathFor(dir, "Save A")!, """{"format":1,"animals":{"8":{"Body":{"settings":{"_Smoothness":0.5}}}}}""");
        var looks = new AnimalLooks();
        looks.Set(5, FurLook("Looks/Save B/5/fur-mask.png"));

        looks.WriteFor(dir, "Save B", alive: new ulong[] { 5, 9 });

        Assert.True(File.Exists(Path.Combine(dir, "Looks", "Save B", "5", "fur-mask.png")));
        Assert.False(Directory.Exists(Path.Combine(dir, "Looks", "Save B", "8")));
        Assert.True(File.Exists(Path.Combine(dir, "Looks", "Save B", "9", "mine.png")));
        Assert.True(File.Exists(Path.Combine(dir, "Looks", "Save A", "8", "fur-mask.png")));
        Assert.Contains("\"8\"", File.ReadAllText(AnimalLooks.PathFor(dir, "Save A")!));
    }

    [Fact]
    public void An_empty_store_removes_the_save_s_old_file_instead_of_writing_one()
    {
        var dir = TempDir();
        var path = AnimalLooks.PathFor(dir, "Save B")!;
        WriteFile(path, """{"format":1,"animals":{"3":{"Body":{"settings":{"_Smoothness":0.5}}}}}""");

        new AnimalLooks().WriteFor(dir, "Save B");

        Assert.False(File.Exists(path)); // a park saved over this name with no looks must not get the old ones
    }

    [Fact]
    public void A_removed_save_loses_its_looks_file_and_folder_and_no_other()
    {
        var dir = TempDir();
        WriteFile(AnimalLooks.PathFor(dir, "Save A")!, "{}");
        WriteFile(Path.Combine(dir, "Looks", "Save A", "5", "fur-mask.png"), "a");
        WriteFile(AnimalLooks.PathFor(dir, "Save B")!, "{}");

        AnimalLooks.DeleteSave(dir, "Save A");

        Assert.False(File.Exists(AnimalLooks.PathFor(dir, "Save A")!));
        Assert.False(Directory.Exists(AnimalLooks.FolderFor(dir, "Save A")!));
        Assert.True(File.Exists(AnimalLooks.PathFor(dir, "Save B")!));
        AnimalLooks.DeleteSave(dir, "Never saved"); // nothing there: nothing happens
    }

    [Fact]
    public void A_look_a_read_would_refuse_is_never_written()
    {
        var path = AnimalLooks.PathFor(TempDir(), "Save A")!;
        var good = new AnimalLooks();
        good.Set(1, Look(0.2f));
        good.Write(path);
        var bad = new MaterialOverride();
        bad.Settings["_DroopDirection"] = MaterialValue.OfVector(0, 0, -21);
        var looks = new AnimalLooks();
        looks.Set(2, new Dictionary<string, MaterialOverride> { ["Body"] = bad });

        var ex = Assert.Throws<ManifestException>(() => looks.Write(path));

        Assert.Contains("_DroopDirection", ex.Message);
        Assert.Contains("\"1\"", File.ReadAllText(path)); // the file is as it was
    }
}
