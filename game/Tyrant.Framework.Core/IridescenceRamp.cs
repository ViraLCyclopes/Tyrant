using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    /// <summary>The iridescence ramp texture: colour by N·V, as the shader samples it.</summary>
    public static class IridescenceRamp
    {
        /// <summary>The colours of an inline or thin-film ramp; null for a file ramp (its pixels are read from the file).</summary>
        public static IReadOnlyList<Rgb>? Stops(RampSource ramp)
        {
            if (ramp.Stops != null) return ramp.Stops;
            if (ramp.ThinFilm != null) return ThinFilm.Ramp(ramp.ThinFilm.Thickness, ramp.ThinFilm.Index);
            return null;
        }

        /// <summary>A size × size RGBA image: x runs N·V 0 to 1, every row the same.</summary>
        public static byte[] Pixels(IReadOnlyList<Rgb> stops, int size = 32)
        {
            var row = new byte[size * 4];
            for (var x = 0; x < size; x++)
            {
                var colour = ColorRamp.Sample(stops, size == 1 ? 0 : x / (double)(size - 1));
                row[x * 4] = Channel(colour.R);
                row[x * 4 + 1] = Channel(colour.G);
                row[x * 4 + 2] = Channel(colour.B);
                row[x * 4 + 3] = 255;
            }
            var pixels = new byte[size * size * 4];
            for (var y = 0; y < size; y++) Buffer.BlockCopy(row, 0, pixels, y * row.Length, row.Length);
            return pixels;
        }

        private static byte Channel(float c) => (byte)Math.Round(Math.Max(0, Math.Min(1, c)) * 255);
    }
}
