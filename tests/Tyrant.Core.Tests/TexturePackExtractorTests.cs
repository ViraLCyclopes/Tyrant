using StbImageSharp;
using Tyrant.Core.Errors;
using Tyrant.Core.Textures;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class TexturePackExtractorTests
{
    private static string TempDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;

    private static byte[] Image(int w, int h, Func<int, int, (byte, byte, byte, byte)> at)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var (r, g, b, a) = at(x, y);
            var i = (y * w + x) * 4;
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (r, g, b, a);
        }
        return rgba;
    }

    /// <summary>A pack holding the given images, cooked as the game's would be.</summary>
    private static string Pack(string dir, params (string Source, SlotKind Kind, TextureCompression Mode, byte[] Rgba, int W, int H)[] images)
    {
        var cooked = images.Select(i => (i, TextureCooker.Cook(i.Rgba, i.W, i.H, i.Kind, i.Mode))).ToList();
        var entries = cooked.Select(c => new TexturePackEntry
        {
            Source = c.i.Source, Kind = c.i.Kind, Mode = c.i.Mode, Format = c.Item2.Format, Linear = c.Item2.Linear,
            Width = c.Item2.Width, Height = c.Item2.Height, MipCount = c.Item2.MipCount, Length = c.Item2.Data.Length,
        }).ToList();
        var path = Path.Combine(dir, TexturePack.FileName);
        using var stream = File.Create(path);
        TexturePack.Write(stream, entries, (i, s) => s.Write(cooked[i].Item2.Data));
        return path;
    }

    private static ImageResult Png(string path) => ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);

    [Theory]
    [InlineData(TextureCompression.Auto, 255)] // DXT1
    [InlineData(TextureCompression.Auto, 128)] // DXT5
    [InlineData(TextureCompression.Bc7, 255)]
    public void Colour_textures_come_back_the_right_way_up_close_to_the_original(TextureCompression mode, byte alpha)
    {
        var dir = TempDir();
        // Top half red, bottom half blue.
        var rgba = Image(16, 16, (_, y) => y < 8 ? ((byte)255, (byte)0, (byte)0, alpha) : ((byte)0, (byte)0, (byte)255, alpha));
        var pack = Pack(dir, ("skins/blue/male_D.png", SlotKind.Color, mode, rgba, 16, 16));
        var outDir = Path.Combine(dir, "out");

        var result = TexturePackExtractor.ExtractTo(pack, outDir);

        Assert.Equal(["skins/blue/male_D.png"], result.Written);
        var png = Png(Path.Combine(outDir, "skins", "blue", "male_D.png"));
        Assert.Equal((16, 16), (png.Width, png.Height));
        Assert.True(png.Data[0] > 200 && png.Data[2] < 50, "top-left should be red");
        var last = png.Data.Length - 4;
        Assert.True(png.Data[last + 2] > 200 && png.Data[last] < 50, "bottom-right should be blue");
        Assert.InRange(png.Data[3], alpha - 10, alpha + 10);
    }

    [Fact]
    public void A_normal_map_comes_back_as_a_standard_blue_map()
    {
        var dir = TempDir();
        var rgba = Image(16, 16, (_, _) => (128, 128, 255, 255)); // straight up
        var pack = Pack(dir, ("skins/blue/male_N.png", SlotKind.Normal, TextureCompression.Auto, rgba, 16, 16));

        TexturePackExtractor.ExtractTo(pack, dir);

        var png = Png(Path.Combine(dir, "skins", "blue", "male_N.png"));
        Assert.InRange(png.Data[0], 118, 138);
        Assert.InRange(png.Data[1], 118, 138);
        Assert.True(png.Data[2] > 240, $"blue {png.Data[2]}");
        Assert.Equal(255, png.Data[3]);
    }

    [Fact]
    public void A_mask_keeps_its_values()
    {
        var dir = TempDir();
        var rgba = Image(16, 16, (_, _) => (200, 40, 0, 255));
        var pack = Pack(dir, ("skins/blue/male_pattern.png", SlotKind.Data, TextureCompression.Bc7, rgba, 16, 16));

        TexturePackExtractor.ExtractTo(pack, dir);

        var png = Png(Path.Combine(dir, "skins", "blue", "male_pattern.png"));
        Assert.InRange(png.Data[0], 195, 205);
        Assert.InRange(png.Data[1], 35, 45);
    }

    [Fact]
    public void An_entry_pointing_outside_the_folder_is_skipped()
    {
        var dir = TempDir();
        var rgba = Image(8, 8, (_, _) => (10, 20, 30, 255));
        var pack = Pack(dir, ("../evil.png", SlotKind.Color, TextureCompression.Auto, rgba, 8, 8), ("ok.png", SlotKind.Color, TextureCompression.Auto, rgba, 8, 8));
        var outDir = Path.Combine(dir, "out");

        var result = TexturePackExtractor.ExtractTo(pack, outDir);

        Assert.Equal(["ok.png"], result.Written);
        Assert.Equal(["../evil.png"], result.Skipped);
        Assert.False(File.Exists(Path.Combine(dir, "evil.png")));
    }

    [Fact]
    public void Only_the_wanted_entries_are_written()
    {
        var dir = TempDir();
        var rgba = Image(8, 8, (_, _) => (10, 20, 30, 255));
        var pack = Pack(dir, ("a.png", SlotKind.Color, TextureCompression.Auto, rgba, 8, 8), ("b.png", SlotKind.Color, TextureCompression.Auto, rgba, 8, 8));

        var result = TexturePackExtractor.ExtractTo(pack, Path.Combine(dir, "out"), source => source == "b.png");

        Assert.Equal(["b.png"], result.Written);
        Assert.False(File.Exists(Path.Combine(dir, "out", "a.png")));
    }

    [Fact]
    public void A_file_that_is_not_a_pack_says_so()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "x.tpack");
        File.WriteAllText(path, "hello");

        var ex = Assert.Throws<TyrantException>(() => TexturePackExtractor.ExtractTo(path, dir));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains("'x.tpack' is not a Tyrant texture pack", ex.Message);
    }
}
