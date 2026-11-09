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
        private MaterialOverride _current;
        private bool? _dirty; // IsDirty as last worked out; null after any change (worked out again when asked)

        public LookDraft(LookScope scope, MaterialOverride? saved)
        {
            _scope = scope;
            _current = Clone(saved);
            _saved = Text(_current);
            _dirty = false;
        }

        /// <summary>
        /// The look being edited. Change it through this class's methods; code that changes its parts directly must call
        /// <see cref="Touch"/> afterwards, or <see cref="IsDirty"/> may keep its earlier answer.
        /// </summary>
        public MaterialOverride Current
        {
            get => _current;
            private set
            {
                _current = value;
                _dirty = null;
            }
        }

        /// <summary>
        /// True when what would be saved differs from the last saved look. Worked out once after each change (the editor asks
        /// several times a frame), not on every call.
        /// </summary>
        public bool IsDirty => _dirty ??= Text(_current) != _saved;

        /// <summary>The draft's parts were changed directly (not through this class): <see cref="IsDirty"/> is worked out again.</summary>
        public void Touch() => _dirty = null;

        /// <summary>A single number stays a single number; per-variant values edit only the given variant (a missing one is added).</summary>
        public void SetStrength(string variant, float value)
        {
            if (Array.IndexOf(Variants, variant) < 0) throw new ArgumentException($"Unknown variant \"{variant}\".", nameof(variant));
            Touch();
            var strength = Iridescence().Strength ??= new VariantNumbers();
            var single = strength.Albino == null && strength.Melanistic == null && strength.Leucistic == null;
            if (single || variant == "normal") strength.Normal = value;
            else if (variant == "albino") strength.Albino = value;
            else if (variant == "melanistic") strength.Melanistic = value;
            else strength.Leucistic = value;
        }

        /// <summary>A hatchling's strength (one number for every variant); null leaves it to the layers below.</summary>
        public void SetBabyStrength(float? value)
        {
            Touch();
            Iridescence().BabyStrength = value;
            Tidy();
        }

        /// <summary>Variation between animals; only skins and species have it.</summary>
        public void SetVariation(float value)
        {
            if (_scope == LookScope.Animal) throw new InvalidOperationException("Variation applies to a whole skin or species, not to one animal.");
            Touch();
            Iridescence().Variation = value;
        }

        public void SetThinFilm(float thicknessNm)
        {
            Touch();
            Iridescence().Ramp = new RampSource { ThinFilm = new ThinFilmSettings { Thickness = thicknessNm } };
        }

        public void SetRamp(RampSource? ramp)
        {
            Touch();
            Iridescence().Ramp = ramp;
            Tidy();
        }

        public void SetMask(MaskSource? mask)
        {
            Touch();
            Iridescence().Mask = mask;
            Tidy();
        }

        public void SetShaping(float? scale, float? power, float? bias)
        {
            Touch();
            var iridescence = Iridescence();
            iridescence.Scale = scale;
            iridescence.Power = power;
            iridescence.Bias = bias;
            Tidy();
        }

        public void SetSetting(string key, MaterialValue? value)
        {
            Touch();
            if (value is { } v) Current.Settings[key] = v;
            else Current.Settings.Remove(key);
        }

        /// <summary>
        /// Off writes strength 0 (dropping the block would show a lower layer's iridescence), and a baby strength this draft holds
        /// 0 too; on sets 1 (every variant it holds) when all were 0 or unset.
        /// </summary>
        public void SetIridescenceOn(bool on)
        {
            Touch();
            var iridescence = Iridescence();
            var strength = iridescence.Strength ??= new VariantNumbers();
            if (on)
            {
                if (strength.Normal == 0 && !(strength.Albino > 0) && !(strength.Melanistic > 0) && !(strength.Leucistic > 0)) Fill(strength, 1);
                return;
            }
            Fill(strength, 0);
            if (iridescence.BabyStrength != null) iridescence.BabyStrength = 0;
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
            Touch();
            var fur = Current.Fur ??= new FurMasks();
            if (infant) fur.InfantMask = mask;
            else fur.Mask = mask;
            if (fur.IsEmpty) Current.Fur = null;
        }

        /// <summary>Whether Tyrant grows fur at this scope: true, false (do not force it over a lower layer), or null (left out).</summary>
        public void SetFurGrow(bool? grow)
        {
            Touch();
            var fur = Current.Fur ??= new FurMasks();
            fur.Grow = grow;
            if (fur.IsEmpty) Current.Fur = null;
        }

        /// <summary>Which ages wear fur at this scope; null leaves it to the layers below (or the game).</summary>
        public void SetFurAge(FurAge? age)
        {
            Touch();
            var fur = Current.Fur ??= new FurMasks();
            fur.Age = age;
            if (fur.IsEmpty) Current.Fur = null;
        }

        /// <summary>Back to the last saved look.</summary>
        public void Reset() => Current = Parse(_saved);

        public void MarkSaved()
        {
            _saved = Text(_current);
            _dirty = false;
        }

        private IridescenceSettings Iridescence() => _current.Iridescence ??= new IridescenceSettings();

        private void Tidy()
        {
            if (_current.Iridescence != null && _current.Iridescence.IsEmpty) _current.Iridescence = null;
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
