using System;
using System.Globalization;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The final scale an animal may have. The game caps it at 0.05 to 2 (AnimalNative.CurrentScale). The limit is automatic: a
    /// species whose sizes need more gets the limit they need (<see cref="Fitting"/>), within Tyrant's own bounds of 0.01 to 20.
    /// The framework, Check and Blender all derive it the same way.
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

        public float Min { get; }
        public float Max { get; }

        /// <summary>The limits back-off keeps to: <see cref="SizeMath.SafeMargin"/> inside, so float rounding in the game never passes them.</summary>
        public float SafeMin => Min * (1 + SizeMath.SafeMargin);
        public float SafeMax => Max * (1 - SizeMath.SafeMargin);

        public bool IsGame => Min == SizeMath.FinalMin && Max == SizeMath.FinalMax;

        public bool Contains(float min, float max) => min >= Min && max <= Max;

        /// <summary>Whether a species' smallest and largest pass Tyrant's own bounds (under 0.01 or over 20): Check's error; the game backs it off.</summary>
        public static bool PastBounds(float min, float max) => min < (float)LowestMin || max > (float)HighestMax; // as floats: 0.01f itself is within

        /// <summary>
        /// The outermost limit, just outside Tyrant's bounds: 20 / (1 − <see cref="SizeMath.SafeMargin"/>) and 0.01 / (1 + margin),
        /// nudged in float until its safe band holds 20 and 0.01 themselves. So the game never backs off a species within 0.01 to
        /// 20, the band where Check reports no error.
        /// </summary>
        public static readonly float OuterMax = Outer(HighestMax, up: true), OuterMin = Outer(LowestMin, up: false);

        private static float Outer(double bound, bool up)
        {
            var value = (float)(up ? bound / (1 - (double)SizeMath.SafeMargin) : bound / (1 + (double)SizeMath.SafeMargin));
            while (up ? new SizeLimit(SizeMath.FinalMin, value).SafeMax < (float)bound : new SizeLimit(value, SizeMath.FinalMax).SafeMin > (float)bound)
                value = up ? value * (1 + 1e-7f) : value * (1 - 1e-7f);
            return value;
        }

        /// <summary>
        /// The size limit a species gets for its smallest and largest final scale (<see cref="SizeMath.Extremes"/>): never narrower
        /// than the game's 0.05 to 2; past it, the maximum is max / (1 − <see cref="SizeMath.SafeMargin"/>) rounded up to the next
        /// 0.5 and the minimum min / (1 + margin) rounded down to the next 0.01, so the extremes stay inside the band back-off keeps
        /// to (<see cref="SafeMin"/>, <see cref="SafeMax"/>). Past the last step inside Tyrant's 20 (or 0.01) it is the outermost
        /// limit (<see cref="OuterMax"/>, <see cref="OuterMin"/>), which still holds 20 (or 0.01).
        /// </summary>
        public static SizeLimit Fitting(float min, float max)
        {
            double hi = LowestMax, lo = HighestMin;
            if (max > Game.SafeMax)
            {
                hi = Math.Max(LowestMax, Math.Ceiling(max / (1 - (double)SizeMath.SafeMargin) * 2) / 2);
                // The game's float maths decides: a step whose safe band still misses the largest gets the next one.
                while (hi <= HighestMax && new SizeLimit((float)lo, (float)hi).SafeMax < max) hi += 0.5;
                if (hi > HighestMax) hi = OuterMax;
            }
            if (min < Game.SafeMin)
            {
                lo = Math.Min(HighestMin, Math.Floor(min / (1 + (double)SizeMath.SafeMargin) * 100) / 100);
                while (lo >= LowestMin && new SizeLimit((float)lo, (float)hi).SafeMin > min) lo = Math.Round(lo - 0.01, 2);
                if (lo < LowestMin) lo = OuterMin;
            }
            return new SizeLimit((float)lo, (float)hi);
        }

        public bool Equals(SizeLimit other) => Min == other.Min && Max == other.Max;
        public override bool Equals(object? obj) => obj is SizeLimit other && Equals(other);
        public override int GetHashCode() => Min.GetHashCode() * 31 + Max.GetHashCode();
        /// <summary>"0.05 to 2", as messages give it.</summary>
        public override string ToString() =>
            Min.ToString("0.###", CultureInfo.InvariantCulture) + " to " + Max.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
