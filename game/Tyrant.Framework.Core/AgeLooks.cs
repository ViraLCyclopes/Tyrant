namespace Tyrant.Framework.Core
{
    /// <summary>What Tyrant puts in one fur mask slot (_InfantFurMask or _AdultFurMask).</summary>
    public enum FurFill
    {
        /// <summary>Nothing: the game's own mask stays (its fur map, or its blank FurMaskEmpty).</summary>
        Leave,

        /// <summary>The block's mask PNG.</summary>
        File,

        /// <summary>White: full-length fur over the whole body (Grow fur on an age with no fur mask).</summary>
        White,

        /// <summary>Black: no fur at this age, the way the game removes baby fluff (its adult slot is the black FurMaskEmpty).</summary>
        Black,
    }

    /// <summary>
    /// How a look changes as an animal grows up. The game's AnimalGrowthManager.UpdateGrowth lerps the iridescence strength
    /// from 0 to the adult's by the integument (integumentGrowthCurve at the animal's maturity: 0 a hatchling, 1 grown), and
    /// the fur shader lerps the fur height between the infant and the adult fur mask's red channel by the same integument
    /// (black: no fur, white: the full _Length). So a baby strength is the lerp's other end, and which ages wear fur
    /// (<see cref="FurAge"/>) is said through the masks: an age without fur gets a black mask. The framework writes these;
    /// the in-game editor and its tests read the same rules.
    /// </summary>
    public static class AgeLooks
    {
        /// <summary>The strength shown at this integument: from the baby's to the adult's (the game's own lerps from 0), unclamped like the game's math.lerp.</summary>
        public static float Strength(float baby, float adult, float integument) => baby + (adult - baby) * integument;

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

        /// <summary>
        /// One fur slot: black for an age the fur age leaves bare (over any mask), else the block's PNG, else the game's own
        /// ("game", or a fur map of the game's for this age), else white where the age or grow wants fur, else the game's.
        /// </summary>
        /// <param name="infant">The infant slot; else the adult one.</param>
        /// <param name="file">The block has a mask PNG for this slot (a baby slot left out uses the adult one).</param>
        /// <param name="keepGame">The block says "game" for this slot.</param>
        /// <param name="gameHasMap">The game gives this age a fur mask of its own (a fur map, not the blank FurMaskEmpty).</param>
        public static FurFill FurSlot(FurAge? age, bool grow, bool infant, bool file, bool keepGame, bool gameHasMap)
        {
            var (babyFur, adultFur) = GrownStages(age, grow);
            var wanted = infant ? babyFur : adultFur;
            if (age != null && !wanted) return FurFill.Black;
            if (file) return FurFill.File;
            if (keepGame || gameHasMap || !wanted) return FurFill.Leave;
            return FurFill.White;
        }

        /// <summary>
        /// True when Tyrant writes the fur sheen (_FresnelIntensity, a rim sheen the game sets to 1 on skins with fur maps) as 1,
        /// so grown fur looks like a furry skin's: grow is on or an age is set.
        /// </summary>
        public static bool WritesFurSheen(FurMasks? fur) => NeedsFurVariant(fur);

        /// <summary>True when the animal needs the shader's fur variant (as a skin with a fur map gets): grow is on or an age is set.</summary>
        public static bool NeedsFurVariant(FurMasks? fur) => fur != null && (fur.Grow == true || fur.Age != null);
    }

    /// <summary>
    /// The animal shader's fur mask mode, a material keyword: furry species' materials use _FURMASKING_TEXTURE (the fur masks'
    /// red sets the height); furless ones _FURMASKING_NONE, where no mask is read and the fur is full length everywhere.
    /// </summary>
    public static class FurMasking
    {
        public const string Texture = "_FURMASKING_TEXTURE";
        public const string None = "_FURMASKING_NONE";
        public const string VertexColor = "_FURMASKING_VERTEXCOLOR";
        public const string DispTexScale = "_FurDispTexScale";

        /// <summary>The strand noise tiling Tyrant gives a furless template (whose is 1, 1); furry species use 3 to 6.</summary>
        public const float DefaultDispScale = 4;

        /// <summary>
        /// True when a material with the fur variant must switch to texture masking: it is made from a furless template
        /// (_FURMASKING_NONE), so without it Grow fur and the fur age would ignore every mask. Texture masking, and vertex
        /// colour masking a species uses on purpose, are left alone.
        /// </summary>
        public static bool NeedsTextureMasking(bool tessellationFur, bool maskingNone) => tessellationFur && maskingNone;

        /// <summary>The strand noise tiling to write over a furless template's 1, 1; null keeps the material's own.</summary>
        public static (float X, float Y)? DispScale(float x, float y) =>
            x == 1 && y == 1 ? (DefaultDispScale, DefaultDispScale) : ((float, float)?)null;
    }
}
