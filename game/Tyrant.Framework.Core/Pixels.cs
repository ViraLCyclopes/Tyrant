using System;

namespace Tyrant.Framework.Core
{
    public static class Pixels
    {
        /// <summary>True when every alpha is 255: the texture can be stored without alpha (DXT1, like vanilla diffuse maps).</summary>
        public static bool IsOpaque(byte[] rgba)
        {
            for (var i = 3; i < rgba.Length; i += 4)
                if (rgba[i] != 255) return false;
            return true;
        }

        public static void CenterSquare(int width, int height, out int x, out int y, out int size)
        {
            size = Math.Min(width, height);
            x = (width - size) / 2;
            y = (height - size) / 2;
        }

        /// <summary>A target×target RGBA image averaged from the centre square of an RGBA image (box filter).</summary>
        public static byte[] Thumbnail(byte[] rgba, int width, int height, int target) =>
            Thumbnail(new ArrayBytes(rgba), width, height, target, 4, 0, 1, 2, 3);

        /// <summary>
        /// <see cref="Thumbnail(byte[], int, int, int)"/> over pixels read in place, in any byte layout: <paramref name="stride"/>
        /// bytes per pixel with red, green, blue and alpha at the given offsets (alpha below 0: none, the image is opaque).
        /// </summary>
        public static byte[] Thumbnail<T>(T pixels, int width, int height, int target, int stride, int red, int green, int blue, int alpha) where T : struct, IByteSource
        {
            CenterSquare(width, height, out var x0, out var y0, out var size);
            var result = new byte[target * target * 4];
            for (var ty = 0; ty < target; ty++)
            for (var tx = 0; tx < target; tx++)
            {
                int sx0 = x0 + tx * size / target, sx1 = Math.Max(sx0 + 1, x0 + (tx + 1) * size / target);
                int sy0 = y0 + ty * size / target, sy1 = Math.Max(sy0 + 1, y0 + (ty + 1) * size / target);
                long r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var sy = sy0; sy < sy1; sy++)
                for (var sx = sx0; sx < sx1; sx++)
                {
                    var i = (sy * width + sx) * stride;
                    r += pixels.At(i + red); g += pixels.At(i + green); b += pixels.At(i + blue); a += alpha < 0 ? 255 : pixels.At(i + alpha); n++;
                }
                var o = (ty * target + tx) * 4;
                result[o] = (byte)(r / n);
                result[o + 1] = (byte)(g / n);
                result[o + 2] = (byte)(b / n);
                result[o + 3] = (byte)(a / n);
            }
            return result;
        }
    }
}
