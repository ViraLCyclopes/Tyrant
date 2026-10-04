using System.Text;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Sounds;
using Tyrant.Core.Workspaces;
using Tyrant.Dumper.Serialization;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModProjectSoundsTests
{
    private const string Roar = "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/_TheroLarge_Comp/Vox/TheroLarge_VoxBroadcast";
    private const string Click = "event:/User Interface/Buttons/UI_Click";

    private static (FakeGame Game, ModProject Mod, string Dir) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        var outside = Path.Combine(ws.Dir, "..", "my sounds");
        Directory.CreateDirectory(outside);
        return (game, ModProject.Create(ws, "carch-voice", null, null), outside);
    }

    private static string Wav(string dir, string name, string body = "roar")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, [.. Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt "), .. Encoding.ASCII.GetBytes(body)]);
        return path;
    }

    private static string Ogg(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("OggS-click"));
        return path;
    }

    [Fact]
    public void Replacing_copies_each_file_under_a_content_hashed_name()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;

        var entry = mod.ReplaceSound(Roar, [Wav(dir, "Big Roar!.wav"), Ogg(dir, "roar2.mp3")], "Carcharodontosaurus", null);

        Assert.Equal(2, entry.Files.Count);
        Assert.Matches(@"^sounds/big-roar-[0-9a-f]{8}\.wav$", entry.Files[0]);
        Assert.Matches(@"^sounds/roar2-[0-9a-f]{8}\.ogg$", entry.Files[1]); // the extension follows the content
        Assert.All(entry.Files, f => Assert.True(File.Exists(Path.Combine(mod.Dir, f))));
        Assert.Equal("Carcharodontosaurus", Assert.Single(ModManifest.Parse(File.ReadAllText(Path.Combine(mod.Dir, "mod.json"))).Sounds).Species);
    }

    [Fact]
    public void Files_already_in_the_mod_keep_their_names_so_files_can_be_added_and_removed()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        var first = mod.ReplaceSound(Roar, [Wav(dir, "a.wav", "one")], null, null);
        var kept = Path.Combine(mod.Dir, first.Files[0].Replace('/', Path.DirectorySeparatorChar));

        var added = mod.ReplaceSound(Roar, [kept, Wav(dir, "b.wav", "two")], null, null);
        var removed = mod.ReplaceSound(Roar, [Path.Combine(mod.Dir, added.Files[1])], null, null);

        Assert.Equal(first.Files[0], added.Files[0]);
        Assert.Matches(@"^sounds/b-[0-9a-f]{8}\.wav$", added.Files[1]);
        Assert.Equal([added.Files[1]], removed.Files);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(mod.Dir, "sounds")).Length);
    }

    [Fact]
    public void A_file_that_is_not_audio_is_refused()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        var text = Path.Combine(dir, "notes.wav");
        File.WriteAllText(text, "hello");

        var ex = Assert.Throws<TyrantException>(() => mod.ReplaceSound(Roar, [text], null, null));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains("is not an audio file (WAV, OGG, MP3 or FLAC)", ex.Message);
        Assert.Empty(mod.Manifest.Sounds);
    }

    [Fact]
    public void Replacing_the_same_event_and_scope_again_keeps_one_entry_and_its_settings()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound(Roar, [Wav(dir, "a.wav", "one")], "Carcharodontosaurus", null);
        mod.SetSound(Roar, "Carcharodontosaurus", null, volume: 0.5, agePitch: null, newSpecies: null, newSkin: null, forEveryone: null);

        var again = mod.ReplaceSound(Roar.ToLowerInvariant(), [Wav(dir, "b.wav", "two")], "Carcharodontosaurus", null);

        Assert.Same(again, Assert.Single(mod.Manifest.Sounds));
        Assert.Matches(@"^sounds/b-", Assert.Single(again.Files));
        Assert.Equal(0.5, again.Volume);
    }

    [Fact]
    public void A_unique_and_a_for_everyone_replacement_of_one_event_live_side_by_side()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;

        mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], null, null);
        mod.ReplaceSound(Roar, [Wav(dir, "b.wav", "b")], "Carcharodontosaurus", null);
        mod.ReplaceSound(Roar, [Wav(dir, "c.wav", "c")], null, "carch-voice/scarred");

        Assert.Equal(3, mod.Manifest.Sounds.Count);
        Assert.Single(mod.Manifest.Sounds, s => !s.IsUnique);
    }

    [Fact]
    public void Species_and_skin_together_are_refused()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;

        var ex = Assert.Throws<TyrantException>(() => mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], "Carcharodontosaurus", "carch-voice/scarred"));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
    }

    [Fact]
    public void Removing_leaves_the_files_and_an_absent_one_is_not_found()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        var entry = mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], "Carcharodontosaurus", null);

        mod.RemoveSound(Roar, "Carcharodontosaurus", null);

        Assert.Empty(mod.Manifest.Sounds);
        Assert.True(File.Exists(Path.Combine(mod.Dir, entry.Files[0])));
        Assert.Equal(TyrantErrorCode.TargetNotFound, Assert.Throws<TyrantException>(() => mod.RemoveSound(Roar, null, null)).Code);
    }

    [Fact]
    public void Setting_changes_volume_age_pitch_and_moves_the_scope()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], "Carcharodontosaurus", null);

        mod.SetSound(Roar, "Carcharodontosaurus", null, 1.5, 0.25, null, null, forEveryone: true);
        var moved = Assert.Single(mod.Manifest.Sounds);
        Assert.False(moved.IsUnique);
        Assert.Equal((1.5, 0.25), (moved.Volume, moved.AgePitch));

        mod.SetSound(Roar, null, null, null, null, null, newSkin: "carch-voice/scarred", forEveryone: null);
        Assert.Equal("carch-voice/scarred", Assert.Single(mod.Manifest.Sounds).Skin);
        Assert.Null(mod.Manifest.Sounds[0].Species);
    }

    [Fact]
    public void Setting_refuses_values_out_of_range_and_a_scope_already_taken()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], null, null);
        mod.ReplaceSound(Roar, [Wav(dir, "b.wav", "b")], "Carcharodontosaurus", null);

        Assert.Throws<TyrantException>(() => mod.SetSound(Roar, null, null, 2.5, null, null, null, null));
        Assert.Throws<TyrantException>(() => mod.SetSound(Roar, null, null, null, -0.1, null, null, null));
        var taken = Assert.Throws<TyrantException>(() => mod.SetSound(Roar, null, null, null, null, "Carcharodontosaurus", null, null));
        Assert.Equal(TyrantErrorCode.ModInvalid, taken.Code);
        Assert.Equal(2, mod.Manifest.Sounds.Count);
    }

    // Check

    private static ModCheckResult Check(ModProject mod, SoundCatalog? sounds = null, IReadOnlyList<SpeciesSkins>? species = null) =>
        new ModChecker(_ => null).Check(mod, null, species, sounds);

    private static SoundCatalog Catalog(bool withEventList)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SkinDumps.Write(dir);
        if (withEventList) AudioEventList.Write(dir, [new AudioEvent(Roar, "{1}", 1000, true, "bank:/A"), new AudioEvent(Click, "{2}", 50, true, "bank:/UI")]);
        return SoundCatalog.Read(dir);
    }

    private static IReadOnlyList<SpeciesSkins> Species()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SkinDumps.Write(dir);
        return SpeciesSkinsReader.Read(Tyrant.Core.Data.DataStore.OpenDirectory(dir));
    }

    [Fact]
    public void A_mod_with_only_sounds_does_something()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound(Click, [Ogg(dir, "c.ogg")], null, null);

        var result = Check(mod, Catalog(true));

        Assert.True(result.Ok);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Missing_outside_and_non_audio_files_are_errors()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        File.WriteAllText(Path.Combine(mod.Dir, "fake.wav"), "text");
        var sound = new SoundReplacement { Event = Click };
        sound.Files.AddRange(["sounds/gone.wav", "../outside.wav", "fake.wav"]);
        mod.Manifest.Sounds.Add(sound);
        mod.Manifest.Sounds.Add(new SoundReplacement { Event = Roar });

        var result = Check(mod);

        Assert.Contains(result.Errors, e => e.Contains("gone.wav") && e.Contains("missing"));
        Assert.Contains(result.Errors, e => e.Contains("outside.wav") && e.Contains("outside the mod"));
        Assert.Contains(result.Errors, e => e.Contains("fake.wav") && e.Contains("not an audio file"));
        Assert.Contains(result.Errors, e => e.Contains("TheroLarge_VoxBroadcast") && e.Contains("no files"));
    }

    [Fact]
    public void Species_and_skin_on_one_entry_is_an_error()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        var entry = mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], "Carcharodontosaurus", null);
        entry.Skin = "carch-voice/scarred";

        Assert.Contains(Check(mod).Errors, e => e.Contains("both a species and a skin"));
    }

    [Fact]
    public void An_event_the_game_does_not_have_is_a_warning()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound("event:/Nope/Not_Real", [Wav(dir, "a.wav")], null, null);

        Assert.Contains(Check(mod, Catalog(true)).Warnings, w => w.Contains("event:/Nope/Not_Real") && w.Contains("not one of the game's sounds"));
        Assert.Contains(Check(mod, Catalog(false)).Warnings, w => w.Contains("event:/Nope/Not_Real") && w.Contains("Run data dump"));
        Assert.DoesNotContain(Check(mod).Warnings, w => w.Contains("event:/Nope/Not_Real")); // no dump: not checked
    }

    [Fact]
    public void Unknown_or_miscapitalised_species_and_unknown_skins_are_warnings()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], "Unicorn", null);
        mod.ReplaceSound(Click, [Ogg(dir, "c.ogg")], "carcharodontosaurus", null);
        mod.ReplaceSound(Roar, [Wav(dir, "b.wav", "b")], null, "Carcharodontosaurus/Pink");
        mod.ReplaceSound(Click, [Ogg(dir, "d.ogg")], null, "carch-voice/missing-skin");
        mod.ReplaceSound(Roar, [Wav(dir, "e.wav", "e")], null, "Carcharodontosaurus/Alt 1");
        mod.ReplaceSound(Click, [Ogg(dir, "f.ogg")], null, "someone-else/their-skin");

        var warnings = Check(mod, Catalog(true), Species()).Warnings;

        Assert.Contains(warnings, w => w.Contains("\"Unicorn\"") && w.Contains("not in the game data"));
        Assert.Contains(warnings, w => w.Contains("must be written \"Carcharodontosaurus\""));
        Assert.Contains(warnings, w => w.Contains("Carcharodontosaurus/Pink"));
        Assert.Contains(warnings, w => w.Contains("carch-voice/missing-skin"));
        Assert.DoesNotContain(warnings, w => w.Contains("for skin Carcharodontosaurus/Alt 1:"));
        Assert.DoesNotContain(warnings, w => w.Contains("someone-else"));
    }

    [Fact]
    public void A_file_over_20_mb_is_a_warning()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        var big = Path.Combine(dir, "long.wav");
        using (var stream = File.Create(big))
        {
            stream.Write(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVE"));
            stream.SetLength(21L * 1024 * 1024);
        }
        mod.ReplaceSound(Roar, [big], null, null);

        Assert.Contains(Check(mod).Warnings, w => w.Contains("long-") && w.Contains("MB"));
    }

    [Fact]
    public void Sound_files_are_shared_on_export()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        var entry = mod.ReplaceSound(Roar, [Wav(dir, "a.wav"), Ogg(dir, "b.ogg")], null, null);
        File.WriteAllText(Path.Combine(mod.Dir, "sounds", "unused.wav"), "x");

        var shared = ModSharing.SharedFiles(mod);

        Assert.Equal(["mod.json", .. entry.Files.Order(StringComparer.Ordinal)], shared);
    }
}
