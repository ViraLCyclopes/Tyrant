using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>A mod's size changes for one species (sizes/&lt;file&gt;.json); mod.json does not list them. A null key is unchanged.</summary>
    public sealed class SizeSettings
    {
        public const string Folder = "sizes";
        public const int Format = 1;

        // The maximums reach the highest size limit (SizeLimit.HighestMax), so a raised limit can be used.
        public const double AdultMin = 0.05, AdultMax = 20;
        public const double SexMin = 0.1, SexMax = 20;
        public const double BabyMax = 1;

        public string Target { get; set; } = "";

        /// <summary>Multiplies the real adult size.</summary>
        public double? Adult { get; set; }

        /// <summary>The newborn's size as a share of the adult's: above 0, at most 1.</summary>
        public double? Baby { get; set; }

        public double? Male { get; set; }
        public double? Female { get; set; }

        public bool IsEmpty => Adult == null && Baby == null && Male == null && Female == null;

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
                if (key != "format" && key != "target" && key != "adult" && key != "baby" && key != "male" && key != "female")
                    throw new ManifestException($"Unknown key \"{key}\" (use adult, baby, male, female).");
            return new SizeSettings
            {
                Target = t.Trim(),
                Adult = Number(map, "adult", AdultMin, AdultMax),
                Baby = Number(map, "baby", 0, BabyMax, minExclusive: true),
                Male = Number(map, "male", SexMin, SexMax),
                Female = Number(map, "female", SexMin, SexMax),
            };
        }

        private static double? Number(Dictionary<string, object?> map, string key, double min, double max, bool minExclusive = false)
        {
            if (!map.TryGetValue(key, out var value) || value == null) return null;
            if (!(value is double d) || double.IsNaN(d) || d > max || (minExclusive ? d <= min : d < min))
                throw new ManifestException($"\"{key}\" must be {(minExclusive ? "above " + min + " and at most " + max : "from " + min + " to " + max)} (got {Json.Write(value)}).");
            return d;
        }

        public string ToJson()
        {
            var map = new Dictionary<string, object?> { ["format"] = Format, ["target"] = Target };
            if (Adult is double adult) map["adult"] = adult;
            if (Baby is double baby) map["baby"] = baby;
            if (Male is double male) map["male"] = male;
            if (Female is double female) map["female"] = female;
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

    /// <summary>
    /// mod.json "sizeRanges", each [min, max]; null is unchanged. Nursery and Breeding are the genetic size ranges the Nursery slider
    /// and breeding use; Limit widens the game's 0.05 to 2 cap on an animal's final scale.
    /// </summary>
    public sealed class SizeRanges
    {
        public const double Min = 0, Max = 20;

        public double[]? Nursery { get; set; }
        public double[]? Breeding { get; set; }
        public double[]? Limit { get; set; }

        public bool IsEmpty => Nursery == null && Breeding == null && Limit == null;

        internal static SizeRanges Parse(object value)
        {
            if (!(value is Dictionary<string, object?> block)) throw new ManifestException("\"sizeRanges\" must be an object with \"nursery\", \"breeding\" or \"limit\".");
            foreach (var key in block.Keys)
                if (key != "nursery" && key != "breeding" && key != "limit") throw new ManifestException($"\"sizeRanges\" has unknown key \"{key}\" (use nursery, breeding, limit).");
            return new SizeRanges { Nursery = Range(block, "nursery"), Breeding = Range(block, "breeding"), Limit = Range(block, "limit") };
        }

        /// <summary>What is wrong with a range ("must be …"), or null when it is valid; key is nursery, breeding or limit.</summary>
        public static string? Problem(string key, double[] range)
        {
            if (key == "limit")
                return range.Length == 2 && range[0] >= SizeLimit.LowestMin && range[0] <= SizeLimit.HighestMin && range[1] >= SizeLimit.LowestMax && range[1] <= SizeLimit.HighestMax
                    ? null
                    : FormattableString.Invariant($"must be [min, max] with {SizeLimit.LowestMin} <= min <= {SizeLimit.HighestMin} and {SizeLimit.LowestMax} <= max <= {SizeLimit.HighestMax}");
            return range.Length == 2 && range[0] > Min && range[0] <= range[1] && range[1] <= Max ? null : $"must be [min, max] with 0 < min <= max <= {Max}";
        }

        private static double[]? Range(Dictionary<string, object?> block, string key)
        {
            if (!block.TryGetValue(key, out var value) || value == null) return null;
            if (!(value is List<object?> list && list.Count == 2 && list[0] is double min && list[1] is double max))
                throw new ManifestException($"\"sizeRanges.{key}\" must be [min, max].");
            var range = new[] { min, max };
            if (Problem(key, range) is string problem) throw new ManifestException($"\"sizeRanges.{key}\" {problem} (got {Json.Write(value)}).");
            return range;
        }

        internal Dictionary<string, object?> ToJson()
        {
            var block = new Dictionary<string, object?>();
            if (Nursery != null) block["nursery"] = Nursery.Cast<object?>().ToList();
            if (Breeding != null) block["breeding"] = Breeding.Cast<object?>().ToList();
            if (Limit != null) block["limit"] = Limit.Cast<object?>().ToList();
            return block;
        }
    }

    /// <summary>
    /// Every enabled mod's sizes by target and its size ranges: the later mod in load order wins, per key and per range, except the
    /// size limit, where the widest wins (so one mod cannot cap another's sizes again).
    /// </summary>
    public sealed class SizeTable
    {
        private readonly Dictionary<string, SizeSettings> _targets = new Dictionary<string, SizeSettings>(StringComparer.OrdinalIgnoreCase);

        public bool HasAny => _targets.Count > 0 || !Ranges.IsEmpty;

        /// <summary>The merged ranges; a range no mod sets is null.</summary>
        public SizeRanges Ranges { get; } = new SizeRanges();

        /// <summary>The size limit in effect: the widest a mod sets, or the game's.</summary>
        public SizeLimit Limit => SizeLimit.Of(Ranges.Limit);

        public SizeSettings? For(string? target) =>
            target != null && _targets.TryGetValue(target, out var sizes) ? sizes : null;

        public IEnumerable<SizeSettings> All => _targets.Values;

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
                }
                messages.AddRange(problems.Select(p => $"{mod.Manifest.Id}: {p}"));
                var ranges = mod.Manifest.SizeRanges;
                if (ranges == null) continue;
                table.Ranges.Nursery = ranges.Nursery ?? table.Ranges.Nursery;
                table.Ranges.Breeding = ranges.Breeding ?? table.Ranges.Breeding;
                table.Ranges.Limit = SizeLimit.Widest(table.Ranges.Limit, ranges.Limit);
            }
            return table;
        }
    }
}
