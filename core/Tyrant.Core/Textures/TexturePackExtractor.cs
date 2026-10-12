using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using StbImageWriteSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Textures;

/// <summary>A decoded pack texture: top row first; normal maps rebuilt as standard ones.</summary>
public sealed record DecodedTexture(int Width, int Height, byte[] Rgba);

/// <summary>Sources written and sources skipped (outside the folder, or not decodable).</summary>
public sealed record ExtractResult(IReadOnlyList<string> Written, IReadOnlyList<string> Skipped);

/// <summary>
/// textures.tpack back to PNGs (import, Copy to workspace, Extract textures): level 0 of each entry, decoded, the right way
/// up, normal maps unpacked. Lossy: the PNG is the cooked texture, not the author's original.
/// </summary>
public static class TexturePackExtractor
{
    public static DecodedTexture Decode(Stream pack, TexturePackEntry entry)
    {
        var format = entry.Format switch
        {
            UnityTextureFormats.Dxt1 => CompressionFormat.Bc1,
            UnityTextureFormats.Dxt5 => CompressionFormat.Bc3,
            UnityTextureFormats.Bc7 => CompressionFormat.Bc7,
            _ => throw new InvalidDataException($"format {UnityTextureFormats.Name(entry.Format)} cannot be extracted"),
        };
        var length = (int)UnityTextureFormats.DataLength(entry.Format, entry.Width, entry.Height, 1);
        var data = new byte[length];
        pack.Position = entry.Offset;
        pack.ReadExactly(data);
        var pixels = new BcDecoder().DecodeRaw(data, entry.Width, entry.Height, format);
        var rgba = new byte[entry.Width * entry.Height * 4];
        for (var i = 0; i < pixels.Length; i++)
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (pixels[i].r, pixels[i].g, pixels[i].b, pixels[i].a);
        MipChain.FlipRows(rgba, entry.Width, entry.Height); // packs store the bottom row first
        if (entry.Kind == SlotKind.Normal && Packed(rgba)) NormalMap.Unpack(rgba);
        return new DecodedTexture(entry.Width, entry.Height, rgba);
    }

    /// <summary>
    /// A cooked normal map in Unity's packed form (the cooker packs standard maps; an unrecognised one is cooked as it was):
    /// red filled, green and blue equal. Unlike NormalMap.LooksPacked this needs no spread in alpha: a flat map is packed too.
    /// </summary>
    private static bool Packed(byte[] rgba)
    {
        var pixels = rgba.Length / 4;
        if (pixels == 0) return false;
        var step = Math.Max(1, pixels / 4096);
        double red = 0, gap = 0;
        var samples = 0;
        for (var p = 0; p < pixels; p += step)
        {
            red += rgba[p * 4];
            gap += Math.Abs(rgba[p * 4 + 1] - rgba[p * 4 + 2]);
            samples++;
        }
        return red / samples >= 240 && gap / samples <= 12;
    }

    public static ExtractResult ExtractTo(string packPath, string outDir, Func<string, bool>? want = null)
    {
        using var stream = File.OpenRead(packPath);
        TexturePack pack;
        try { pack = TexturePack.Read(stream); }
        catch (TexturePackException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{Path.GetFileName(packPath)}' is not a Tyrant texture pack (textures.tpack).");
        }
        var root = Path.GetFullPath(outDir);
        var written = new List<string>();
        var skipped = new List<string>();
        foreach (var entry in pack.Entries)
        {
            var source = TexturePack.NormalSource(entry.Source);
            if (want is not null && !want(source)) continue;
            var target = ModTextureCooker.InsidePath(root, source);
            if (target is null)
            {
                skipped.Add(source);
                continue;
            }
            DecodedTexture image;
            try { image = Decode(stream, entry); }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException)
            {
                skipped.Add(source);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var file = File.Create(target))
                new ImageWriter().WritePng(image.Rgba, image.Width, image.Height, ColorComponents.RedGreenBlueAlpha, file);
            written.Add(source);
        }
        return new ExtractResult(written, skipped);
    }
}
