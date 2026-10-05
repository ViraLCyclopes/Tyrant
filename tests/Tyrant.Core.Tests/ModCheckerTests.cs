using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;

namespace Tyrant.Core.Tests;

public class ModCheckerTests
{
    private static readonly AssetRecord Diffuse = new("b.bundle", 1, "Texture2D", "T_Carch_D", "Assets/T_Carch_D.png", "1111", null);
    private static readonly AssetRecord Normal = new("b.bundle", 2, "Texture2D", "T_Carch_N", "Assets/T_Carch_N.png", "2222", null);
    private static readonly AssetIndex Index = new() { Assets = [Diffuse, Normal] };

    private static readonly ModChecker Checker = new(_ => (4, 4));

    private static (FakeGame Game, Workspace Ws, ModProject Mod) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        return (game, ws, ModProject.Create(ws, "red-spot", null, null));
    }

    private static void Png(ModProject mod, string name, int size, Func<int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var data = Enumerable.Range(0, size * size).SelectMany(i => { var p = pixel(i); return new[] { p.R, p.G, p.B, p.A }; }).ToArray();
        Directory.CreateDirectory(Path.Combine(mod.Dir, "textures"));
        using var stream = File.Create(Path.Combine(mod.Dir, "textures", name));
        new StbImageWriteSharp.ImageWriter().WritePng(data, size, size, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }

    private static void Entry(ModProject mod, string texture, string file)
    {
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = texture, File = file });
        mod.Save();
    }

    [Fact]
    public void A_good_mod_has_no_errors_or_warnings()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Png(mod, "d.png", 4, _ => (200, 40, 30, 255));
        Entry(mod, "T_Carch_D", "textures/d.png");

        var result = Checker.Check(mod, Index);

        Assert.True(result.Ok);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Missing_or_unreadable_files_are_errors()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Directory.CreateDirectory(Path.Combine(mod.Dir, "textures"));
        File.WriteAllBytes(Path.Combine(mod.Dir, "textures", "bad.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]); // a PNG signature, then garbage
        Entry(mod, "T_Carch_D", "textures/missing.png");
        Entry(mod, "T_Carch_N", "textures/bad.png");

        var result = Checker.Check(mod, Index);

        Assert.Contains(result.Errors, e => e.Contains("missing.png") && e.Contains("missing"));
        Assert.Contains(result.Errors, e => e.Contains("bad.png") && e.Contains("not a readable PNG"));
    }

    [Fact]
    public void Unknown_targets_and_files_outside_the_mod_are_errors()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Png(mod, "d.png", 4, _ => (1, 2, 3, 255));
        Entry(mod, "T_Nope_D", "textures/d.png");
        Entry(mod, "T_Carch_D", "../../escape.png");

        var result = Checker.Check(mod, Index);

        Assert.Contains(result.Errors, e => e.Contains("T_Nope_D") && e.Contains("not in the asset index"));
        Assert.Contains(result.Errors, e => e.Contains("outside the mod folder"));
    }

    [Fact]
    public void A_size_different_from_the_original_is_a_warning()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Png(mod, "d.png", 8, _ => (200, 40, 30, 255));
        Entry(mod, "T_Carch_D", "textures/d.png");

        var result = Checker.Check(mod, Index);

        Assert.True(result.Ok);
        Assert.Contains("8x8", Assert.Single(result.Warnings));
        Assert.Contains("4x4", result.Warnings[0]);
    }

    [Fact]
    public void A_normal_slot_png_that_is_not_a_normal_map_is_a_warning()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Png(mod, "n.png", 4, _ => (200, 40, 30, 255));          // a diffuse put in a normal slot
        Png(mod, "n2.png", 4, i => (128, 128, 255, 255));        // a real (standard) normal map
        Entry(mod, "T_Carch_N", "textures/n.png");

        var bad = Checker.Check(mod, Index);
        mod.Manifest.Replace.Clear();
        Entry(mod, "T_Carch_N", "textures/n2.png");
        var good = Checker.Check(mod, Index);

        Assert.Contains("normal map", Assert.Single(bad.Warnings));
        Assert.Empty(good.Warnings);
    }

    [Fact]
    public void A_mod_that_does_nothing_and_a_missing_index_are_warnings()
    {
        var (game, _, mod) = Setup();
        using var _ = game;

        var empty = Checker.Check(mod, Index);
        Png(mod, "d.png", 4, _ => (1, 2, 3, 255));
        Entry(mod, "T_Carch_D", "textures/d.png");
        var noIndex = Checker.Check(mod, null);

        Assert.Contains("does nothing", Assert.Single(empty.Warnings));
        Assert.Contains(noIndex.Warnings, w => w.Contains("asset index"));
        Assert.True(noIndex.Ok);
    }

    [Fact]
    public void An_image_that_is_not_a_png_is_an_error()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Directory.CreateDirectory(Path.Combine(mod.Dir, "textures"));
        var bmp = new byte[4 * 4 * 3];
        using (var stream = File.Create(Path.Combine(mod.Dir, "textures", "d.png")))
            new StbImageWriteSharp.ImageWriter().WriteBmp(bmp, 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlue, stream); // a BMP named .png
        Entry(mod, "T_Carch_D", "textures/d.png");

        var result = Checker.Check(mod, Index);

        Assert.Contains(result.Errors, e => e.Contains("not a PNG"));
    }

    private static IReadOnlyList<SpeciesSkins> Species()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        Write(dir);
        return SpeciesSkinsReader.Read(Tyrant.Core.Data.DataStore.OpenDirectory(dir));
    }

    private static void SkinPng(ModProject mod, string relative)
    {
        var path = Path.Combine(mod.Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(Enumerable.Repeat((byte)200, 4 * 4 * 4).ToArray(), 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }

    private static void Skin(ModProject mod, string species, string baseSkin, string? maleDiffuse)
    {
        mod.Manifest.Skins.Add(new SkinEntry
        {
            Id = "red-spot", Species = species, Name = "Red spot", Base = baseSkin,
            Male = maleDiffuse is null ? null : new Dictionary<string, string> { ["diffuse"] = maleDiffuse },
            Female = maleDiffuse is null ? new Dictionary<string, string> { ["diffuse"] = "skins/red-spot/f.png" } : null,
        });
        mod.Save();
    }

    [Fact]
    public void A_good_skin_has_no_errors()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        SkinPng(mod, "skins/red-spot/m.png");
        Skin(mod, "Carcharodontosaurus", "Alt 1", "skins/red-spot/m.png");

        var result = new ModChecker(_ => (4, 4)).Check(mod, SkinDumps.Index(), Species());

        Assert.True(result.Ok, string.Join(" ", result.Errors));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Skin_files_missing_or_unknown_species_are_errors()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Skin(mod, "Nessie", "Alt 1", "skins/red-spot/missing.png");

        var result = Checker.Check(mod, SkinDumps.Index(), Species());

        Assert.Contains(result.Errors, e => e.Contains("Nessie"));
        Assert.Contains(result.Errors, e => e.Contains("missing.png") && e.Contains("missing"));
    }

    [Fact]
    public void An_unknown_base_is_an_error_and_no_dump_is_a_warning()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        SkinPng(mod, "skins/red-spot/m.png");
        Skin(mod, "Carcharodontosaurus", "Alt 9", "skins/red-spot/m.png");

        var withDump = Checker.Check(mod, SkinDumps.Index(), Species());
        var noDump = Checker.Check(mod, SkinDumps.Index(), null);

        Assert.Contains(withDump.Errors, e => e.Contains("Alt 9"));
        Assert.True(noDump.Ok);
        Assert.Contains(noDump.Warnings, w => w.Contains("Run data dump"));
    }

    [Fact]
    public void A_png_locked_by_another_program_is_an_error_not_a_crash()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Png(mod, "d.png", 4, _ => (1, 2, 3, 255));
        Entry(mod, "T_Carch_D", "textures/d.png");

        using (new FileStream(Path.Combine(mod.Dir, "textures", "d.png"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = Checker.Check(mod, Index);

            Assert.Contains(result.Errors, e => e.Contains("d.png") && e.Contains("could not be read"));
        }
    }

    [Fact]
    public void A_species_written_in_the_wrong_case_is_an_error_that_names_the_right_spelling()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        SkinPng(mod, "skins/red-spot/m.png");
        Skin(mod, "carcharodontosaurus", "Alt 1", "skins/red-spot/m.png");

        var result = Checker.Check(mod, SkinDumps.Index(), Species());

        Assert.Contains(result.Errors, e => e.Contains("\"Carcharodontosaurus\""));
    }

    private static void RigOnly(ModProject mod, string bone)
    {
        mod.Manifest.Models.Add(new ModelReplacement
        {
            Target = "Carcharodontosaurus", File = "",
            Rig = new Dictionary<string, RigOffset> { [bone] = RigOffset.Identity },
        });
        mod.Save();
    }

    [Fact]
    public void A_rig_edit_is_checked_against_the_species_skeleton()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        RigOnly(mod, "Tail.099");
        var info = new Tyrant.Core.Rigging.RigInfo(["Jaw", "Pelvis"], ["Pelvis"], [], [], []);

        var result = Checker.Check(mod, Index, rigInfo: species => species == "Carcharodontosaurus" ? info : null);

        Assert.Contains(result.Errors, e => e.Contains("Tail.099"));
        Assert.Contains(result.Warnings, w => w.Contains("no model of its own"));
    }

    [Fact]
    public void Without_the_games_skeleton_a_rig_edit_is_noted_as_unchecked()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        RigOnly(mod, "Jaw");

        var result = Checker.Check(mod, Index);

        Assert.True(result.Ok);
        Assert.Contains(result.Warnings, w => w.Contains("not checked") && w.Contains("Run data dump"));
    }

    [Fact]
    public void A_mod_with_only_a_rig_edit_is_not_empty()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        RigOnly(mod, "Jaw");

        Assert.DoesNotContain(Checker.Check(mod, Index).Warnings, w => w.Contains("does nothing"));
    }
}
