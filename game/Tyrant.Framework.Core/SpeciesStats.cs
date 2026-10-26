using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    public sealed class SpeciesStat
    {
        public SpeciesStat(string id, string label, string unit, double min, double max, string field, string? component = null)
        { Id = id; Label = label; Unit = unit; Min = min; Max = max; Field = field; Component = component; }
        public string Id { get; }
        public string Label { get; }
        public string Unit { get; }
        public double Min { get; }
        public double Max { get; }
        public string Field { get; }
        public string? Component { get; }
    }

    /// <summary>Tyrant's editable animal database schema; omitted stats inherit the vanilla template.</summary>
    public static class SpeciesStats
    {
        public static readonly IReadOnlyList<SpeciesStat> Fields = new[]
        {
            new SpeciesStat("adultMassKg", "Adult mass", "kg", 0.001, 1000000, "massAverageAdult"),
            new SpeciesStat("babyMassKg", "Baby mass", "kg", 0.001, 1000000, "massAverageBaby"),
            new SpeciesStat("matureAgeYears", "Maturity age", "years", 0.001, 10000, "matureMaxAge", "x"),
            new SpeciesStat("maxAgeYears", "Lifespan", "years", 0.001, 10000, "matureMaxAge", "y"),
            new SpeciesStat("temperatureMinC", "Minimum temperature", "°C", -100, 100, "minMaxTemperature", "x"),
            new SpeciesStat("temperatureMaxC", "Maximum temperature", "°C", -100, 100, "minMaxTemperature", "y"),
            new SpeciesStat("foliageMin", "Minimum plant coverage", "fraction (0–1)", 0, 1, "minMaxFoliage", "x"),
            new SpeciesStat("foliageMax", "Maximum plant coverage", "fraction (0–1)", 0, 1, "minMaxFoliage", "y"),
            new SpeciesStat("waterMin", "Minimum water", "fraction (0–1)", 0, 1, "minMaxWater", "x"),
            new SpeciesStat("waterMax", "Maximum water", "fraction (0–1)", 0, 1, "minMaxWater", "y"),
            new SpeciesStat("bedRequirement", "Bed requirement", "fraction (0–1)", 0, 1, "bedRequirement"),
            new SpeciesStat("vocalizationRangeM", "Vocalization range", "m", 0, 100000, "adultVocalizationAudioRange"),
            new SpeciesStat("footstepRangeM", "Footstep range", "m", 0, 100000, "adultFootstepAudioRange"),
        };

        public static void Validate(IReadOnlyDictionary<string, double> stats)
        {
            foreach (var pair in stats)
            {
                var field = Fields.FirstOrDefault(f => f.Id == pair.Key) ?? throw new ManifestException($"Unknown species stat '{pair.Key}'.");
                if (double.IsNaN(pair.Value) || double.IsInfinity(pair.Value) || pair.Value < field.Min || pair.Value > field.Max)
                    throw new ManifestException($"{field.Label} must be between {field.Min} and {field.Max} {field.Unit}.");
            }
            foreach (var range in new[] { ("matureAgeYears", "maxAgeYears"), ("temperatureMinC", "temperatureMaxC"), ("foliageMin", "foliageMax"), ("waterMin", "waterMax") })
                if (stats.TryGetValue(range.Item1, out var min) && stats.TryGetValue(range.Item2, out var max) && min > max)
                    throw new ManifestException($"{range.Item1} must not exceed {range.Item2}.");
        }

        internal static Dictionary<string, double> Parse(object? value)
        {
            if (!(value is Dictionary<string, object?> map)) throw new ManifestException("Species stats must be an object of numeric overrides.");
            var stats = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var pair in map)
                stats[pair.Key] = pair.Value is double n ? n : throw new ManifestException($"Species stat '{pair.Key}' must be a number; omit it to inherit.");
            Validate(stats);
            return stats;
        }
    }
}
