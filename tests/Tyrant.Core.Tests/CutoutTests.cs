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

        Assert.Equal([File], restored);
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
}
