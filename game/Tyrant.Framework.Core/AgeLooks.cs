namespace Tyrant.Framework.Core
{
    /// <summary>
    /// How a look changes as an animal grows up, as the game's AnimalGrowthManager.UpdateGrowth works it out from the
    /// integument (integumentGrowthCurve at the animal's maturity: 0 a hatchling, 1 grown): the iridescence strength and the fur
    /// amount (_FresnelIntensity) are each a lerp from a baby's value to an adult's. The framework writes these; the in-game
    /// editor and its tests read the same rules.
    /// </summary>
    public static class AgeLooks
    {
        /// <summary>The strength shown at this integument: from the baby's to the adult's (the game's own lerps from 0).</summary>
        public static float Strength(float baby, float adult, float integument) => Lerp(baby, adult, integument);

        /// <summary>
        /// True when Tyrant turns the shader's iridescence on (ExtraFeatures): the strength of this variant, or the baby strength,
        /// is above 0. So a baby shimmers on a skin the game gives none.
        /// </summary>
        /// <param name="skinType">The game's AnimalSkinType: 0 base, 1 albino, 2 melanistic, 3 leucistic.</param>
        public static bool Shimmers(IridescenceSettings? i, int skinType) =>
            i != null && ((i.Strength != null && i.Strength.For(skinType) > 0) || i.BabyStrength > 0);

        /// <summary>True when Tyrant writes the strength over the game's: a layer sets the adult or the baby strength.</summary>
        public static bool WritesStrength(IridescenceSettings? i) => i != null && (i.Strength != null || i.BabyStrength != null);

        /// <summary>
        /// The ends (hatchling, grown) of the fuzz lerp Tyrant writes: the age's (babies 1 to 0, adults 0 to 1, both 1 to 1), else
        /// 1 to 1 while grow is on; null leaves the game's own (<see cref="GameFuzzEnds"/>).
        /// </summary>
        public static (float Baby, float Adult)? FuzzEnds(FurAge? age, bool grow) => age switch
        {
            FurAge.Babies => (1f, 0f),
            FurAge.Adults => (0f, 1f),
            FurAge.Both => (1f, 1f),
            _ => grow ? (1f, 1f) : ((float, float)?)null,
        };

        /// <summary>The game's own ends: 1 for each age whose fur map is valid (FuzzTexturesValid), else 0.</summary>
        public static (float Baby, float Adult) GameFuzzEnds(bool babyMap, bool adultMap) => (babyMap ? 1f : 0f, adultMap ? 1f : 0f);

        /// <summary>The fur amount at this integument, between the ends.</summary>
        public static float Fuzz((float Baby, float Adult) ends, float integument) => Lerp(ends.Baby, ends.Adult, integument);

        /// <summary>
        /// The ages Tyrant gives a white fur mask when they have none of their own (the game's or the block's): those the age
        /// names, else every age while grow is on.
        /// </summary>
        public static (bool Baby, bool Adult) GrownStages(FurAge? age, bool grow) => age switch
        {
            FurAge.Babies => (true, false),
            FurAge.Adults => (false, true),
            FurAge.Both => (true, true),
            _ => (grow, grow),
        };

        /// <summary>True when the animal needs the shader's fur variant (as a skin with a fur map gets): grow is on or an age is set.</summary>
        public static bool NeedsFurVariant(FurMasks? fur) => fur != null && (fur.Grow == true || fur.Age != null);

        private static float Lerp(float from, float to, float t)
        {
            var at = t < 0 ? 0 : t > 1 ? 1 : t;
            return from + (to - from) * at;
        }
    }
}
