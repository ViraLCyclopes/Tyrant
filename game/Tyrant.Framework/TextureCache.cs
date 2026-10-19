using System;
using System.Collections.Generic;
using System.IO;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>Builds each mod texture once per file and slot kind: from the mod's cooked pack when it can, else from the PNG.</summary>
    internal static class TextureCache
    {
        private static readonly Dictionary<string, Texture2D?> Cache = new Dictionary<string, Texture2D?>(StringComparer.OrdinalIgnoreCase);

        public static Texture2D? Get(Replacement replacement, SlotKind kind)
        {
            var key = replacement.FilePath + "|" + kind;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var texture = Load(replacement, kind, looseFirst: false);
            Cache[key] = texture; // a failure is remembered too, so it is reported once
            return texture;
        }

        /// <summary>
        /// Builds the texture without caching it: the caller owns (and destroys) it. With <paramref name="looseFirst"/> a PNG on
        /// disk wins over the cooked pack entry: an image saved in game is newer than the mod's cooked copy.
        /// </summary>
        public static Texture2D? Load(Replacement replacement, SlotKind kind, bool looseFirst)
        {
            try
            {
                return Build(replacement, kind, looseFirst);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Error($"{replacement.ModId}: {Path.GetFileName(replacement.FilePath)} could not be loaded ({ex.Message}); {replacement.Texture} stays vanilla.");
                return null;
            }
        }

        private static Texture2D? Build(Replacement replacement, SlotKind kind, bool looseFirst)
        {
            var file = Path.GetFileName(replacement.FilePath);
            if (!File.Exists(replacement.FilePath))
            {
                // A slim install: Tyrant left the PNG out because the pack holds it.
                var packed = TexturePacks.LoadWithoutSource(replacement, kind, out var reason);
                if (packed != null) return packed;
                FrameworkMod.Log.Error($"{replacement.ModId}: file {file} is missing{reason} — reinstall the mod from Tyrant; {replacement.Texture} stays vanilla.");
                return null;
            }

            var cooked = looseFirst ? null : TexturePacks.Load(replacement, kind);
            if (cooked != null) return cooked;

            // Not cooked: decode the PNG straight into a texture and work on Unity's own buffer (no managed pixel copies).
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, kind != SlotKind.Color) { name = replacement.Texture };
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(replacement.FilePath), false))
                {
                    UnityEngine.Object.Destroy(texture);
                    FrameworkMod.Log.Error($"{replacement.ModId}: {file} is not a readable PNG; {replacement.Texture} stays vanilla.");
                    return null;
                }
                if (kind == SlotKind.Normal) PackNormals(texture, replacement, file);
                if (texture.width % 4 == 0 && texture.height % 4 == 0) texture.Compress(true); // DXT5 (RGBA source)
                else FrameworkMod.Log.Warning($"{replacement.ModId}: {file} is {texture.width}x{texture.height}; sizes divisible by 4 use less memory.");
                texture.Apply(false, true); // no longer readable: frees the CPU copy
                return texture;
            }
            catch
            {
                UnityEngine.Object.Destroy(texture);
                throw;
            }
        }

        /// <summary>A standard normal map -> Unity's packed form, in the texture's own buffer (level 0), then mips rebuilt.</summary>
        private static void PackNormals(Texture2D texture, Replacement replacement, string file)
        {
            int r, g, b, a;
            if (texture.format == TextureFormat.RGBA32) (r, g, b, a) = (0, 1, 2, 3);
            else if (texture.format == TextureFormat.ARGB32) (a, r, g, b) = (0, 1, 2, 3);
            else
            {
                FrameworkMod.Log.Warning($"{replacement.ModId}: {file} loaded as {texture.format}; it is used as-is.");
                return;
            }
            var data = texture.GetRawTextureData<byte>();
            var pixels = texture.width * texture.height;
            var step = Math.Max(1, pixels / 4096);
            var sample = new byte[(pixels + step - 1) / step * 4];
            for (int p = 0, s = 0; p < pixels && s < sample.Length; p += step, s += 4)
            {
                var i = p * 4;
                (sample[s], sample[s + 1], sample[s + 2], sample[s + 3]) = (data[i + r], data[i + g], data[i + b], data[i + a]);
            }
            var shape = NormalMaps.Classify(sample);
            if (shape == NormalMapKind.Unknown)
            {
                FrameworkMod.Log.Warning($"{replacement.ModId}: {file} does not look like a normal map; it is used as-is.");
                return;
            }
            if (shape != NormalMapKind.Standard) return;
            for (var p = 0; p < pixels; p++)
            {
                var i = p * 4;
                var x = data[i + r];
                var y = data[i + g];
                data[i + r] = 255;
                data[i + b] = y;
                data[i + a] = x;
            }
            texture.Apply(true, false); // rebuild the mips from the packed level 0
        }
    }
}
