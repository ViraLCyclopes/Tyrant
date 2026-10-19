using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>What a preset starts the look from: a species' iridescence or its fur.</summary>
    public enum PresetKind { Iridescence, Fur }

    /// <summary>
    /// One skin variation asset (a sex of a skin) as a preset reads it, from the game's AnimalSkinVariationAsset or the data
    /// dump's copy of it.
    /// </summary>
    public sealed class PresetSkin
    {
        /// <summary>defaultVariationData.iridescence: the skin shimmers.</summary>
        public bool Shimmers { get; set; }

        /// <summary>Its iridescenceStrength: the default one, and a mutation's where it differs; null when not known.</summary>
        public VariantNumbers? Strength { get; set; }

        /// <summary>Its adultFurMap names a texture (the game's own test for fur, AnimalMaterials.CalculateKeywords).</summary>
        public bool HasFur { get; set; }

        /// <summary>Its adultFuzzDroopiness (x, y, z); null when not known.</summary>
        public float[]? Droop { get; set; }
    }

    /// <summary>
    /// What a preset copies from a vanilla species: numbers only. Its ramp and mask are named (<see cref="RampSource.Species"/>),
    /// never copied into a file: the game's images stay out of mods.
    /// </summary>
    public sealed class SpeciesPreset
    {
        public SpeciesPreset(string species, string name)
        {
            Species = species;
            Name = name;
        }

        /// <summary>The game's species id.</summary>
        public string Species { get; }

        /// <summary>The name the game shows.</summary>
        public string Name { get; }

        /// <summary>A skin of it shimmers: it is offered as an iridescence preset.</summary>
        public bool Shimmers { get; set; }

        /// <summary>A skin of it has fur: it is offered as a fur preset.</summary>
        public bool Furry { get; set; }

        /// <summary>The strength of its first shimmering skin; null when none shimmers or the strength is not known.</summary>
        public VariantNumbers? Strength { get; set; }

        /// <summary>Its material has an iridescence ramp texture (_IridescenceTexture) / mask texture (_IridescenceMask).</summary>
        public bool HasRamp { get; set; }

        public bool HasMask { get; set; }

        /// <summary>Its material's numbers (_IridescenceScale, _Length, …); one left out is not copied.</summary>
        public Dictionary<string, float> Floats { get; } = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>The adult droop of its first furred skin; null when none has fur or it is not known.</summary>
        public float[]? Droop { get; set; }

        /// <summary>
        /// A species' preset from its skins in the game's order (each skin's male asset, then its female one), its material's
        /// numbers, and whether that material has a ramp and a mask.
        /// </summary>
        public static SpeciesPreset Of(string species, string name, IEnumerable<PresetSkin> skins, IReadOnlyDictionary<string, float>? floats, bool hasRamp, bool hasMask)
        {
            var preset = new SpeciesPreset(species, name) { HasRamp = hasRamp, HasMask = hasMask };
            foreach (var skin in skins)
            {
                if (skin.Shimmers && !preset.Shimmers)
                {
                    preset.Shimmers = true;
                    preset.Strength = LookEditing.Copy(skin.Strength);
                }
                if (skin.HasFur && !preset.Furry)
                {
                    preset.Furry = true;
                    preset.Droop = (float[]?)skin.Droop?.Clone();
                }
            }
            if (floats != null)
                foreach (var pair in floats) preset.Floats[pair.Key] = pair.Value;
            return preset;
        }

        /// <summary>Species as a preset list shows them: by name (any case), each id once.</summary>
        public static IReadOnlyList<(string Id, string Name)> Sorted(IEnumerable<(string Id, string Name)> species) =>
            species.GroupBy(s => s.Id, StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Where the presets' values come from: the game at runtime (its material loads in the background) or the data dump.</summary>
    public interface ISpeciesPresets
    {
        /// <summary>The species' preset; null while it is not known yet, or when the species has none.</summary>
        SpeciesPreset? Preset(string species);
    }

    public static partial class LookEditing
    {
        /// <summary>The fur settings a fur preset copies from the species' material (its droop direction comes from its skin).</summary>
        public static readonly IReadOnlyList<string> PresetFurSettings = new[] { "_Length", "_Droopage", "_FurWindStrength", "_FurWindSpeed", "_FurWindScale" };

        private const string DroopDirection = "_DroopDirection";

        /// <summary>Applies the species' preset (<see cref="ApplyPreset(LookDraft, PresetKind, SpeciesPreset)"/>); false, changing nothing, while it is not known.</summary>
        public static bool ApplyPreset(LookDraft draft, PresetKind kind, ISpeciesPresets source, string species)
        {
            if (!(source.Preset(species) is SpeciesPreset preset)) return false;
            ApplyPreset(draft, kind, preset);
            return true;
        }

        /// <summary>
        /// Starts the draft from a species. Iridescence: its ramp and mask by reference (no mask texture: everywhere; no ramp
        /// texture: the draft's ramp stays), its material's scale, power and bias, its first shimmering skin's strength, and on.
        /// Fur: grow on, its material's length, droop and wind, its skin's droop direction; the fur mask stays. Every number is
        /// kept within its setting's range, so the look can be saved; everything else in the draft stays, and stays editable.
        /// </summary>
        public static void ApplyPreset(LookDraft draft, PresetKind kind, SpeciesPreset preset)
        {
            if (kind == PresetKind.Fur)
            {
                draft.SetFurGrow(true);
                foreach (var key in PresetFurSettings)
                    if (preset.Floats.TryGetValue(key, out var value) && MaterialProperties.Find(key) is MaterialProperty property)
                        draft.SetSetting(key, MaterialValue.OfNumber(Clamp(value, property.Min, property.Max)));
                if (preset.Droop is float[] droop && MaterialProperties.Find(DroopDirection) is MaterialProperty range)
                    draft.SetSetting(DroopDirection, MaterialValue.OfVector(Enumerable.Range(0, 3).Select(i => Clamp(i < droop.Length ? droop[i] : 0, range.Min, range.Max)).ToArray()));
                return;
            }
            if (preset.HasRamp) draft.SetRamp(new RampSource { Species = preset.Species });
            draft.SetMask(preset.HasMask ? new MaskSource { Species = preset.Species } : new MaskSource { Everywhere = true });
            var current = draft.Current.Iridescence;
            float? Shaping(string key, float? now) => preset.Floats.TryGetValue(key, out var v) ? Clamp(v, MinShaping, MaxShaping) : now;
            draft.SetShaping(Shaping("_IridescenceScale", current?.Scale), Shaping("_IridescencePower", current?.Power), Shaping("_IridescenceBias", current?.Bias));
            if (preset.Strength is VariantNumbers strength)
            {
                var copy = Copy(strength)!;
                copy.Normal = Clamp(copy.Normal, 0, 1);
                if (copy.Albino is float a) copy.Albino = Clamp(a, 0, 1);
                if (copy.Melanistic is float m) copy.Melanistic = Clamp(m, 0, 1);
                if (copy.Leucistic is float l) copy.Leucistic = Clamp(l, 0, 1);
                draft.Current.Iridescence!.Strength = copy;
            }
            draft.SetIridescenceOn(true); // a strength of 0 everywhere (or none, when the skin's is not known) becomes 1
        }

        /// <summary>The block a preset alone makes (what the app merges into its draft, and the CLI over a saved block).</summary>
        public static MaterialOverride PresetBlock(SpeciesPreset preset, PresetKind kind)
        {
            var draft = new LookDraft(LookScope.Species, null);
            ApplyPreset(draft, kind, preset);
            return draft.Current;
        }
    }
}
