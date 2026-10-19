using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    public enum LookScope { Animal, Skin, Species }

    /// <summary>
    /// The edits of one material in the in-game editor: a preview until saved. Every edit leaves a block that can be written
    /// (empty parts are dropped); strength edits never lose the other mutations' values.
    /// </summary>
    public sealed class LookDraft
    {
        private static readonly string[] Variants = { "normal", "albino", "melanistic", "leucistic" };

        private readonly LookScope _scope;
        private string _saved;

        public LookDraft(LookScope scope, MaterialOverride? saved)
        {
            _scope = scope;
            Current = Clone(saved);
            _saved = Text(Current);
        }

        public MaterialOverride Current { get; private set; }

        public bool IsDirty => Text(Current) != _saved;

        /// <summary>A single number stays a single number; per-variant values edit only the given variant (a missing one is added).</summary>
        public void SetStrength(string variant, float value)
        {
            if (Array.IndexOf(Variants, variant) < 0) throw new ArgumentException($"Unknown variant \"{variant}\".", nameof(variant));
            var strength = Iridescence().Strength ??= new VariantNumbers();
            var single = strength.Albino == null && strength.Melanistic == null && strength.Leucistic == null;
            if (single || variant == "normal") strength.Normal = value;
            else if (variant == "albino") strength.Albino = value;
            else if (variant == "melanistic") strength.Melanistic = value;
            else strength.Leucistic = value;
        }

        /// <summary>Variation between animals; only skins and species have it.</summary>
        public void SetVariation(float value)
        {
            if (_scope == LookScope.Animal) throw new InvalidOperationException("Variation applies to a whole skin or species, not to one animal.");
            Iridescence().Variation = value;
        }

        public void SetThinFilm(float thicknessNm) =>
            Iridescence().Ramp = new RampSource { ThinFilm = new ThinFilmSettings { Thickness = thicknessNm } };

        public void SetRamp(RampSource? ramp)
        {
            Iridescence().Ramp = ramp;
            Tidy();
        }

        public void SetMask(MaskSource? mask)
        {
            Iridescence().Mask = mask;
            Tidy();
        }

        public void SetShaping(float? scale, float? power, float? bias)
        {
            var iridescence = Iridescence();
            iridescence.Scale = scale;
            iridescence.Power = power;
            iridescence.Bias = bias;
            Tidy();
        }

        public void SetSetting(string key, MaterialValue? value)
        {
            if (value is { } v) Current.Settings[key] = v;
            else Current.Settings.Remove(key);
        }

        /// <summary>Off writes strength 0 (dropping the block would show a lower layer's iridescence); on sets 1 (every variant it holds) when all were 0 or unset.</summary>
        public void SetIridescenceOn(bool on)
        {
            var strength = Iridescence().Strength ??= new VariantNumbers();
            if (on)
            {
                if (strength.Normal == 0 && !(strength.Albino > 0) && !(strength.Melanistic > 0) && !(strength.Leucistic > 0)) Fill(strength, 1);
                return;
            }
            Fill(strength, 0);
        }

        private static void Fill(VariantNumbers strength, float value)
        {
            strength.Normal = value;
            if (strength.Albino != null) strength.Albino = value;
            if (strength.Melanistic != null) strength.Melanistic = value;
            if (strength.Leucistic != null) strength.Leucistic = value;
        }

        public void SetFurMask(MaskSource? mask, bool infant)
        {
            var fur = Current.Fur ??= new FurMasks();
            if (infant) fur.InfantMask = mask;
            else fur.Mask = mask;
            if (fur.IsEmpty) Current.Fur = null;
        }

        /// <summary>Back to the last saved look.</summary>
        public void Reset() => Current = Parse(_saved);

        public void MarkSaved() => _saved = Text(Current);

        private IridescenceSettings Iridescence() => Current.Iridescence ??= new IridescenceSettings();

        private void Tidy()
        {
            if (Current.Iridescence != null && Current.Iridescence.IsEmpty) Current.Iridescence = null;
        }

        // The JSON form is the comparison and the copy: what would be saved is what counts as a change.
        private static string Text(MaterialOverride material) =>
            Json.Write(MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride> { ["m"] = material }));

        private static MaterialOverride Clone(MaterialOverride? material) => material == null ? new MaterialOverride() : Parse(Text(material));

        private static MaterialOverride Parse(string text)
        {
            var parsed = MaterialOverrides.Parse(Json.Parse(text), "Draft", true);
            return parsed != null && parsed.TryGetValue("m", out var material) ? material : new MaterialOverride();
        }
    }
}
