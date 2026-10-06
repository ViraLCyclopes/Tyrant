using Tyrant.Core.Sounds;

namespace Tyrant.Core.Tests;

public class SoundFolderMatcherTests
{
    private const string Med = "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodMedium/_TheroMed_Comp";
    private const string Large = "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/_TheroLarge_Comp";

    private static SoundInfo Sound(string path) => new(path, SoundCatalog.NameOf(path), "Calls", ["Allosaurus Anax"], null, true, true);

    private static readonly IReadOnlyList<SoundInfo> Anax =
    [
        Sound($"{Med}/Growls/TheroMed_Growl_1sec"),
        Sound($"{Med}/Vox/TheroMed_VoxAngry"),
        Sound($"{Med}/Vox/TheroMed_VoxBroadcastA"),
        Sound($"{Med}/Vox/TheroMed_VoxBroadcastB"),
        Sound($"{Med}/Vox/TheroMed_VoxYawn"),
        Sound($"{Large}/Vox/TheroLarge_VoxYawn"),
    ];

    private static string F(string name) => Path.Combine(@"C:\pack", name);

    [Fact]
    public void A_packs_takes_become_one_sound_each_matched_without_the_species_prefix()
    {
        // As the Ultimasaurus pack names them: the pack's own prefix, the game's sound name, a take number.
        var files = new[] { "AlloAnax_Growl1sec_02.wav", "AlloAnax_Growl1sec_01.wav", "AlloAnax_VoxAngry_01.wav", "AlloAnax_VoxBroadcastA_01.wav" }
            .Select(F).ToList();

        var match = SoundFolderMatcher.Match(files, Anax);

        Assert.Equal(["AlloAnax_Growl1sec", "AlloAnax_VoxAngry", "AlloAnax_VoxBroadcastA"], match.Groups.Select(g => g.Name));
        Assert.Equal([F("AlloAnax_Growl1sec_01.wav"), F("AlloAnax_Growl1sec_02.wav")], match.Groups[0].Files);
        Assert.Equal([$"{Med}/Growls/TheroMed_Growl_1sec"], match.Groups[0].Events);
        Assert.Equal([$"{Med}/Vox/TheroMed_VoxAngry"], match.Groups[1].Events);
        Assert.Empty(match.Unmatched);
    }

    [Fact]
    public void Files_named_like_the_game_with_or_without_a_take_number_match_too()
    {
        var files = new[] { "TheroMed_VoxAngry.ogg", "VoxBroadcastB.mp3", "Growl_1sec_3.flac" }.Select(F).ToList();

        var match = SoundFolderMatcher.Match(files, Anax);

        Assert.Equal([$"{Med}/Vox/TheroMed_VoxAngry"], match.Groups.Single(g => g.Name == "TheroMed_VoxAngry").Events);
        Assert.Equal([$"{Med}/Vox/TheroMed_VoxBroadcastB"], match.Groups.Single(g => g.Name == "VoxBroadcastB").Events);
        Assert.Equal([$"{Med}/Growls/TheroMed_Growl_1sec"], match.Groups.Single(g => g.Name == "Growl_1sec").Events);
    }

    [Fact]
    public void A_name_two_game_sounds_share_offers_both()
    {
        var match = SoundFolderMatcher.Match([F("AlloAnax_VoxYawn_01.wav")], Anax);

        Assert.Equal([$"{Med}/Vox/TheroMed_VoxYawn", $"{Large}/Vox/TheroLarge_VoxYawn"], Assert.Single(match.Groups).Events);
    }

    [Fact]
    public void Files_that_match_nothing_or_are_not_audio_are_named()
    {
        var match = SoundFolderMatcher.Match([F("AlloAnax_Sneeze_01.wav"), F("readme.txt"), F("AlloAnax_VoxAngry_01.wav")], Anax);

        Assert.Equal([F("AlloAnax_Sneeze_01.wav"), F("readme.txt")], match.Unmatched);
        Assert.Single(match.Groups);
    }
}
