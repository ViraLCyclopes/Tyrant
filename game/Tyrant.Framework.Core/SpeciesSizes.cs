using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// A mod's size changes for one species (sizes/&lt;file&gt;.json); mod.json does not list them. A null key is the game's. Besides
    /// the sizes, the file holds the species' genetic size ranges (the Nursery slider's and breeding's); its size limit is automatic.
    /// </summary>
    public sealed class SizeSettings
    {
        public const string Folder = "sizes";
        public const int Format = 1;

        // The maximums reach Tyrant's own bound (SizeLimit.HighestMax), the most an automatic size limit allows.
        public const double AdultMin = 0.05, AdultMax = 20;
        public const double SexMin = 0.1, SexMax = 20;
        public const double BabyMax = 1;

        /// <summary>A genetic size range is [min, max] with 0 &lt; min &lt;= max &lt;= RangeMax.</summary>
        public const double RangeMax = 20;

        private static readonly string[] Keys = { "adult", "baby", "male", "female", "nursery", "breeding", "limit" };

        public string Target { get; set; } = "";

        /// <summary>Multiplies the real adult size.</summary>
        public double? Adult { get; set; }

        /// <summary>The newborn's size as a share of the adult's: above 0, at most 1.</summary>
        public double? Baby { get; set; }

        public double? Male { get; set; }
        public double? Female { get; set; }

        /// <summary>The genetic size range [min, max] the Nursery's Customize size slider covers for this species; random eggs keep the game's.</summary>
        public double[]? Nursery { get; set; }

        /// <summary>The genetic size range [min, max] of this species' bred young.</summary>
        public double[]? Breeding { get; set; }

        /// <summary>
        /// An old file's size limit [min, max]. The limit is automatic now (<see cref="SizeLimit.Fitting"/>): this is read only so
        /// Check can say it is ignored, and it is never written again (<see cref="ToJson"/>).
        /// </summary>
        public double[]? Limit { get; set; }

        /// <summary>Whether it changes the species' size itself (adult, baby, male or female), not only its ranges or limit.</summary>
        public bool SetsSize => Adult != null || Baby != null || Male != null || Female != null;

        /// <summary>Whether it changes nothing (an old limit alone changes nothing: it is ignored).</summary>
        public bool IsEmpty => !SetsSize && Nursery == null && Breeding == null;

        public static SizeSettings Parse(string json)
        {
            object? root;
            try
            {
                root = Json.Parse(json);
            }
            catch (FormatException ex)
            {
                throw new ManifestException("It is not valid JSON: " + ex.Message);
            }
            if (!(root is Dictionary<string, object?> map)) throw new ManifestException("It must be a JSON object.");
            if (!(map.TryGetValue("format", out var format) && format is double f && f == Format))
                throw new ManifestException($"\"format\" must be {Format}.");
            if (!(map.TryGetValue("target", out var target) && target is string t && t.Trim().Length > 0))
                throw new ManifestException("\"target\" must name a species.");
            foreach (var key in map.Keys)
                if (key != "format" && key != "target" && !Keys.Contains(key))
                    throw new ManifestException($"Unknown key \"{key}\" (use {string.Join(", ", Keys)}).");
            return new SizeSettings
            {
                Target = t.Trim(),
                Adult = Number(map, "adult", AdultMin, AdultMax),
                Baby = Number(map, "baby", 0, BabyMax, minExclusive: true),
                Male = Number(map, "male", SexMin, SexMax),
                Female = Number(map, "female", SexMin, SexMax),
                Nursery = Range(map, "nursery"),
                Breeding = Range(map, "breeding"),
                Limit = Range(map, "limit"),
            };
        }

        /// <summary>What is wrong with a range ("must be …"), or null when it is valid; key is nursery, breeding or limit.</summary>
        public static string? RangeProblem(string key, double[] range)
        {
            if (key == "limit")
                return range.Length == 2 && range[0] >= SizeLimit.LowestMin && range[0] <= SizeLimit.HighestMin && range[1] >= SizeLimit.LowestMax && range[1] <= SizeLimit.HighestMax
                    ? null
                    : FormattableString.Invariant($"must be [min, max] with {SizeLimit.LowestMin} <= min <= {SizeLimit.HighestMin} and {SizeLimit.LowestMax} <= max <= {SizeLimit.HighestMax}");
            return range.Length == 2 && range[0] > 0 && range[0] <= range[1] && range[1] <= RangeMax ? null : $"must be [min, max] with 0 < min <= max <= {RangeMax}";
        }

        private static double? Number(Dictionary<string, object?> map, string key, double min, double max, bool minExclusive = false)
        {
            if (!map.TryGetValue(key, out var value) || value == null) return null;
            if (!(value is double d) || double.IsNaN(d) || d > max || (minExclusive ? d <= min : d < min))
                throw new ManifestException($"\"{key}\" must be {(minExclusive ? "above " + min + " and at most " + max : "from " + min + " to " + max)} (got {Json.Write(value)}).");
            return d;
        }

        private static double[]? Range(Dictionary<string, object?> map, string key)
        {
            if (!map.TryGetValue(key, out var value) || value == null) return null;
            var range = value is List<object?> list && list.All(v => v is double) ? list.Cast<double>().ToArray() : null;
            if (range == null || RangeProblem(key, range) is string)
                throw new ManifestException($"\"{key}\" {RangeProblem(key, range ?? new double[0])} (got {Json.Write(value)}).");
            return range;
        }

        public string ToJson()
        {
            var map = new Dictionary<string, object?> { ["format"] = Format, ["target"] = Target };
            if (Adult is double adult) map["adult"] = adult;
            if (Baby is double baby) map["baby"] = baby;
            if (Male is double male) map["male"] = male;
            if (Female is double female) map["female"] = female;
            if (Nursery != null) map["nursery"] = Nursery.Cast<object?>().ToList();
            if (Breeding != null) map["breeding"] = Breeding.Cast<object?>().ToList();

            return Json.Write(map) + "\n";
        }

        /// <summary>The file a target's sizes go in: its lowercased name made safe for a file name.</summary>
        public static string FileName(string target) => PrefabOverrides.FileName(target);

        /// <summary>Every sizes/*.json of a mod, by file name; a broken file or a second file for a target is skipped and noted.</summary>
        public static List<(string File, SizeSettings Sizes)> ReadFolder(string modDir, List<string>? problems)
        {
            var result = new List<(string, SizeSettings)>();
            var folder = Path.Combine(modDir, Folder);
            if (!Directory.Exists(folder)) return result;
            foreach (var path in Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                var file = Path.GetFileName(path);
                try
                {
                    var sizes = Parse(File.ReadAllText(path));
                    var first = result.FirstOrDefault(r => string.Equals(r.Item2.Target, sizes.Target, StringComparison.OrdinalIgnoreCase));
                    if (first.Item2 != null)
                    {
                        problems?.Add($"sizes/{file} also sets {sizes.Target}, which sizes/{first.Item1} already sets; it was skipped. Merge them into one file.");
                        continue;
                    }
                    result.Add((file, sizes));
                }
                catch (Exception ex) when (ex is ManifestException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    problems?.Add($"sizes/{file} was skipped: {ex.Message}");
                }
            }
            return result;
        }
    }

    /// <summary>Every enabled mod's sizes by target: the later mod in load order wins, per key. An old limit is ignored (the limit is automatic).</summary>
    public sealed class SizeTable
    {
        private readonly Dictionary<string, SizeSettings> _targets = new Dictionary<string, SizeSettings>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether any species has a size or a range set; without one nothing is patched.</summary>
        public bool HasAny => _targets.Values.Any(s => !s.IsEmpty);

        /// <summary>Whether any species sets a Nursery slider range (the slider is patched only then).</summary>
        public bool SetsSlider => _targets.Values.Any(s => s.Nursery != null);

        /// <summary>Whether any species sets a breeding range (breeding is patched only then).</summary>
        public bool SetsBreeding => _targets.Values.Any(s => s.Breeding != null);


        /// <summary>The Nursery slider range of each species that sets one.</summary>
        public IReadOnlyDictionary<string, (double Min, double Max)> SliderRanges => RangesOf(s => s.Nursery);

        /// <summary>The breeding range of each species that sets one.</summary>
        public IReadOnlyDictionary<string, (double Min, double Max)> BreedingRanges => RangesOf(s => s.Breeding);

        public SizeSettings? For(string? target) =>
            target != null && _targets.TryGetValue(target, out var sizes) ? sizes : null;


        public IEnumerable<SizeSettings> All => _targets.Values;


        private Dictionary<string, (double, double)> RangesOf(Func<SizeSettings, double[]?> range)
        {
            var result = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
            foreach (var sizes in _targets.Values)
                if (range(sizes) is { } r) result[sizes.Target] = (r[0], r[1]);
            return result;
        }

        public static SizeTable Build(IReadOnlyList<LoadedMod> mods, out List<string> messages)
        {
            var table = new SizeTable();
            messages = new List<string>();
            foreach (var mod in mods)
            {
                var problems = new List<string>();
                foreach (var (_, sizes) in SizeSettings.ReadFolder(mod.Directory, problems))
                {
                    if (!table._targets.TryGetValue(sizes.Target, out var merged))
                        table._targets[sizes.Target] = merged = new SizeSettings { Target = sizes.Target };
                    merged.Adult = sizes.Adult ?? merged.Adult;
                    merged.Baby = sizes.Baby ?? merged.Baby;
                    merged.Male = sizes.Male ?? merged.Male;
                    merged.Female = sizes.Female ?? merged.Female;
                    merged.Nursery = sizes.Nursery ?? merged.Nursery;
                    merged.Breeding = sizes.Breeding ?? merged.Breeding;

                }
                messages.AddRange(problems.Select(p => $"{mod.Manifest.Id}: {p}"));
            }
            return table;
        }
    }
}
