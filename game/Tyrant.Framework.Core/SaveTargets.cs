using System;

namespace Tyrant.Framework.Core
{
    public enum SaveTargetKind { AnimalLooks, Mod, CreateVanillaLooks }

    /// <summary>Where a save goes. Owner is the materials.json owner inside the mod (null for AnimalLooks); ModId is null for CreateVanillaLooks.</summary>
    public sealed record SaveTarget(SaveTargetKind Kind, string? ModId, MaterialOwner? Owner);

    /// <summary>Picks where an edited look is kept, so a tweak lands next to the settings it changes.</summary>
    public static class SaveTargets
    {
        /// <param name="modSkin">The mod skin the animal wears, if it is one.</param>
        /// <param name="vanillaSkinKey">"&lt;species&gt;/&lt;skin name&gt;" of the vanilla skin, when it wears one.</param>
        public static SaveTarget For(LookScope scope, string species, (string ModId, string SkinId)? modSkin, string? vanillaSkinKey,
            bool customSpecies, string? customSpeciesModId, MaterialTable table, string materialName)
        {
            switch (scope)
            {
                case LookScope.Animal:
                    return new SaveTarget(SaveTargetKind.AnimalLooks, null, null);
                case LookScope.Skin:
                    if (modSkin is { } skin)
                        return new SaveTarget(SaveTargetKind.Mod, skin.ModId, new MaterialOwner(MaterialOwnerKind.Skin, skin.SkinId, null, null));
                    if (vanillaSkinKey != null)
                    {
                        var slash = vanillaSkinKey.IndexOf('/');
                        var owner = new MaterialOwner(MaterialOwnerKind.VanillaSkin, null,
                            slash > 0 ? vanillaSkinKey.Substring(0, slash) : species, slash >= 0 ? vanillaSkinKey.Substring(slash + 1) : vanillaSkinKey);
                        return table.OwnerOf(MaterialOwnerKind.VanillaSkin, vanillaSkinKey, materialName) is { } winner
                            ? new SaveTarget(SaveTargetKind.Mod, winner.ModId, owner)
                            : new SaveTarget(SaveTargetKind.CreateVanillaLooks, null, owner);
                    }
                    throw new ArgumentException("A skin look needs a mod skin or a vanilla skin key.");
                default:
                    var speciesOwner = new MaterialOwner(MaterialOwnerKind.Species, null, species, null);
                    if (customSpecies && customSpeciesModId != null) return new SaveTarget(SaveTargetKind.Mod, customSpeciesModId, speciesOwner);
                    return table.OwnerOf(MaterialOwnerKind.Species, species, materialName) is { } speciesWinner
                        ? new SaveTarget(SaveTargetKind.Mod, speciesWinner.ModId, speciesOwner)
                        : new SaveTarget(SaveTargetKind.CreateVanillaLooks, null, speciesOwner);
            }
        }
    }
}
