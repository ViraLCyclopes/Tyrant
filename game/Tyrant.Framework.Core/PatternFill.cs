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
        /// The game saves an animal's colours; a normal animal it made itself has none (alpha 0). Colours set before are kept,
        /// so a reload does not re-roll them.
        /// </summary>
        public static bool NeedsColours(float storedAlphaA) => storedAlphaA <= 0f;
    }
}
