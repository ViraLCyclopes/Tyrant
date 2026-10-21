using System;
using System.Globalization;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The final scale an animal may have. The game caps it at 0.05 to 2 (AnimalNative.CurrentScale); mod.json
    /// "sizeRanges.limit" widens that, within 0.01 to 0.05 for the minimum and 2 to 20 for the maximum.
    /// </summary>
    public readonly struct SizeLimit : IEquatable<SizeLimit>
    {
        /// <summary>The absolute bounds of a limit: its minimum in LowestMin to HighestMin, its maximum in LowestMax to HighestMax.</summary>
        public const double LowestMin = 0.01, HighestMin = 0.05, LowestMax = 2, HighestMax = 20;

        public SizeLimit(float min, float max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>The game's own limit, 0.05 to 2.</summary>
        public static SizeLimit Game => new SizeLimit(SizeMath.FinalMin, SizeMath.FinalMax);

        /// <summary>A mod's [min, max] ("sizeRanges.limit"); null is the game's.</summary>
        public static SizeLimit Of(double[]? limit) => limit == null ? Game : new SizeLimit((float)limit[0], (float)limit[1]);

        public float Min { get; }
        public float Max { get; }

        /// <summary>The limits back-off keeps to: <see cref="SizeMath.SafeMargin"/> inside, so float rounding in the game never passes them.</summary>
        public float SafeMin => Min * (1 + SizeMath.SafeMargin);
        public float SafeMax => Max * (1 - SizeMath.SafeMargin);

        public bool IsGame => Min == SizeMath.FinalMin && Max == SizeMath.FinalMax;

        public bool Contains(float min, float max) => min >= Min && max <= Max;

        /// <summary>The widest of two [min, max] limits (the lowest minimum, the highest maximum); a null one does not count.</summary>
        public static double[]? Widest(double[]? a, double[]? b) =>
            a == null ? b : b == null ? a : new[] { Math.Min(a[0], b[0]), Math.Max(a[1], b[1]) };

        public bool Equals(SizeLimit other) => Min == other.Min && Max == other.Max;
        public override bool Equals(object? obj) => obj is SizeLimit other && Equals(other);
        public override int GetHashCode() => Min.GetHashCode() * 31 + Max.GetHashCode();
        /// <summary>"0.05 to 2", as messages give it.</summary>
        public override string ToString() =>
            Min.ToString("0.###", CultureInfo.InvariantCulture) + " to " + Max.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
