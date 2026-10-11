using Tyrant.Framework.Core;

namespace Tyrant.Core.Textures;

/// <summary>The DDS files Tyrant hands to texconv and reads back (DX10 header only).</summary>
internal static class Dds
{
    public const int Rgba8 = 28; // DXGI_FORMAT_R8G8B8A8_UNORM
    public const int Bc7 = 98; // DXGI_FORMAT_BC7_UNORM
    public const int HeaderBytes = 4 + 124 + 20; // magic, header, DX10 header

    /// <summary>An uncompressed RGBA DDS holding every level as given, largest first.</summary>
    public static void WriteRgba(Stream stream, IReadOnlyList<MipLevel> levels)
    {
        var top = levels[0];
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write(0x20534444u); // "DDS "
            w.Write(124); // header size
            w.Write(0x1 | 0x2 | 0x4 | 0x8 | 0x1000 | 0x20000); // caps, height, width, pitch, pixel format, mip count
            w.Write(top.Height);
            w.Write(top.Width);
            w.Write(top.Width * 4); // pitch
            w.Write(0); // depth
            w.Write(levels.Count);
            for (var i = 0; i < 11; i++) w.Write(0); // reserved
            w.Write(32); // pixel format size
            w.Write(0x4); // DDPF_FOURCC
            w.Write(0x30315844u); // "DX10"
            for (var i = 0; i < 5; i++) w.Write(0); // bit count and masks
            w.Write(0x1000 | 0x8 | 0x400000); // texture, complex, mipmap
            for (var i = 0; i < 4; i++) w.Write(0); // caps 2-4, reserved
            w.Write(Rgba8);
            w.Write(3); // 2D texture
            w.Write(0); // misc flags
            w.Write(1); // array size
            w.Write(0); // misc flags 2
        }
        foreach (var level in levels) stream.Write(level.Rgba);
    }

    /// <summary>The block data of a BC7 DDS, checked against the size and mip count that were asked for.</summary>
    public static byte[] ReadBc7(byte[] file, int width, int height, int mipCount)
    {
        if (file.Length < HeaderBytes || BitConverter.ToUInt32(file, 0) != 0x20534444u || BitConverter.ToUInt32(file, 84) != 0x30315844u)
            throw new InvalidDataException("not a DDS file with a DX10 header");
        var (h, w, mips, format) = (BitConverter.ToInt32(file, 12), BitConverter.ToInt32(file, 16), Math.Max(1, BitConverter.ToInt32(file, 28)), BitConverter.ToInt32(file, 128));
        if (format != Bc7) throw new InvalidDataException($"format {format}, expected BC7 ({Bc7})");
        if (w != width || h != height || mips != mipCount) throw new InvalidDataException($"{w}x{h} with {mips} mips, expected {width}x{height} with {mipCount}");
        var length = UnityTextureFormats.DataLength(UnityTextureFormats.Bc7, width, height, mipCount);
        if (file.Length - HeaderBytes != length) throw new InvalidDataException($"{file.Length - HeaderBytes} bytes of data, expected {length}");
        return file[HeaderBytes..];
    }
}
