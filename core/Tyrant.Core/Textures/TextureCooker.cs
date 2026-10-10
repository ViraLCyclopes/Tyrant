using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using StbImageSharp;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Textures;

/// <summary>One texture ready for Unity: compressed mips, largest first, bottom row first (LoadRawTextureData's layout).</summary>
public sealed record CookedTexture(int Format, bool Linear, int Width, int Height, int MipCount, byte[] Data);

/// <summary>Compresses a mod's PNG the way the game's own textures are (spec: docs/superpowers/specs/2026-10-10-tyrant-texture-cooking-design.md).</summary>
public static class TextureCooker
{
    /// <summary>Raise when the output for the same PNG changes, so cached textures are cooked again.</summary>
    public const int Version = 1;

    public static int FormatFor(SlotKind kind, TextureCompression mode, bool opaque) =>
        mode == TextureCompression.Bc7 ? UnityTextureFormats.Bc7
        : kind != SlotKind.Normal && opaque ? UnityTextureFormats.Dxt1
        : UnityTextureFormats.Dxt5;

    /// <summary>The size a texture is cooked at: each side up to a multiple of 4.</summary>
    public static (int Width, int Height) CookedSize(int width, int height) => ((width + 3) / 4 * 4, (height + 3) / 4 * 4);

    public static CookedTexture CookPng(string path, SlotKind kind, TextureCompression mode)
    {
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return Cook(image.Data, image.Width, image.Height, kind, mode);
    }

    /// <param name="rgba">Top row first, 4 bytes per pixel; not changed.</param>
    public static CookedTexture Cook(byte[] rgba, int width, int height, SlotKind kind, TextureCompression mode)
    {
        var pixels = (byte[])rgba.Clone();
        var filter = kind == SlotKind.Color ? MipFilter.Srgb : MipFilter.Plain;
        if (kind == SlotKind.Normal)
        {
            var shape = NormalMaps.Classify(pixels);
            if (shape == NormalMapKind.Standard) NormalMaps.PackForUnity(pixels);
            if (shape != NormalMapKind.Unknown) filter = MipFilter.PackedNormal; // an unknown map is used as-is (Check warns)
        }
        var format = FormatFor(kind, mode, Pixels.IsOpaque(pixels));
        (pixels, width, height) = MipChain.ScaleToMultipleOf4(pixels, width, height);
        MipChain.FlipRows(pixels, width, height);
        var levels = MipChain.Build(pixels, width, height, filter);

        var encoder = new BcEncoder();
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.OutputOptions.Format = format == UnityTextureFormats.Dxt1 ? CompressionFormat.Bc1
            : format == UnityTextureFormats.Dxt5 ? CompressionFormat.Bc3 : CompressionFormat.Bc7;
        encoder.Options.IsParallel = true;
        var data = new byte[UnityTextureFormats.DataLength(format, width, height, levels.Count)];
        var at = 0;
        foreach (var level in levels)
        {
            var block = encoder.EncodeToRawBytes(level.Rgba.AsSpan(), level.Width, level.Height, PixelFormat.Rgba32)[0];
            block.CopyTo(data, at);
            at += block.Length;
        }
        if (at != data.Length) throw new InvalidOperationException($"Cooked {at} bytes, expected {data.Length}.");
        return new CookedTexture(format, kind != SlotKind.Color, width, height, levels.Count, data);
    }
}
