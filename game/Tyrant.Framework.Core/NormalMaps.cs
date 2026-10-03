using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    public enum NormalMapKind
    {
        /// <summary>Tangent-space map as artists paint it: R = X, G = Y, B = Z (blue-purple).</summary>
        Standard,

        /// <summary>Unity's packed form (DXT5nm): X in alpha, Y in green, red filled; looks red.</summary>
        Packed,

        Unknown,
    }

    public static class NormalMaps
    {
        /// <summary>Judges up to ~4096 sampled RGBA pixels; the packed rule matches Tyrant's exporter, so exports round-trip.</summary>
        public static NormalMapKind Classify(byte[] rgba)
        {
            var pixels = rgba.Length / 4;
            if (pixels == 0) return NormalMapKind.Unknown;
            var step = Math.Max(1, pixels / 4096);
            double r = 0, g = 0, b = 0, a = 0, aa = 0, gb = 0;
            var n = 0;
            for (var p = 0; p < pixels; p += step)
            {
                var i = p * 4;
                r += rgba[i];
                g += rgba[i + 1];
                b += rgba[i + 2];
                a += rgba[i + 3];
                aa += rgba[i + 3] * (double)rgba[i + 3];
                gb += Math.Abs(rgba[i + 1] - rgba[i + 2]);
                n++;
            }
            r /= n; g /= n; b /= n; a /= n; gb /= n;
            var aStd = Math.Sqrt(Math.Max(0, aa / n - a * a));
            if (r >= 240 && gb <= 12 && a >= 64 && a <= 192 && aStd >= 8) return NormalMapKind.Packed;
            if (b >= 180 && Math.Abs(r - 128) <= 48 && Math.Abs(g - 128) <= 48) return NormalMapKind.Standard;
            return NormalMapKind.Unknown;
        }

        /// <summary>Standard → Unity packed, in place: A = X (old R), G = B = Y, R = 255.</summary>
        public static void PackForUnity(byte[] rgba)
        {
            for (var i = 0; i + 3 < rgba.Length; i += 4)
            {
                var x = rgba[i];
                var y = rgba[i + 1];
                rgba[i] = 255;
                rgba[i + 1] = y;
                rgba[i + 2] = y;
                rgba[i + 3] = x;
            }
        }
    }

    public enum SlotKind
    {
        /// <summary>Colour (sRGB): diffuse.</summary>
        Color,

        /// <summary>Packed normal map (linear).</summary>
        Normal,

        /// <summary>Masks and extra maps (linear).</summary>
        Data,
    }

    public sealed class TextureSlot
    {
        public TextureSlot(string property, SlotKind kind)
        {
            Property = property;
            Kind = kind;
        }

        /// <summary>The animal shader property the game sets, e.g. "_AdultDiffuse".</summary>
        public string Property { get; }
        public SlotKind Kind { get; }
    }

    public static class TextureSlots
    {
        /// <summary>Every texture AnimalVisuals.AssignTexturesAndSetMaterialVariation sets on an animal.</summary>
        public static readonly IReadOnlyList<TextureSlot> All = new[]
        {
            new TextureSlot("_AdultDiffuse", SlotKind.Color),
            new TextureSlot("_AdultNormal", SlotKind.Normal),
            new TextureSlot("_AdultExtraMap", SlotKind.Data),
            new TextureSlot("_AdultPatternMask", SlotKind.Data),
            new TextureSlot("_AdultFurMask", SlotKind.Data),
            new TextureSlot("_InfantDiffuse", SlotKind.Color),
            new TextureSlot("_InfantNormal", SlotKind.Normal),
            new TextureSlot("_InfantExtraMap", SlotKind.Data),
            new TextureSlot("_InfantPatternMask", SlotKind.Data),
            new TextureSlot("_InfantFurMask", SlotKind.Data),
        };
    }
}
