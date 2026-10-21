using System.Text;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Sounds;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SoundDumps;

namespace Tyrant.Core.Tests;

/// <summary>Sound replacements for a mod's own (custom) species and other mods' ones: a custom species plays its template's sounds.</summary>
public class CustomSpeciesSoundsTests
{
    private const string Anax = "tyrant.carch-voice.anax";

    private static (FakeGame Game, ModProject Mod, string Dir) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        var outside = Path.Combine(ws.Dir, "..", "my sounds");
        Directory.CreateDirectory(outside);
        var mod = ModProject.Create(ws, "carch-voice", null, null);
        mod.Manifest.Species.Add(new SpeciesEntry { Id = "anax", Base = "Carcharodontosaurus", Name = "Anax", Genus = "A", Epithet = "anax" });
        return (game, mod, outside);
    }

    private static string Wav(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, [.. Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt "), .. Encoding.ASCII.GetBytes(name)]);
        return path;
    }

    private static SoundCatalog Catalog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SoundDumps.Write(dir);
        return SoundCatalog.Read(dir);
    }

    private static IReadOnlyList<SpeciesSkins> Species()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SkinDumps.Write(dir);
        return SpeciesSkinsReader.Read(Tyrant.Core.Data.DataStore.OpenDirectory(dir));
    }

    private static OtherMods Others(params (string ModId, string Id, string Base)[] species) => new(
        species.Select(s => new OtherSpecies($"tyrant.{s.ModId}.{s.Id}", s.ModId, s.ModId, new SpeciesEntry { Id = s.Id, Base = s.Base, Name = s.Id })).ToList(),
        species.Select(s => s.ModId).Distinct().ToDictionary(m => m, m => new OtherModState(m, true, false, false)));

    private static ModCheckResult Check(ModProject mod, SoundCatalog? sounds, IReadOnlyList<SpeciesSkins>? species, OtherMods? others = null) =>
        new ModChecker(_ => null).Check(mod, null, species, sounds, others: others);

    [Fact]
    public void A_custom_species_plays_its_templates_sounds()
    {
        var catalog = Catalog();
        string? TemplateOf(string id) => id == Anax ? "Carcharodontosaurus" : null;

        var anax = catalog.SoundsOf(Anax, TemplateOf);
        var carch = catalog.SoundsOf("carcharodontosaurus", TemplateOf);

        Assert.NotNull(anax);
        Assert.Equal((Anax, "Carcharodontosaurus"), (anax.SpeciesId, anax.Template));
        Assert.Equal(catalog.ForSpecies("Carcharodontosaurus").Select(s => s.Event), anax.Sounds.Select(s => s.Event));
        Assert.Equal(("Carcharodontosaurus", (string?)null), (carch!.SpeciesId, carch.Template));
        Assert.Null(catalog.SoundsOf("tyrant.carch-voice.nobody", TemplateOf));
        Assert.Null(catalog.SoundsOf("Unicorn", TemplateOf));
    }

    [Fact]
    public void The_template_comes_from_this_mod_or_the_others()
    {
        var manifest = new ModManifest { Id = "carch-voice", Name = "C" };
        manifest.Species.Add(new SpeciesEntry { Id = "anax", Base = "Carcharodontosaurus" });
        var others = Others(("mod-a", "rex", "Acrocanthosaurus")).Species;

        Assert.Equal("Carcharodontosaurus", CustomSpeciesTemplate.Of(Anax, manifest, others));
        Assert.Equal("Acrocanthosaurus", CustomSpeciesTemplate.Of("tyrant.mod-a.rex", manifest, others));
        Assert.Null(CustomSpeciesTemplate.Of("tyrant.mod-a.nope", manifest, others));
        Assert.Null(CustomSpeciesTemplate.Of("Carcharodontosaurus", manifest, others));
    }

    [Fact]
    public void A_sound_for_this_mods_own_species_is_valid()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], Anax, null);

        var result = Check(mod, Catalog(), Species());

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("Sound ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_sound_for_a_species_this_mod_does_not_have_is_an_error_even_without_a_dump()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], "tyrant.carch-voice.nobody", null);

        Assert.Contains(Check(mod, Catalog(), Species()).Errors, e => e.Contains("\"tyrant.carch-voice.nobody\"") && e.Contains("not one of this mod's species"));
        Assert.Contains(Check(mod, null, null).Errors, e => e.Contains("\"tyrant.carch-voice.nobody\""));
    }

    [Fact]
    public void Another_mods_species_is_valid_when_that_mod_has_it()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound(Roar, [Wav(dir, "a.wav")], "tyrant.mod-a.rex", null);
        mod.ReplaceSound(Growl, [Wav(dir, "b.wav")], "tyrant.mod-a.nope", null);
        mod.ReplaceSound(Step, [Wav(dir, "c.wav")], "tyrant.mod-z.ghost", null);

        var result = Check(mod, Catalog(), Species(), Others(("mod-a", "rex", "Acrocanthosaurus")));

        Assert.DoesNotContain(result.Errors.Concat(result.Warnings), p => p.Contains("tyrant.mod-a.rex"));
        Assert.Contains(result.Errors, e => e.Contains("\"tyrant.mod-a.nope\"") && e.Contains("not in mod 'mod-a'"));
        Assert.Contains(result.Warnings, w => w.Contains("tyrant.mod-z.ghost") && w.Contains("mod 'mod-z'") && w.Contains("installed"));
    }

    [Fact]
    public void A_sound_the_species_never_plays_is_a_warning_for_game_and_custom_species()
    {
        var (game, mod, dir) = Setup();
        using var _ = game;
        mod.ReplaceSound(AcroCall, [Wav(dir, "a.wav")], Anax, null);
        mod.ReplaceSound(AcroCall, [Wav(dir, "b.wav")], "Carcharodontosaurus", null);
        mod.ReplaceSound(Roar, [Wav(dir, "c.wav")], Anax, null);
        mod.ReplaceSound(AcroCall, [Wav(dir, "d.wav")], "Acrocanthosaurus", null);

        var warnings = Check(mod, Catalog(), Species()).Warnings;

        Assert.Contains(warnings, w => w.Contains($"for {Anax}:") && w.Contains("Carcharodontosaurus's sounds") && w.Contains("never heard"));
        Assert.Contains(warnings, w => w.Contains("for Carcharodontosaurus:") && w.Contains("does not play broadcast") && w.Contains("never heard"));
        Assert.DoesNotContain(warnings, w => w.Contains("TheroLarge_VoxSocialCall"));
        Assert.DoesNotContain(warnings, w => w.Contains("for Acrocanthosaurus:") && w.Contains("never heard"));
    }

    [Fact]
    public void Sounds_for_other_mods_species_make_check_read_the_other_mods()
    {
        var manifest = new ModManifest { Id = "carch-voice", Name = "C" };
        manifest.Sounds.Add(new SoundReplacement { Event = Roar, Species = Anax });
        Assert.False(SpeciesUse.SoundsUseOtherMods(manifest));

        manifest.Sounds.Add(new SoundReplacement { Event = Roar, Species = "tyrant.mod-a.rex" });
        Assert.True(SpeciesUse.SoundsUseOtherMods(manifest));
    }
}
