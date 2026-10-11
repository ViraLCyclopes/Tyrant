using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Tyrant.Core.Textures;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class TextureCookerTests
{
    private static byte[] Image(int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> at)
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

    private static ColorRgba32[] Level0(CookedTexture t)
    {
        var bytes = (int)UnityTextureFormats.DataLength(t.Format, t.Width, t.Height, 1);
        var format = t.Format == UnityTextureFormats.Dxt1 ? CompressionFormat.Bc1 : t.Format == UnityTextureFormats.Dxt5 ? CompressionFormat.Bc3 : CompressionFormat.Bc7;
        return new BcDecoder().DecodeRaw(t.Data[..bytes], t.Width, t.Height, format);
    }

    [Theory]
    [InlineData(SlotKind.Color, 255, TextureCompression.Auto, UnityTextureFormats.Dxt1, false)]
    [InlineData(SlotKind.Color, 128, TextureCompression.Auto, UnityTextureFormats.Dxt5, false)]
    [InlineData(SlotKind.Data, 255, TextureCompression.Auto, UnityTextureFormats.Dxt1, true)]
    [InlineData(SlotKind.Normal, 255, TextureCompression.Auto, UnityTextureFormats.Dxt5, true)]
    [InlineData(SlotKind.Color, 255, TextureCompression.Bc7, UnityTextureFormats.Bc7, false)]
    [InlineData(SlotKind.Normal, 255, TextureCompression.Bc7, UnityTextureFormats.Bc7, true)]
    public void Each_kind_and_mode_gets_the_games_format_and_colour_space(SlotKind kind, byte alpha, TextureCompression mode, int format, bool linear)
    {
        var cooked = TextureCooker.Cook(Image(16, 16, (_, _) => (128, 128, 255, alpha)), 16, 16, kind, mode);

        Assert.Equal((format, linear, 16, 16, 5), (cooked.Format, cooked.Linear, cooked.Width, cooked.Height, cooked.MipCount));
        Assert.Equal(UnityTextureFormats.DataLength(format, 16, 16, 5), cooked.Data.Length);
    }

    [Fact]
    public void Odd_and_non_square_sizes_are_scaled_up_to_multiples_of_4_with_a_full_chain()
    {
        var cooked = TextureCooker.Cook(Image(6, 10, (x, y) => ((byte)(x * 40), (byte)(y * 25), 0, 255)), 6, 10, SlotKind.Color, TextureCompression.Auto);

        Assert.Equal((8, 12), (cooked.Width, cooked.Height));
        Assert.Equal((8, 12), TextureCooker.CookedSize(6, 10));
        Assert.Equal(UnityTextureFormats.FullMipCount(8, 12), cooked.MipCount); // 8x12, 4x6, 2x3, 1x1
        Assert.Equal(UnityTextureFormats.DataLength(cooked.Format, 8, 12, cooked.MipCount), cooked.Data.Length);
    }

    [Fact]
    public void Rows_are_stored_bottom_first_as_unity_reads_them()
    {
        // Top half red, bottom half blue (PNG order: top row first).
        var cooked = TextureCooker.Cook(Image(8, 8, (_, y) => y < 4 ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)0, (byte)255, (byte)255)),
            8, 8, SlotKind.Color, TextureCompression.Auto);

        var pixels = Level0(cooked);
        Assert.True(pixels[0].b > 200 && pixels[0].r < 50); // first stored row = the image's bottom
        Assert.True(pixels[^1].r > 200 && pixels[^1].b < 50);
    }

    [Fact]
    public void A_standard_normal_map_is_packed_like_the_games()
    {
        var cooked = TextureCooker.Cook(Image(8, 8, (_, _) => (128, 128, 255, 255)), 8, 8, SlotKind.Normal, TextureCompression.Auto);

        var p = Level0(cooked)[0];
        Assert.True(p.r > 240, $"red {p.r}");
        Assert.InRange(p.a, 120, 136); // x
        Assert.InRange(p.g, 120, 136); // y
    }

    [Fact]
    public void Colour_mips_average_in_linear_light_and_masks_as_stored()
    {
        var level = new MipLevel(2, 2, Image(2, 2, (x, _) => x == 0 ? ((byte)0, (byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255)));

        Assert.InRange(MipChain.Half(level, MipFilter.Srgb).Rgba[0], 186, 190); // sRGB of linear 0.5
        Assert.InRange(MipChain.Half(level, MipFilter.Plain).Rgba[0], 127, 128);
        Assert.Equal(255, MipChain.Half(level, MipFilter.Srgb).Rgba[3]);
    }

    [Fact]
    public void Normal_mips_stay_unit_length()
    {
        // Packed normals (x in alpha, y in green) tilted opposite ways: the plain average would shrink them.
        var level = new MipLevel(2, 1, Image(2, 1, (x, _) => x == 0 ? ((byte)255, (byte)128, (byte)128, (byte)230) : ((byte)255, (byte)128, (byte)128, (byte)25)));

        var half = MipChain.Half(level, MipFilter.PackedNormal).Rgba;
        double nx = half[3] / 255.0 * 2 - 1, ny = half[1] / 255.0 * 2 - 1;
        Assert.Equal(255, half[0]);
        Assert.Equal(half[1], half[2]);
        Assert.InRange(nx * nx + ny * ny, 0, 0.01); // straight up: z carries the length, x and y cancel
    }

    [Fact]
    public void A_png_that_cannot_be_read_says_so()
    {
        var path = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N") + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

        Assert.ThrowsAny<Exception>(() => TextureCooker.CookPng(path, SlotKind.Color, TextureCompression.Auto));
    }

    private static Texconv Missing() =>
        new(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), Texconv.FileName));

    [Theory]
    [InlineData(64, 64)]
    [InlineData(8, 4)]
    [InlineData(4, 4)]
    public void Bc7_goes_through_texconv_at_any_size_with_every_mip(int w, int h)
    {
        var slower = 0;
        var cooked = TextureCooker.Cook(Image(w, h, (x, y) => ((byte)(x * 255 / w), (byte)(y * 255 / h), 90, 255)), w, h,
            SlotKind.Color, TextureCompression.Bc7, Texconv.Default, () => slower++);

        Assert.Equal(0, slower);
        Assert.Equal((UnityTextureFormats.Bc7, UnityTextureFormats.FullMipCount(w, h)), (cooked.Format, cooked.MipCount));
        Assert.Equal(UnityTextureFormats.DataLength(UnityTextureFormats.Bc7, w, h, cooked.MipCount), cooked.Data.Length);
        var corner = Level0(cooked)[(h - 1) * w + (w - 1)]; // rows are stored bottom first: the last row is the image's top
        Assert.InRange(Math.Abs(corner.r - 255 * (w - 1) / w), 0, 12);
        Assert.InRange(corner.g, 0, 12);
    }

    [Fact]
    public void Without_texconv_bc7_still_cooks_and_says_it_is_slower()
    {
        var slower = 0;
        var cooked = TextureCooker.Cook(Image(16, 16, (_, _) => (40, 160, 220, 255)), 16, 16, SlotKind.Color, TextureCompression.Bc7, Missing(), () => slower++);

        Assert.Equal(1, slower);
        Assert.Equal(UnityTextureFormats.Bc7, cooked.Format);
        var pixel = Level0(cooked)[0];
        Assert.InRange(Math.Abs(pixel.r - 40) + Math.Abs(pixel.g - 160) + Math.Abs(pixel.b - 220), 0, 12);
    }

    [Fact]
    public void Cancelling_stops_a_texture_being_encoded()
    {
        var size = 1024;
        var random = new Random(1);
        var noise = new byte[size * size * 4];
        random.NextBytes(noise);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.ThrowsAny<OperationCanceledException>(() =>
            TextureCooker.Cook(noise, size, size, SlotKind.Color, TextureCompression.Bc7, Missing(), null, cts.Token)); // BCnEncoder's BC7 takes seconds here
        Assert.ThrowsAny<OperationCanceledException>(() =>
            TextureCooker.Cook(noise, size, size, SlotKind.Color, TextureCompression.Auto, null, null, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void The_cooker_version_is_raised_for_the_new_bc7_encoder() => Assert.Equal(2, TextureCooker.Version);
}
