using System;
using System.Collections.Generic;
using System.IO;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>Builds each replacement texture once per file and slot kind, with mipmaps and compression like the game's own.</summary>
    internal static class TextureCache
    {
        private static readonly Dictionary<string, Texture2D?> Cache = new Dictionary<string, Texture2D?>(StringComparer.OrdinalIgnoreCase);

        public static Texture2D? Get(Replacement replacement, SlotKind kind)
        {
            var key = replacement.FilePath + "|" + kind;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            Texture2D? texture = null;
            try
            {
                texture = Build(replacement, kind);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Error($"{replacement.ModId}: {Path.GetFileName(replacement.FilePath)} could not be loaded ({ex.Message}); {replacement.Texture} stays vanilla.");
            }
            Cache[key] = texture; // a failure is remembered too, so it is reported once
            return texture;
        }

        private static Texture2D? Build(Replacement replacement, SlotKind kind)
        {
            var file = Path.GetFileName(replacement.FilePath);
            if (!File.Exists(replacement.FilePath))
            {
                FrameworkMod.Log.Error($"{replacement.ModId}: file {file} is missing — reinstall the mod from Tyrant; {replacement.Texture} stays vanilla.");
                return null;
            }

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Color32[] pixels;
            int width, height;
            try
            {
                if (!decoded.LoadImage(File.ReadAllBytes(replacement.FilePath), false))
                {
                    FrameworkMod.Log.Error($"{replacement.ModId}: {file} is not a readable PNG; {replacement.Texture} stays vanilla.");
                    return null;
                }
                pixels = decoded.GetPixels32();
                width = decoded.width;
                height = decoded.height;
            }
            finally
            {
                UnityEngine.Object.Destroy(decoded);
            }

            if (kind == SlotKind.Normal)
            {
                var rgba = ToBytes(pixels);
                var shape = NormalMaps.Classify(rgba);
                if (shape == NormalMapKind.Standard)
                {
                    NormalMaps.PackForUnity(rgba);
                    pixels = ToColors(rgba);
                }
                else if (shape == NormalMapKind.Unknown)
                {
                    FrameworkMod.Log.Warning($"{replacement.ModId}: {file} does not look like a normal map; it is used as-is.");
                }
            }

            // Colour is sRGB; normal maps and masks are data (linear), as the game's own textures are.
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, kind != SlotKind.Color) { name = replacement.Texture };
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            if (width % 4 == 0 && height % 4 == 0) texture.Compress(true); // DXT, like vanilla; needs sizes divisible by 4
            else FrameworkMod.Log.Warning($"{replacement.ModId}: {file} is {width}x{height}; sizes divisible by 4 use less memory.");
            texture.Apply(false, true); // no longer readable: frees the CPU copy
            return texture;
        }

        private static byte[] ToBytes(Color32[] pixels)
        {
            var bytes = new byte[pixels.Length * 4];
            for (var k = 0; k < pixels.Length; k++)
            {
                bytes[k * 4] = pixels[k].r;
                bytes[k * 4 + 1] = pixels[k].g;
                bytes[k * 4 + 2] = pixels[k].b;
                bytes[k * 4 + 3] = pixels[k].a;
            }
            return bytes;
        }

        private static Color32[] ToColors(byte[] bytes)
        {
            var pixels = new Color32[bytes.Length / 4];
            for (var k = 0; k < pixels.Length; k++) pixels[k] = new Color32(bytes[k * 4], bytes[k * 4 + 1], bytes[k * 4 + 2], bytes[k * 4 + 3]);
            return pixels;
        }
    }
}
