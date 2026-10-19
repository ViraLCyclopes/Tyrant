using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    /// <summary>The colours of a thin transparent film (an oil slick, a soap bubble) as the viewing angle changes.</summary>
    public static class ThinFilm
    {
        private const double FirstWavelength = 380;
        private const double LastWavelength = 780;
        private const double WavelengthStep = 5;

        /// <summary>
        /// <paramref name="count"/> colours from grazing (N·V 0) to facing (N·V 1) for a film <paramref name="thicknessNm"/> thick;
        /// scaled so the brightest channel of the whole ramp is 1. A film of no thickness gives black.
        /// </summary>
        public static IReadOnlyList<Rgb> Ramp(double thicknessNm, double index = 1.56, int count = 32)
        {
            var white = Linear(1, 1, 1, null);
            var linear = new double[count][];
            var largest = 0.0;
            for (var i = 0; i < count; i++)
            {
                var c = count == 1 ? 1 : i / (double)(count - 1);
                var cosT = Math.Sqrt(Math.Max(0, 1 - (1 - c * c) / (index * index)));
                var xyz = new double[3];
                for (var wavelength = FirstWavelength; wavelength <= LastWavelength; wavelength += WavelengthStep)
                {
                    var r = 0.5 * (1 - Math.Cos(4 * Math.PI * index * thicknessNm * cosT / wavelength));
                    xyz[0] += r * X(wavelength);
                    xyz[1] += r * Y(wavelength);
                    xyz[2] += r * Z(wavelength);
                }
                var rgb = Linear(xyz[0], xyz[1], xyz[2], white);
                linear[i] = rgb;
                largest = Math.Max(largest, Math.Max(rgb[0], Math.Max(rgb[1], rgb[2])));
            }
            var ramp = new List<Rgb>(count);
            foreach (var rgb in linear)
                ramp.Add(largest < 1e-6 ? new Rgb(0, 0, 0) : new Rgb(Encode(rgb[0] / largest), Encode(rgb[1] / largest), Encode(rgb[2] / largest)));
            return ramp;
        }

        /// <summary>Linear sRGB of an XYZ colour, divided per channel by <paramref name="white"/> when given; negatives become 0.</summary>
        private static double[] Linear(double x, double y, double z, double[]? white)
        {
            var rgb = new[]
            {
                3.2406 * x - 1.5372 * y - 0.4986 * z,
                -0.9689 * x + 1.8758 * y + 0.0415 * z,
                0.0557 * x - 0.2040 * y + 1.0570 * z,
            };
            if (white == null) return rgb;
            for (var k = 0; k < 3; k++) rgb[k] = Math.Max(0, rgb[k] / white[k]);
            return rgb;
        }

        private static float Encode(double v) => (float)(v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055);

        // Wyman, Sloan and Shirley 2013: multi-lobe Gaussian fit of the CIE 1931 colour matching functions.
        private static double X(double l) => 1.056 * G(l, 599.8, 37.9, 31.0) + 0.362 * G(l, 442.0, 16.0, 26.7) - 0.065 * G(l, 501.1, 20.4, 26.2);

        private static double Y(double l) => 0.821 * G(l, 568.8, 46.9, 40.5) + 0.286 * G(l, 530.9, 16.3, 31.1);

        private static double Z(double l) => 1.217 * G(l, 437.0, 11.8, 36.0) + 0.681 * G(l, 459.0, 26.0, 13.8);

        private static double G(double l, double mu, double sigma1, double sigma2)
        {
            var t = (l - mu) / (l < mu ? sigma1 : sigma2);
            return Math.Exp(-0.5 * t * t);
        }
    }
}
