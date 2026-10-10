using System;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The 12 portraits every game skin has (PrehistoricKingdom.AnimalPortraits' fields, in its order), and which one the game
    /// shows (BaseAnimalData.GetPortrait: adults and adolescents use the adult pictures of their sex, infants the infant ones).
    /// </summary>
    public static class PortraitSlots
    {
        public static readonly string[] All =
        {
            "adultMale", "adultMaleAlbino", "adultMaleMelanistic", "adultMaleLeucistic",
            "adultFemale", "adultFemaleAlbino", "adultFemaleMelanistic", "adultFemaleLeucistic",
            "infant", "infantAlbino", "infantMelanistic", "infantLeucistic",
        };

        /// <summary>The game's AnimalSkinType names by value.</summary>
        public static readonly string[] Morphs = { "Base", "Albino", "Melanistic", "Leucistic" };

        /// <summary>The game's AnimalGrowthStage names by value.</summary>
        public static readonly string[] Stages = { "Infant", "Adolescent", "Adult" };

        public static bool IsSlot(string name) => Array.IndexOf(All, name) >= 0;

        /// <summary>The slot the game reads; stage, sex and morph as the game's enum values. Null for values the game does not have.</summary>
        public static string? For(int stage, bool male, int morph)
        {
            if (stage < 0 || stage >= Stages.Length || morph < 0 || morph >= Morphs.Length) return null;
            var start = stage == 0 ? "infant" : male ? "adultMale" : "adultFemale";
            return morph == 0 ? start : start + Morphs[morph];
        }

        /// <summary>A portrait's file name in its folder; a rendered one is marked, so a later render may replace it.</summary>
        public static string FileName(string slot, bool rendered) => slot + (rendered ? ".render" : "") + ".png";

        public static bool IsRendered(string file) => file.EndsWith(".render.png", StringComparison.OrdinalIgnoreCase);

        /// <summary>The game shows portraits at 2:1 (128×64); within 2% is fine.</summary>
        public static bool IsTwoToOne(int width, int height) => height > 0 && Math.Abs(width / (double)height - 2.0) <= 0.04;
    }
}
