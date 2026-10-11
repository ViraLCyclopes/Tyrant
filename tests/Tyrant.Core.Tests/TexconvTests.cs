using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Tyrant.Core.Textures;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class TexconvTests
{
    private static string Dir(string name = "") =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), name)).FullName;

    private static readonly (byte R, byte G, byte B)[] Colours =
        [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0), (0, 255, 255), (255, 0, 255), (128, 128, 128)];

    /// <summary>A chain whose every level is one solid, different colour, so a regenerated mip would show.</summary>
    private static List<MipLevel> Chain(int width, int height)
    {
        var levels = new List<MipLevel>();
        for (int w = width, h = height, i = 0; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2), i++)
        {
            var rgba = new byte[w * h * 4];
            var (r, g, b) = Colours[i % Colours.Length];
            for (var p = 0; p < rgba.Length; p += 4) (rgba[p], rgba[p + 1], rgba[p + 2], rgba[p + 3]) = (r, g, b, 255);
            levels.Add(new MipLevel(w, h, rgba));
            if (w == 1 && h == 1) return levels;
        }
    }

    private static string Fake(string body)
    {
        var path = Path.Combine(Dir(), "texconv.cmd");
        File.WriteAllText(path, "@echo off\r\n" + body + "\r\n");
        return path;
    }

    [Fact]
    public void The_dds_written_for_texconv_has_a_dx10_rgba_header_and_every_level()
    {
        var levels = Chain(8, 4);
        using var stream = new MemoryStream();

        Dds.WriteRgba(stream, levels);

        var file = stream.ToArray();
        Assert.Equal("DDS ", System.Text.Encoding.ASCII.GetString(file, 0, 4));
        Assert.Equal((4, 8, levels.Count), (BitConverter.ToInt32(file, 12), BitConverter.ToInt32(file, 16), BitConverter.ToInt32(file, 28)));
        Assert.Equal("DX10", System.Text.Encoding.ASCII.GetString(file, 84, 4));
        Assert.Equal((Dds.Rgba8, 3, 1), (BitConverter.ToInt32(file, 128), BitConverter.ToInt32(file, 132), BitConverter.ToInt32(file, 140)));
        Assert.Equal(Dds.HeaderBytes + levels.Sum(l => l.Rgba.Length), file.Length);
        Assert.Equal(levels[1].Rgba, file[(Dds.HeaderBytes + levels[0].Rgba.Length)..(Dds.HeaderBytes + levels[0].Rgba.Length + levels[1].Rgba.Length)]);
    }

    private static byte[] Bc7File(int width, int height, int mips, int format = Dds.Bc7, long? dataBytes = null)
    {
        var data = dataBytes ?? UnityTextureFormats.DataLength(UnityTextureFormats.Bc7, width, height, mips);
        var file = new byte[Dds.HeaderBytes + data];
        System.Text.Encoding.ASCII.GetBytes("DDS ").CopyTo(file, 0);
        BitConverter.GetBytes(124).CopyTo(file, 4);
        BitConverter.GetBytes(height).CopyTo(file, 12);
        BitConverter.GetBytes(width).CopyTo(file, 16);
        BitConverter.GetBytes(mips).CopyTo(file, 28);
        System.Text.Encoding.ASCII.GetBytes("DX10").CopyTo(file, 84);
        BitConverter.GetBytes(format).CopyTo(file, 128);
        file[Dds.HeaderBytes] = 0x5A; // first data byte
        return file;
    }

    [Fact]
    public void Reading_texconvs_output_returns_the_blocks_and_rejects_anything_else()
    {
        var data = Dds.ReadBc7(Bc7File(8, 4, 4), 8, 4, 4);
        Assert.Equal(UnityTextureFormats.DataLength(UnityTextureFormats.Bc7, 8, 4, 4), data.Length);
        Assert.Equal(0x5A, data[0]);

        Assert.Throws<InvalidDataException>(() => Dds.ReadBc7(Bc7File(8, 4, 4, format: 77), 8, 4, 4));
        Assert.Throws<InvalidDataException>(() => Dds.ReadBc7(Bc7File(8, 8, 4), 8, 4, 4));
        Assert.Throws<InvalidDataException>(() => Dds.ReadBc7(Bc7File(8, 4, 1), 8, 4, 4));
        Assert.Throws<InvalidDataException>(() => Dds.ReadBc7(Bc7File(8, 4, 4, dataBytes: 16), 8, 4, 4));
        Assert.Throws<InvalidDataException>(() => Dds.ReadBc7(new byte[10], 8, 4, 4));
    }

    [Fact]
    public void The_shipped_texconv_compresses_every_level_as_given()
    {
        Assert.True(Texconv.Default.Available, $"{Texconv.Default.ExePath} should be copied next to the tests");
        var levels = Chain(64, 32);
        var temp = Dir("with space");

        var data = new Texconv(Texconv.Default.ExePath, temp).CompressBc7(levels, CancellationToken.None);

        Assert.Equal(UnityTextureFormats.DataLength(UnityTextureFormats.Bc7, 64, 32, levels.Count), data.Length);
        var at = 0;
        for (var i = 0; i < levels.Count; i++)
        {
            int w = Math.Max(4, (levels[i].Width + 3) / 4 * 4), h = Math.Max(4, (levels[i].Height + 3) / 4 * 4);
            var bytes = (int)UnityTextureFormats.DataLength(UnityTextureFormats.Bc7, levels[i].Width, levels[i].Height, 1);
            var pixel = new BcDecoder().DecodeRaw(data[at..(at + bytes)], w, h, CompressionFormat.Bc7)[0];
            at += bytes;
            var (r, g, b) = Colours[i % Colours.Length];
            Assert.InRange(Math.Abs(pixel.r - r) + Math.Abs(pixel.g - g) + Math.Abs(pixel.b - b), 0, 12);
        }
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }

    [Fact]
    public void A_failing_texconv_is_reported_with_its_output()
    {
        var temp = Dir();
        var texconv = new Texconv(Fake("echo cannot open the device\r\nexit /b 3"), temp);

        var ex = Assert.Throws<TexconvException>(() => texconv.CompressBc7(Chain(8, 8), CancellationToken.None));

        Assert.Contains("3", ex.Message);
        Assert.Contains("cannot open the device", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }

    [Fact]
    public void A_missing_texconv_is_not_available_and_says_so()
    {
        var texconv = new Texconv(Path.Combine(Dir(), Texconv.FileName));

        Assert.False(texconv.Available);
        Assert.Throws<TexconvException>(() => texconv.CompressBc7(Chain(8, 8), CancellationToken.None));
    }

    [Fact]
    public void A_texconv_that_hangs_is_stopped_after_its_time_limit_and_reported()
    {
        var temp = Dir();
        var texconv = new Texconv(Fake("ping -n 60 127.0.0.1 >nul"), temp, TimeSpan.FromSeconds(1));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var ex = Assert.Throws<TexconvException>(() => texconv.CompressBc7(Chain(8, 8), CancellationToken.None));

        Assert.Contains("did not finish", ex.Message);
        Assert.InRange(clock.Elapsed.TotalSeconds, 0, 20);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }

    [Fact]
    public void Cancelling_stops_texconv_at_once_and_leaves_no_temp_folder()
    {
        var temp = Dir();
        var texconv = new Texconv(Fake("ping -n 60 127.0.0.1 >nul"), temp);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(() => texconv.CompressBc7(Chain(8, 8), cts.Token));

        Assert.InRange(clock.Elapsed.TotalSeconds, 0, 20);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }
}
