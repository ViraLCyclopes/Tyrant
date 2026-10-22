using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// Whether a skin's fur mask gives fur anywhere. The fur shader reads only red as length, so a mask with no red anywhere
    /// is as bare as the game's FurMaskEmpty, whatever its name. Starting skins copied such masks from furless templates, and
    /// Grow fur would otherwise leave them in place as the skin's own fur.
    /// </summary>
    public static class FurMaskContent
    {
        /// <summary>Unity's uncompressed TextureFormats a PNG can load as (LoadImage).</summary>
        public const int Rgb24 = 3, Rgba32 = 4, Argb32 = 5;

        /// <summary>A skin's fur slot texture, as Tyrant names it ("&lt;mod&gt;/&lt;skin&gt;:fur" or ":infantFur").</summary>
        public static bool IsSkinFurSlot(string textureName) =>
            textureName.EndsWith(":fur", StringComparison.Ordinal) || textureName.EndsWith(":infantFur", StringComparison.Ordinal);

        /// <summary>
        /// True when the largest level of <paramref name="data"/> has no red anywhere. DXT1 and DXT5 are judged by each block's
        /// two end colours (every colour in a block lies between them); RGB24, RGBA32 and ARGB32 pixel by pixel. Other formats,
        /// and data shorter than the level, count as fur: Tyrant then keeps the mask, as before.
        /// </summary>
        public static bool HasNoFur(int format, int width, int height, IList<byte> data) => HasNoFur(format, width, height, data.Count, i => data[i]);

        /// <summary><see cref="HasNoFur(int, int, int, IList{byte})"/> over <paramref name="length"/> bytes read in place (a texture's own buffer).</summary>
        public static bool HasNoFur(int format, int width, int height, long length, Func<int, byte> at)
        {
            if (format == Rgb24 || format == Rgba32 || format == Argb32)
            {
                var stride = format == Rgb24 ? 3 : 4;
                var red = format == Argb32 ? 1 : 0;
                var pixels = (long)width * height;
                if (length < pixels * stride) return false;
                for (var i = 0L; i < pixels; i++)
                    if (at((int)(i * stride) + red) != 0) return false;
                return true;
            }
            if (format != UnityTextureFormats.Dxt1 && format != UnityTextureFormats.Dxt5) return false;
            var blockBytes = UnityTextureFormats.BlockBytes(format);
            var colourAt = format == UnityTextureFormats.Dxt5 ? 8 : 0; // DXT5: 8 bytes of alpha, then a DXT1 colour block
            var blocks = (long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4);
            if (length < blocks * blockBytes) return false;
            for (var b = 0L; b < blocks; b++)
            {
                var block = (int)(b * blockBytes) + colourAt;
                // RGB565, little-endian: red is the top 5 bits of each end colour.
                if ((at(block + 1) & 0xF8) != 0 || (at(block + 3) & 0xF8) != 0) return false;
            }
            return true;
        }
    }
}
