namespace Tyrant.Framework.Core
{
    /// <summary>The small per-animal change to an iridescence strength, so a herd does not shimmer identically.</summary>
    public static class MaterialStrength
    {
        /// <summary>A stable number in [-1, 1) for an animal (a splitmix64 hash of its id).</summary>
        public static float Spread(ulong id)
        {
            unchecked
            {
                var z = id + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (z >> 40) / 16777216f * 2 - 1;
            }
        }

        /// <summary>The strength for one animal: the set strength varied by up to ±<paramref name="variation"/> of itself, kept in 0..1.</summary>
        public static float For(float strength, float variation, ulong? id) =>
            id is ulong animal ? Clamp01(strength * (1 + variation * Spread(animal))) : Clamp01(strength);

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
