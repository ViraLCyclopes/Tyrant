using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>Where the game reads a genetic size range.</summary>
    public enum SizeSite
    {
        /// <summary>An uncustomised Nursery egg's random size (VirtualAnimal.GenerateGeneticSize).</summary>
        RandomEgg,

        /// <summary>The Nursery's Customize size slider.</summary>
        NurserySlider,

        /// <summary>A bred animal's size (EmbryoGroup).</summary>
        Breeding,
    }

    /// <summary>A skin as the game holds it, with its sex sizes as they are now.</summary>
    public readonly struct SizeSkin
    {
        public SizeSkin(object skin, double male, double female)
        {
            Skin = skin;
            Male = male;
            Female = female;
        }

        public object Skin { get; }
        public double Male { get; }
        public double Female { get; }
    }

    /// <summary>A species as the game holds it: its data (by reference), id, growth curve and skins in list order.</summary>
    public sealed class SizeSpecies
    {
        public SizeSpecies(object data, string id, (float Time, float Value)[] curve, IReadOnlyList<SizeSkin> skins)
        {
            Data = data;
            Id = id;
            Curve = curve;
            Skins = skins;
        }

        public object Data { get; }
        public string Id { get; }
        public (float Time, float Value)[] Curve { get; }
        public IReadOnlyList<SizeSkin> Skins { get; }
    }

    /// <summary>What one pass changes: new growth curves, new sex sizes, and the warnings to log.</summary>
    public sealed class SizePass
    {
        public List<(object Data, string Id, (float Time, float Value)[] Curve)> Curves { get; } = new List<(object, string, (float, float)[])>();
        public List<(object Skin, double Male, double Female)> Skins { get; } = new List<(object, double, double)>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();

        public bool IsEmpty => Curves.Count == 0 && Skins.Count == 0 && Warnings.Count == 0;
    }

    /// <summary>
    /// Decides how the mods' sizes land on the game's species, pass after pass: a species' curve is replaced once, each skin's
    /// sex sizes are multiplied once (a skin copied from a multiplied one counts as multiplied), and each warning is given once.
    /// The adult multiplier is backed off when the extremes over the genetic ranges in effect would pass the size limit in effect.
    /// </summary>
    public sealed class SizePlan
    {
        /// <summary>
        /// The NurseryMenuV2 methods whose incubation-range reads map the Customize size slider (create, show, update).
        /// Uncustomised eggs roll their size in VirtualAnimal.GenerateGeneticSize, which none of these swaps.
        /// </summary>
        public static readonly IReadOnlyList<string> SliderMethods = new[] { "GenerateNewAnimal", "UpdateCustomizationRect", "UpdateAnimalPreview" };

        private readonly SizeTable _table;
        private readonly List<(double Min, double Max)> _ranges;
        private readonly SizeLimit _limit;
        private readonly Dictionary<object, SizeSettings> _species = new Dictionary<object, SizeSettings>(ReferenceComparer.Instance);
        private readonly HashSet<object> _skins = new HashSet<object>(ReferenceComparer.Instance);
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <param name="geneticRanges">Every genetic range in effect (<see cref="InEffect"/>); at least one.</param>
        /// <param name="limit">The size limit in game: the table's, unless the game's cap could not be lifted (then the game's).</param>
        public SizePlan(SizeTable table, IReadOnlyList<(double Min, double Max)> geneticRanges, SizeLimit? limit = null)
        {
            if (geneticRanges.Count == 0) throw new ArgumentException("At least one genetic range is in effect.", nameof(geneticRanges));
            _table = table;
            _ranges = geneticRanges.ToList();
            _limit = limit ?? table.Limit;
        }

        /// <summary>The range the game uses at a site: Tyrant's where a mod sets one, except for random eggs, which keep the game's.</summary>
        public static (double Min, double Max) RangeFor(SizeSite site, SizeRanges tyrant, (double Min, double Max) incubation, (double Min, double Max) breeding)
        {
            switch (site)
            {
                case SizeSite.NurserySlider: return tyrant.Nursery is { } nursery ? (nursery[0], nursery[1]) : incubation;
                case SizeSite.Breeding: return tyrant.Breeding is { } bred ? (bred[0], bred[1]) : breeding;
                default: return incubation;
            }
        }

        /// <summary>The distinct genetic ranges in effect: random eggs', the slider's and breeding's, in that order.</summary>
        public static List<(double Min, double Max)> InEffect(SizeRanges tyrant, (double Min, double Max) incubation, (double Min, double Max) breeding)
        {
            var result = new List<(double, double)>();
            foreach (var site in new[] { SizeSite.RandomEgg, SizeSite.NurserySlider, SizeSite.Breeding })
            {
                var range = RangeFor(site, tyrant, incubation, breeding);
                if (!result.Contains(range)) result.Add(range);
            }
            return result;
        }

        /// <summary>Tangents for the rebuilt curve's keys: centred finite differences, one-sided at the ends, so the game's curve follows the keys smoothly.</summary>
        public static float[] Slopes((float Time, float Value)[] keys)
        {
            var slopes = new float[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                var a = keys[Math.Max(0, i - 1)];
                var b = keys[Math.Min(keys.Length - 1, i + 1)];
                slopes[i] = b.Time > a.Time ? (b.Value - a.Value) / (b.Time - a.Time) : 0f;
            }
            return slopes;
        }

        /// <summary>A skin copied after the multiply carries the multiplied sizes: it must not be multiplied again.</summary>
        public void Cloned(object source, object clone)
        {
            if (_skins.Contains(source)) _skins.Add(clone);
        }

        /// <summary>What to change now. Species without sizes, and everything an earlier pass did, are left alone.</summary>
        public SizePass Next(IEnumerable<SizeSpecies> species)
        {
            var pass = new SizePass();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in species)
            {
                seen.Add(s.Id);
                var sizes = _table.For(s.Id);
                if (sizes == null || sizes.IsEmpty) continue;
                if (!_species.TryGetValue(s.Data, out var used))
                {
                    _species[s.Data] = used = Settle(s, sizes, pass);
                }
                if (used.Male == 1 && used.Female == 1 && used.Adult == 1) continue;
                foreach (var skin in s.Skins)
                {
                    if (!_skins.Add(skin.Skin)) continue;
                    var (male, female) = SizeMath.Sex(skin.Male, skin.Female, used);
                    pass.Skins.Add((skin.Skin, male, female));
                }
            }
            foreach (var target in _table.All.Where(t => !t.IsEmpty && !seen.Contains(t.Target)))
                if (_warned.Add(target.Target)) pass.Warnings.Add(Unknown(target.Target));
            return pass;
        }

        /// <summary>The sizes a species gets (its adult backed off if needed), and its new curve into the pass.</summary>
        private SizeSettings Settle(SizeSpecies s, SizeSettings sizes, SizePass pass)
        {
            var requested = sizes.Adult ?? 1;
            var skins = s.Skins.Select(k => (k.Male, k.Female)).ToList();
            var adult = SizeMath.BackOff(s.Curve, requested, sizes.Baby, skins, sizes.Male, sizes.Female, _ranges, _limit);
            var used = new SizeSettings { Target = sizes.Target, Adult = adult, Baby = sizes.Baby, Male = sizes.Male ?? 1, Female = sizes.Female ?? 1 };
            var curve = SizeMath.Rebuild(s.Curve, adult, sizes.Baby);
            var sexes = skins.SelectMany(k => { var (m, f) = SizeMath.Sex(k.Male, k.Female, used); return new[] { m, f }; });
            var (min, max) = SizeMath.Extremes(curve, sexes, _ranges);
            var fits = _limit.Contains(min, max);
            var still = fits ? "" : " Some animals still pass it with the size ranges in effect; the game clamps them.";
            var which = _limit.IsGame ? "the game's limit" : $"the size limit ({_limit})";
            if (adult != requested)
                pass.Warnings.Add($"{s.Id}: size {requested.ToString("0.0##", CultureInfo.InvariantCulture)} would pass {which}; using {adult.ToString("0.00", CultureInfo.InvariantCulture)}.{still}");
            else if (!fits)
                pass.Warnings.Add($"{s.Id}: its sizes pass {(_limit.IsGame ? "the game's limit (0.05 to 2)" : which)} with the size ranges in effect; the game clamps those animals.");
            // The curve stays the game's own when neither the adult nor the baby changes: resampling it would only lose shape.
            if (adult != 1 || sizes.Baby != null) pass.Curves.Add((s.Data, s.Id, curve));
            pass.Notes.Add($"{s.Id}: sizes applied (adult {Number(adult)}, baby {(sizes.Baby is double baby ? Number(baby) : "unchanged")}, male {Number(used.Male!.Value)}, female {Number(used.Female!.Value)}).");
            return used;
        }

        private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Unknown(string target)
        {
            var owner = target.StartsWith("tyrant.", StringComparison.Ordinal) ? target.Split('.') : null;
            return owner != null && owner.Length == 3
                ? $"{target}: its sizes are skipped; the species is not loaded (mod '{owner[1]}' missing, off, or the species failed)."
                : $"{target}: its sizes are skipped; the species is not in the game (vivarium species have no sizes).";
        }
    }
}
