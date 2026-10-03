using System;

namespace Tyrant.Framework.Core
{
    /// <summary>The pattern colours one normal animal of a pattern-coloured skin gets.</summary>
    public sealed class PatternValues
    {
        public PatternValues(Rgb a, Rgb b, Rgb secondary, Rgb eye, float strength, float softness)
        {
            A = a;
            B = b;
            Secondary = secondary;
            Eye = eye;
            Strength = strength;
            Softness = softness;
        }

        public Rgb A { get; }
        public Rgb B { get; }
        public Rgb Secondary { get; }
        public Rgb Eye { get; }
        public float Strength { get; }
        public float Softness { get; }
    }

    /// <summary>The pattern values an animal has stored (from its save or its parents); alpha 0 means never set.</summary>
    public sealed class StoredPattern
    {
        public StoredPattern(Rgb a, float alphaA, Rgb b, float alphaB, Rgb secondary, float alphaSecondary, Rgb eye, float alphaEye, float strength, float softness)
        {
            (A, AlphaA, B, AlphaB, Secondary, AlphaSecondary, Eye, AlphaEye, Strength, Softness) = (a, alphaA, b, alphaB, secondary, alphaSecondary, eye, alphaEye, strength, softness);
        }

        public Rgb A { get; }
        public float AlphaA { get; }
        public Rgb B { get; }
        public float AlphaB { get; }
        public Rgb Secondary { get; }
        public float AlphaSecondary { get; }
        public Rgb Eye { get; }
        public float AlphaEye { get; }
        public float Strength { get; }
        public float Softness { get; }
    }

    public static class PatternFill
    {
        /// <summary>Used where the pattern map's green is set when the mod gives no secondary colour (the shader always paints that area).</summary>
        public static readonly Rgb DefaultSecondary = new Rgb(0xa0 / 255f, 0x78 / 255f, 0x68 / 255f);

        /// <summary>Used for the eyes when the mod gives no eye colour (amber).</summary>
        public static readonly Rgb DefaultEye = new Rgb(0xd0 / 255f, 0xa0 / 255f, 0x40 / 255f);

        /// <summary>The game's own default for how much a genetics colour replaces the texture.</summary>
        public static readonly FloatRange DefaultStrength = new FloatRange(0.5f, 0.75f);

        public static readonly FloatRange DefaultSoftness = new FloatRange(0.1f, 0.35f);

        public static PatternValues Create(SkinColorSet pattern, Random random)
        {
            var a = pattern.A ?? pattern.B;
            var b = pattern.B ?? pattern.A;
            return new PatternValues(
                a == null ? DefaultSecondary : ColorRamp.Sample(a, random.NextDouble()),
                b == null ? DefaultSecondary : ColorRamp.Sample(b, random.NextDouble()),
                pattern.Secondary == null ? DefaultSecondary : ColorRamp.Sample(pattern.Secondary, random.NextDouble()),
                pattern.Eye == null ? DefaultEye : ColorRamp.Sample(pattern.Eye, random.NextDouble()),
                (pattern.Strength ?? DefaultStrength).Sample(random.NextDouble()),
                (pattern.Softness ?? DefaultSoftness).Sample(random.NextDouble()));
        }

        /// <summary>
        /// On load or breeding: keeps an animal's stored pattern values where they fit the skin's palette and ranges, snaps the
        /// rest in, and draws any that were never set (alpha 0 or 0) — breeding mixes each field from either parent, so an
        /// offspring of a vanilla parent can have only some of them.
        /// </summary>
        public static PatternValues Keep(StoredPattern stored, SkinColorSet pattern, Random random)
        {
            var a = pattern.A ?? pattern.B;
            var b = pattern.B ?? pattern.A;
            return new PatternValues(
                Colour(stored.A, stored.AlphaA, a, DefaultSecondary, random),
                Colour(stored.B, stored.AlphaB, b, DefaultSecondary, random),
                Colour(stored.Secondary, stored.AlphaSecondary, pattern.Secondary, DefaultSecondary, random),
                Colour(stored.Eye, stored.AlphaEye, pattern.Eye, DefaultEye, random),
                Number(stored.Strength, pattern.Strength ?? DefaultStrength, random),
                Number(stored.Softness, pattern.Softness ?? DefaultSoftness, random));
        }

        private static Rgb Colour(Rgb stored, float alpha, System.Collections.Generic.IReadOnlyList<Rgb>? ramp, Rgb fallback, Random random)
        {
            if (ramp == null) return fallback;
            return alpha <= 0f ? ColorRamp.Sample(ramp, random.NextDouble()) : ColorRamp.Nearest(ramp, stored);
        }

        private static float Number(float stored, FloatRange range, Random random) =>
            stored <= 0f ? range.Sample(random.NextDouble()) : Math.Max(range.Min, Math.Min(range.Max, stored));
    }
}
