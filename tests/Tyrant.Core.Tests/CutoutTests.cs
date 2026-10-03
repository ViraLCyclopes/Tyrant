using StbImageSharp;
using StbImageWriteSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Core.Tests.SkinDumps;
using ColorComponents = StbImageWriteSharp.ColorComponents;

namespace Tyrant.Core.Tests;

/// <summary>Feathers and hair cut out by a diffuse's transparency (e.g. Velociraptor): a mod PNG saved without it loses them.</summary>
public class CutoutTests
{
    private const string File = "skins/red-spot/m.png";

    private static (FakeGame Game, ModProject Mod, IReadOnlyList<SpeciesSkins> Species) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        Write(ws.DataDir);
        return (game, ModProject.Create(ws, "red-spot-carcharo", null, null), SpeciesSkinsReader.Load(ws));
    }

    /// <summary>An RGBA image of size×size: colour 200 everywhere; alpha from the function.</summary>
    private static byte[] Rgba(int size, Func<int, byte> alpha) =>
        Enumerable.Range(0, size * size).SelectMany(i => new byte[] { 200, 100, 50, alpha(i) }).ToArray();

    private static byte[] Png(int size, Func<int, byte> alpha)
    {
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(Rgba(size, alpha), size, size, ColorComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }

    private static ImageResult Image(int size, Func<int, byte> alpha) => ImageResult.FromMemory(Png(size, alpha), StbImageSharp.ColorComponents.RedGreenBlueAlpha);

    private static void SkinWithMaleDiffuse(ModProject mod, byte[] png)
    {
        var path = Path.Combine(mod.Dir, File);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, png);
        mod.Manifest.Skins.Add(new SkinEntry { Id = "red-spot", Species = "Carcharodontosaurus", Name = "Red spot", Base = "Alt 1", Male = new() { ["diffuse"] = File } });
        mod.Save();
    }

    private static Func<int, byte> HalfCut => i => (byte)(i % 2 == 0 ? 0 : 255);
    private static Func<int, byte> Opaque => _ => 255;

    [Fact]
    public void An_opaque_skin_diffuse_over_a_base_with_cutouts_is_warned_and_listed()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        SkinWithMaleDiffuse(mod, Png(4, Opaque));
        AssetRecord? asked = null;

        var result = new ModChecker(_ => (4, 4), a => { asked = a; return Image(4, HalfCut); }).Check(mod, Index(), species);

        Assert.Equal(MaleDiffuse, asked?.Guid); // the base skin's male diffuse
        Assert.Contains(result.Warnings, w => w.Contains("see-through") && w.Contains("m.png"));
        Assert.Equal([File], result.MissingCutouts);
    }

    [Fact]
    public void A_png_that_keeps_transparency_is_not_checked_against_the_base()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        SkinWithMaleDiffuse(mod, Png(4, HalfCut));
        var asked = 0;

        var result = new ModChecker(_ => (4, 4), _ => { asked++; return Image(4, HalfCut); }).Check(mod, Index(), species);

        Assert.Equal(0, asked);
        Assert.Empty(result.MissingCutouts);
    }

    [Fact]
    public void An_opaque_base_needs_no_cutouts()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        SkinWithMaleDiffuse(mod, Png(4, Opaque));

        var result = new ModChecker(_ => (4, 4), _ => Image(4, Opaque)).Check(mod, Index(), species);

        Assert.Empty(result.MissingCutouts);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("see-through"));
    }

    [Fact]
    public void A_replacement_that_drops_the_cutouts_is_warned()
    {
        var (game, mod, _) = Setup();
        using var _ = game;
        var path = Path.Combine(mod.Dir, "textures", "d.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, Png(4, Opaque));
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = "T_carch_alt1_male_D", File = "textures/d.png" });
        mod.Save();

        var result = new ModChecker(_ => (4, 4), _ => Image(4, HalfCut)).Check(mod, Index());

        Assert.Equal(["textures/d.png"], result.MissingCutouts);
    }

    [Fact]
    public void Restore_copies_the_base_transparency_into_the_png_and_keeps_its_colours()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        SkinWithMaleDiffuse(mod, Png(4, Opaque));

        var restored = new CutoutRestorer(_ => Image(8, i => (byte)(i % 8 < 4 ? 0 : 255))).Restore(mod, Index(), species); // left half see-through, at twice the size

        Assert.Equal([File], restored.Restored);
        var after = ImageResult.FromMemory(System.IO.File.ReadAllBytes(Path.Combine(mod.Dir, File)), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((4, 4), (after.Width, after.Height));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Enumerable.Range(0, 4).Select(x => after.Data[x * 4 + 3]).ToArray()); // first row: resampled
        Assert.Equal(new byte[] { 200, 100, 50 }, after.Data[4..7]); // colours untouched
        Assert.Empty(new ModChecker(_ => (4, 4), _ => Image(8, HalfCut)).Check(mod, Index(), species).MissingCutouts);
    }

    [Theory]
    [InlineData("DXT1", false)]
    [InlineData("DXT1Crunched", false)]
    [InlineData("RGB24", false)]
    [InlineData("RGB565", false)]
    [InlineData("BC4", false)]
    [InlineData("BC5", false)]
    [InlineData("BC6H", false)]
    [InlineData("R8", false)]
    [InlineData("DXT5", true)]
    [InlineData("RGBA32", true)]
    [InlineData("BC7", true)]
    [InlineData("SomethingNew", true)] // unknown formats are decoded to be safe
    public void Textures_stored_without_alpha_are_not_decoded(string format, bool mayHaveAlpha)
    {
        Assert.Equal(mayHaveAlpha, Cutouts.MayHaveAlpha(format));
    }

    private static void WriteFile(ModProject mod, string relative, byte[] bytes)
    {
        var path = Path.Combine(mod.Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, bytes);
    }

    private static byte[] Png(int width, int height, Func<int, byte> alpha)
    {
        var rgba = Enumerable.Range(0, width * height).SelectMany(i => new byte[] { 200, 100, 50, alpha(i) }).ToArray();
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(rgba, width, height, ColorComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }

    [Fact]
    public void Restore_reports_files_it_cannot_read_or_that_are_not_pngs_and_fixes_the_rest()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        WriteFile(mod, "skins/red-spot/m.png", Png(4, 4, _ => 255)); // will be locked
        WriteFile(mod, "skins/red-spot/f.png", Png(4, 4, _ => 255));
        WriteFile(mod, "skins/red-spot/j.png", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]); // a JPEG named .png
        mod.Manifest.Skins.Add(new SkinEntry { Id = "red-spot", Species = "Carcharodontosaurus", Name = "Red spot", Base = "Alt 1",
            Male = new() { ["diffuse"] = "skins/red-spot/m.png" }, Female = new() { ["diffuse"] = "skins/red-spot/f.png" } });
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = "T_carch_alt1_male_D", File = "skins/red-spot/j.png" });
        mod.Save();
        var jpeg = System.IO.File.ReadAllBytes(Path.Combine(mod.Dir, "skins/red-spot/j.png"));

        CutoutRestore result;
        using (new FileStream(Path.Combine(mod.Dir, "skins/red-spot/m.png"), FileMode.Open, FileAccess.Read, FileShare.None))
            result = new CutoutRestorer(_ => Image(4, HalfCut)).Restore(mod, Index(), species);

        Assert.Equal(["skins/red-spot/f.png"], result.Restored);
        Assert.Contains(result.Problems, p => p.Contains("m.png") && p.Contains("could not be read"));
        Assert.Contains(result.Problems, p => p.Contains("j.png") && p.Contains("not a PNG"));
        Assert.Equal(jpeg, System.IO.File.ReadAllBytes(Path.Combine(mod.Dir, "skins/red-spot/j.png"))); // left alone
    }

    [Fact]
    public void Restore_resamples_to_odd_sizes()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        SkinWithMaleDiffuse(mod, Png(5, 3, _ => 255));

        var result = new CutoutRestorer(_ => Image(8, i => (byte)(i % 8 < 4 ? 0 : 255))).Restore(mod, Index(), species);

        var after = ImageResult.FromMemory(System.IO.File.ReadAllBytes(Path.Combine(mod.Dir, File)), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((5, 3), (after.Width, after.Height));
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255 }, Enumerable.Range(0, 5).Select(x => after.Data[x * 4 + 3]).ToArray());
        Assert.Single(result.Restored);
    }

    [Fact]
    public void A_png_shared_by_several_targets_is_judged_by_the_one_with_cutouts()
    {
        var (game, mod, species) = Setup();
        using var _ = game;
        WriteFile(mod, File, Png(4, 4, _ => 255));
        mod.Manifest.Skins.Add(new SkinEntry { Id = "red-spot", Species = "Carcharodontosaurus", Name = "Red spot", Base = "Alt 1",
            Male = new() { ["diffuse"] = File }, Female = new() { ["diffuse"] = File.Replace('/', '\\') } }); // one file, two spellings
        mod.Save();
        Func<AssetRecord, ImageResult?> pixels = a => a.Guid == FemaleDiffuse ? Image(4, HalfCut) : Image(4, Opaque); // only the female base has cutouts

        var check = new ModChecker(_ => (4, 4), pixels).Check(mod, Index(), species);
        var restored = new CutoutRestorer(pixels).Restore(mod, Index(), species);

        Assert.Single(check.MissingCutouts);
        Assert.Single(check.Warnings, w => w.Contains("see-through"));
        Assert.Single(restored.Restored);
    }

    [Fact]
    public void Masks_are_not_checked_for_cutouts_only_colour_textures_are()
    {
        var (game, mod, _) = Setup();
        using var _ = game;
        WriteFile(mod, "textures/x.png", Png(4, 4, _ => 255));
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = "T_carch_extra", File = "textures/x.png" }); // an extra map: its alpha is data
        mod.Save();

        var result = new ModChecker(_ => (4, 4), _ => Image(4, HalfCut)).Check(mod, Index());

        Assert.Empty(result.MissingCutouts);
    }
}
