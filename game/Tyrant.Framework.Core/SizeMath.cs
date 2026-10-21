using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The maths behind animal sizes. The game's final size is S(m, s, g) = curve(m) · (1 + (s·g − 1)·f(m)), with m the maturity,
    /// s the sex size and g the genetic size. Tyrant moves the adult multiplier into s and compensates the curve so that, at s·g = adult,
    /// S equals adult × shape(m). Curves are key lists so that no Unity type is needed.
    /// </summary>
    public static class SizeMath
    {
        /// <summary>The game's own limits on the final scale (AnimalNative.CurrentScale); <see cref="SizeLimit"/> is the one in effect.</summary>
        public const float FinalMin = 0.05f, FinalMax = 2f;

        /// <summary>The margin BackOff keeps inside the limit in effect, so float rounding in the game never pushes the scale over it. The plain limit stays the error threshold.</summary>
        public const float SafeMargin = 0.001f;
        private const int Steps = 16, Samples = 101;

        /// <summary>How many times over 0–1 <see cref="Sample"/> reads the game's curve; with straight lines between them, the shape stays within 1%.</summary>
        public const int CurveSamples = 33;

        /// <summary>The game's maturity fade f(m) = 0.2 + 0.8·(1 − (1 − m)²), clamped to 0–1 in m.</summary>
        public static float Fade(float maturity)
        {
            var m = Math.Min(1f, Math.Max(0f, maturity));
            var inv = 1f - m;
            return 0.2f + 0.8f * (1f - inv * inv);
        }

        /// <summary>
        /// The game's curve as keys for the maths here: <paramref name="evaluate"/> (the game's own reading, Hermite with the keys'
        /// tangents) at 33 times over 0–1 and at each key's time. Its keys alone would lose the shape: read as straight lines, a
        /// juvenile can be 25% off. No keys, no curve.
        /// </summary>
        public static (float Time, float Value)[] Sample(IEnumerable<float> keyTimes, Func<float, float> evaluate)
        {
            var times = new SortedSet<float>(keyTimes);
            if (times.Count == 0) return new (float, float)[0];
            for (var i = 0; i < CurveSamples; i++) times.Add(i / (float)(CurveSamples - 1));
            return times.Select(t => (t, evaluate(t))).ToArray();
        }

        /// <summary>The curve at t, linear in between its keys (a game curve goes through <see cref="Sample"/> first; the rebuilt tangents make the game follow these lines).</summary>
        public static float Evaluate((float Time, float Value)[] keys, float t)
        {
            if (keys.Length == 0) return 0f;
            if (t <= keys[0].Time) return keys[0].Value;
            for (var i = 1; i < keys.Length; i++)
            {
                if (t > keys[i].Time) continue;
                var (t0, v0) = keys[i - 1];
                var (t1, v1) = keys[i];
                return t1 <= t0 ? v1 : v0 + (v1 - v0) * (t - t0) / (t1 - t0);
            }
            return keys[keys.Length - 1].Value;
        }

        /// <summary>
        /// The growth curve for a new adult multiplier and hatchling share: 17 keys over 0–1 of adult · shape(m) / (1 + (adult − 1)·f(m)),
        /// plus every original key past 1, mapped by shape. Shape is an affine map of the original, so C(0) goes to baby · C(1) and C(1) stays.
        /// </summary>
        public static (float Time, float Value)[] Rebuild((float Time, float Value)[] original, double adult, double? baby)
        {
            var c0 = Evaluate(original, 0f);
            var c1 = Evaluate(original, 1f);
            var share0 = c1 == 0f ? 1.0 : c0 / (double)c1;
            double a = 0, b = 1;
            if (1 - share0 >= 1e-4)
            {
                var target = baby ?? share0;
                b = c1 * (1 - target) / (c1 - c0);
                a = c1 - b * c1;
            }
            double Shape(float v) => a + b * v;

            var keys = new List<(float, float)>();
            for (var i = 0; i <= Steps; i++)
            {
                var m = i / (float)Steps;
                keys.Add((m, (float)(adult * Shape(Evaluate(original, m)) / (1 + (adult - 1) * Fade(m)))));
            }
            foreach (var key in original.Where(k => k.Time > 1f).OrderBy(k => k.Time))
                keys.Add((key.Time, (float)Shape(key.Value)));
            return keys.ToArray();
        }

        /// <summary>A skin's sex sizes with the adult multiplier folded in once; unset values are 1.</summary>
        public static (double Male, double Female) Sex(double male0, double female0, SizeSettings s)
        {
            var adult = s.Adult ?? 1;
            return (male0 * adult * (s.Male ?? 1), female0 * adult * (s.Female ?? 1));
        }

        /// <summary>The smallest and largest final size over maturity (101 samples), the sex sizes and the genetic ranges' ends.</summary>
        public static (float Min, float Max) Extremes((float, float)[] curve, IEnumerable<double> sexMultipliers, IEnumerable<(double Min, double Max)> geneticRanges)
        {
            var sexes = sexMultipliers.ToList();
            var genetics = geneticRanges.SelectMany(r => new[] { r.Min, r.Max }).ToList();
            var min = float.MaxValue;
            var max = float.MinValue;
            for (var i = 0; i < Samples; i++)
            {
                var m = i / (float)(Samples - 1);
                var curveAt = Evaluate(curve, m);
                var fade = Fade(m);
                foreach (var s in sexes)
                    foreach (var g in genetics)
                    {
                        var size = (float)(curveAt * (1 + (s * g - 1) * fade));
                        if (size < min) min = size;
                        if (size > max) max = size;
                    }
            }
            return (min, max);
        }

        /// <summary>
        /// The adult multiplier closest to the requested one, by bisection towards 1, that keeps every extreme within the limit
        /// (the game's 0.05–2 by default) with <see cref="SafeMargin"/> to spare. When even 1 is outside, it returns 1 and the caller warns.
        /// </summary>
        public static double BackOff((float Time, float Value)[] original, double requested, double? baby,
            IEnumerable<(double Male0, double Female0)> skins, double? male, double? female, IEnumerable<(double Min, double Max)> geneticRanges,
            SizeLimit? limit = null)
        {
            var within = limit ?? SizeLimit.Game;
            var skinList = skins.ToList();
            var ranges = geneticRanges.ToList();
            bool Fits(double adult)
            {
                var curve = Rebuild(original, adult, baby);
                var settings = new SizeSettings { Adult = adult, Male = male, Female = female };
                var sexes = skinList.SelectMany(s => { var (m, f) = Sex(s.Male0, s.Female0, settings); return new[] { m, f }; });
                var (min, max) = Extremes(curve, sexes, ranges);
                return min >= within.SafeMin && max <= within.SafeMax;
            }

            if (Fits(requested)) return requested;
            if (!Fits(1)) return 1;
            double good = 1, bad = requested;
            for (var i = 0; i < 40; i++)
            {
                var mid = (good + bad) / 2;
                if (Fits(mid)) good = mid; else bad = mid;
            }
            return good;
        }
    }
}
