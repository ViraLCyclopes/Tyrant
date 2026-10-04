using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SoundTableTests
{
    private const string Call = "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/_TheroLarge_Comp/Vox/TheroLarge_VoxSocialCall";

    /// <summary>A loaded mod in a temp folder with every sound file it names (inside the folder) written.</summary>
    private static LoadedMod Mod(string id, string sounds, params string[] skipFiles)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), id);
        Directory.CreateDirectory(dir);
        var manifest = ModManifest.Parse("{\"format\":1,\"id\":\"" + id + "\",\"sounds\":" + sounds + "}");
        foreach (var file in manifest.Sounds.SelectMany(s => s.Files).Where(f => !skipFiles.Contains(f)))
        {
            var path = Path.GetFullPath(Path.Combine(dir, file));
            if (!ModPaths.IsInside(path, dir)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "audio");
        }
        return new LoadedMod(manifest, dir);
    }

    private static string Entry(string? species, string? skin, params string[] files) =>
        "{\"event\":\"" + Call + "\"" + (species is null ? "" : ",\"species\":\"" + species + "\"") + (skin is null ? "" : ",\"skin\":\"" + skin + "\"")
        + ",\"files\":[" + string.Join(",", files.Select(f => "\"" + f + "\"")) + "]}";

    [Fact]
    public void A_skin_beats_its_species_which_beats_everyone()
    {
        var everyone = Mod("all-theropods", "[" + Entry(null, null, "sounds/all.ogg") + "]");
        var species = Mod("carch-voice", "[" + Entry("Carcharodontosaurus", null, "sounds/carch.ogg") + "]");
        var skin = Mod("zombie-carch", "[" + Entry(null, "zombie-carch/zombie", "sounds/zombie.ogg") + "]");
        var table = SoundTable.Build([everyone, species, skin], []);

        Assert.EndsWith("zombie.ogg", table.Choose(Call, "Carcharodontosaurus", "zombie-carch/zombie")!.FilePaths[0]);
        Assert.EndsWith("carch.ogg", table.Choose(Call, "Carcharodontosaurus", "zombie-carch/other")!.FilePaths[0]);
        Assert.EndsWith("all.ogg", table.Choose(Call, "Acrocanthosaurus", null)!.FilePaths[0]);
        Assert.True(table.Choose(Call, "Carcharodontosaurus", null)!.Unique);
        Assert.False(table.Choose(Call, "Acrocanthosaurus", null)!.Unique);
    }

    [Fact]
    public void The_animal_matters_for_unique_sounds_and_for_baby_pitch()
    {
        var unique = SoundTable.Build([Mod("carch-voice", "[" + Entry("Carcharodontosaurus", null, "sounds/carch.ogg") + "]")], []);
        var everyone = SoundTable.Build([Mod("all", "[" + Entry(null, null, "sounds/all.ogg") + "]")], []);
        var flat = SoundTable.Build([Mod("flat", "[" + Entry(null, null, "sounds/all.ogg").TrimEnd('}') + ",\"agePitch\":0}]")], []);

        Assert.True(unique.NeedsAnimal(Call));
        Assert.True(everyone.NeedsAnimal(Call)); // babies play a for-everyone replacement higher too
        Assert.False(flat.NeedsAnimal(Call));
        Assert.False(everyone.NeedsAnimal("event:/Other"));
    }

    [Fact]
    public void A_unique_sound_alone_leaves_other_species_with_the_original()
    {
        var table = SoundTable.Build([Mod("carch-voice", "[" + Entry("Carcharodontosaurus", null, "sounds/carch.ogg") + "]")], []);

        Assert.Null(table.Choose(Call, "Acrocanthosaurus", null));
        Assert.Null(table.Choose(Call, null, null));
        Assert.True(table.Mentions(Call.ToUpperInvariant()));
        Assert.True(table.HasUnique(Call));
        Assert.Equal((1, 1), (table.Count, table.UniqueCount));
    }

    [Fact]
    public void A_later_mod_wins_within_a_level()
    {
        var first = Mod("first", "[" + Entry("Carcharodontosaurus", null, "sounds/first.ogg") + "]");
        var second = Mod("second", "[" + Entry("Carcharodontosaurus", null, "sounds/second.ogg") + "]");

        var choice = SoundTable.Build([first, second], []).Choose(Call, "Carcharodontosaurus", null)!;

        Assert.Equal("second", choice.ModId);
    }

    [Fact]
    public void Files_outside_the_mod_or_missing_are_dropped_with_a_warning()
    {
        var warnings = new List<string>();
        var mod = Mod("carch-voice", "[" + Entry(null, null, "sounds/ok.ogg", "../../evil.ogg", "sounds/gone.ogg") + "," +
            Entry("Carcharodontosaurus", null, "sounds/gone.ogg") + "]", "sounds/gone.ogg");

        var table = SoundTable.Build([mod], warnings);

        Assert.Single(table.Choose(Call, "Acrocanthosaurus", null)!.FilePaths);
        Assert.False(table.Choose(Call, "Carcharodontosaurus", null)!.Unique); // its only file was missing: the entry is dropped
        Assert.Contains(warnings, w => w.Contains("evil.ogg"));
        Assert.Contains(warnings, w => w.Contains("gone.ogg"));
    }

    [Fact]
    public void Several_files_never_play_the_same_one_twice_in_a_row()
    {
        var choice = SoundTable.Build([Mod("carch-voice", "[" + Entry(null, null, "a.ogg", "b.ogg", "c.ogg") + "]")], []).Choose(Call, null, null)!;
        var random = new Random(7);

        var picks = Enumerable.Range(0, 200).Select(_ => choice.NextFile(random)).ToList();

        Assert.All(picks.Zip(picks.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));
        Assert.Equal(3, picks.Distinct().Count());
    }

    [Fact]
    public void A_single_file_is_always_the_one()
    {
        var choice = SoundTable.Build([Mod("carch-voice", "[" + Entry(null, null, "a.ogg") + "]")], []).Choose(Call, null, null)!;
        Assert.All(Enumerable.Range(0, 5), _ => Assert.EndsWith("a.ogg", choice.NextFile(new Random())));
    }

    [Theory]
    [InlineData(1.0, 0f, 1.5f)]
    [InlineData(1.0, 1f, 1f)]
    [InlineData(0.0, 0f, 1f)]
    [InlineData(0.5, 0f, 1.25f)]
    [InlineData(1.0, -3f, 1.5f)]
    [InlineData(1.0, 4f, 1f)]
    public void Babies_play_higher_by_the_age_pitch(double agePitch, float maturity, float pitch) =>
        Assert.Equal(pitch, AgePitch.For(agePitch, maturity), 3);

    [Fact]
    public void Volume_and_age_pitch_come_with_the_choice()
    {
        var mod = Mod("carch-voice", "[{\"event\":\"" + Call + "\",\"files\":[\"a.ogg\"],\"volume\":0.5,\"agePitch\":0.25}]");
        var choice = SoundTable.Build([mod], []).Choose(Call, null, null)!;
        Assert.Equal((0.5, 0.25, Call), (choice.Volume, choice.AgePitch, choice.Event));
    }
}
