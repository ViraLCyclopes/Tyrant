using StbImageSharp;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ColorPreviewTests
{
    private static byte[] Fill(int n, byte r, byte g, byte b, byte a = 255) => Enumerable.Range(0, n).SelectMany(_ => new[] { r, g, b, a }).ToArray();

    private static SkinColorSet Set(string a, string? b = null, float strength = 1, float softness = 0.25f) => new()
    {
        A = [Parse(a)], B = b is null ? null : [Parse(b)], Strength = new FloatRange(strength, strength), Softness = new FloatRange(softness, softness),
    };

    private static Rgb Parse(string hex) => Rgb.TryParse(hex, out var c) ? c : throw new ArgumentException(hex);

    private static (byte R, byte G, byte B) Pixel(byte[] rgba, int i) => (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]);

    [Fact]
    public void Pattern_red_zero_keeps_the_texture()
    {
        var maps = new PreviewMaps(1, 1, Fill(1, 100, 110, 120), Fill(1, 0, 0, 0), null);

        var result = ColorPreview.Render(maps, Set("#ff0000"), null, new Random(1));

        Assert.Equal(((byte)100, (byte)110, (byte)120), Pixel(result, 0));
    }

    [Fact]
    public void Low_red_takes_colour_a_and_high_red_colour_b()
    {
        var maps = new PreviewMaps(2, 1, Fill(2, 100, 100, 100), [10, 0, 0, 255, 255, 0, 0, 255], null);

        var result = ColorPreview.Render(maps, Set("#ff0000", "#0000ff", softness: 1), null, new Random(1));

        Assert.True(Pixel(result, 0).R > 200 && Pixel(result, 0).B < 60, $"{Pixel(result, 0)}");
        Assert.Equal(((byte)0, (byte)0, (byte)255), Pixel(result, 1));
    }

    [Fact]
    public void Strength_blends_the_colour_over_the_texture()
    {
        var maps = new PreviewMaps(1, 1, Fill(1, 0, 0, 0), Fill(1, 255, 0, 0), null);

        var result = ColorPreview.Render(maps, Set("#ffffff", strength: 0.5f), null, new Random(1));

        Assert.InRange(Pixel(result, 0).R, (byte)126, (byte)129);
    }

    [Fact]
    public void Secondary_shows_where_pattern_green_is_set_and_eyes_where_extra_red_is_above_90_percent()
    {
        var set = Set("#000000");
        set.Secondary = [Parse("#00ff00")];
        set.Eye = [Parse("#ff00ff")];
        var maps = new PreviewMaps(2, 1, Fill(2, 0, 0, 0), [255, 255, 0, 255, 0, 0, 0, 255], [0, 0, 0, 255, 240, 0, 0, 255]);

        var result = ColorPreview.Render(maps, set, null, new Random(1));

        Assert.Equal(((byte)0, (byte)255, (byte)0), Pixel(result, 0));
        Assert.Equal(((byte)255, (byte)0, (byte)255), Pixel(result, 1));
    }

    [Fact]
    public void Tint_shifts_only_where_pattern_red_is_set_and_no_colour_means_no_recolour()
    {
        var tint = new SkinTint { Value = new FloatRange(-0.5f, -0.5f) };
        var maps = new PreviewMaps(2, 1, Fill(2, 200, 200, 200), [255, 0, 0, 255, 0, 0, 0, 255], null);

        var result = ColorPreview.Render(maps, null, tint, new Random(1));

        Assert.True(Pixel(result, 0).R < 120, $"{Pixel(result, 0)}");
        Assert.Equal(((byte)200, (byte)200, (byte)200), Pixel(result, 1));
    }

    [Fact]
    public void Diffuse_alpha_is_kept()
    {
        var maps = new PreviewMaps(1, 1, Fill(1, 1, 2, 3, 40), Fill(1, 255, 0, 0), null);

        var result = ColorPreview.Render(maps, Set("#ffffff"), null, new Random(1));

        Assert.Equal(40, result[3]);
    }

    [Theory]
    [InlineData("normal", true)]
    [InlineData("albino", false)]
    public void Each_variant_uses_its_own_set_and_tint(string variant, bool usesPattern)
    {
        var albino = Set("#eeeeee");
        albino.Tint = new SkinTint();
        var colors = new SkinColors { Tint = new SkinTint(), Pattern = Set("#111111"), Albino = albino };

        var (set, tint) = ColorPreview.For(colors, variant);

        Assert.Same(usesPattern ? colors.Pattern : colors.Albino, set);
        Assert.Same(usesPattern ? colors.Tint : colors.Albino!.Tint, tint);
    }

    private static (FakeGame Game, Workspace Ws, ModProject Mod) ModWithSkin(bool withDiffuse)
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "red-spot", null, null);
        var male = new Dictionary<string, string> { ["pattern"] = "skins/blue/male_pattern.png" };
        WritePng(Path.Combine(mod.Dir, "skins", "blue", "male_pattern.png"), Fill(16, 255, 0, 0), 4);
        if (withDiffuse)
        {
            WritePng(Path.Combine(mod.Dir, "skins", "blue", "male_D.png"), Fill(16, 200, 200, 200), 4);
            male["diffuse"] = "skins/blue/male_D.png";
        }
        mod.Manifest.Skins.Add(new SkinEntry { Id = "blue", Species = "Carcharodontosaurus", Name = "Blue", Base = "0", Male = male });
        mod.Save();
        return (game, ws, mod);
    }

    [Fact]
    public void A_preview_is_written_per_animal_from_the_mods_own_files()
    {
        var (game, ws, mod) = ModWithSkin(withDiffuse: true);
        using var _ = game;
        var outDir = Path.Combine(ws.Dir, "cache", "previews", "colors");

        var files = new ModImages(_ => null).ColorPreview(mod, "blue", "{\"pattern\":{\"a\":\"#0000ff\"}}", "normal", "male", seed: 3, count: 2, size: 8, outDir, null, null);

        Assert.Equal(2, files.Count);
        var first = ImageResult.FromMemory(File.ReadAllBytes(files[0]), ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((8, 8), (first.Width, first.Height));
        Assert.Equal(((byte)0, (byte)0, (byte)255), (first.Data[0], first.Data[1], first.Data[2]));
    }

    [Fact]
    public void A_preview_without_any_diffuse_says_why()
    {
        var (game, ws, mod) = ModWithSkin(withDiffuse: false);
        using var _ = game;

        var ex = Assert.Throws<TyrantException>(() => new ModImages(_ => null).ColorPreview(mod, "blue", null, "normal", "male", 1, 1, 8, Path.Combine(ws.Dir, "out"), null, null));

        Assert.Equal(TyrantErrorCode.TargetNotFound, ex.Code);
        Assert.Contains("diffuse", ex.Message);
    }

    [Fact]
    public void A_thumbnail_is_a_small_copy_and_a_missing_file_gives_none()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "red-spot", null, null);
        WritePng(Path.Combine(mod.Dir, "textures", "a.png"), Fill(64, 1, 2, 3), 8);
        var images = new ModImages(_ => null);

        var thumb = images.Thumbnail(mod, "textures/a.png", 4, Path.Combine(ws.Dir, "out"));

        Assert.NotNull(thumb);
        Assert.Equal(4, ImageResult.FromMemory(File.ReadAllBytes(thumb!), ColorComponents.RedGreenBlueAlpha).Width);
        Assert.Null(images.Thumbnail(mod, "textures/missing.png", 4, Path.Combine(ws.Dir, "out")));
        Assert.Null(images.Thumbnail(mod, "../outside.png", 4, Path.Combine(ws.Dir, "out")));
    }

    private static void WritePng(string path, byte[] rgba, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(rgba, size, size, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }
}
