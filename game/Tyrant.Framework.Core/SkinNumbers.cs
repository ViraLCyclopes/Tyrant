using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// UserData/Tyrant/skin-slots.json: the permanent index of every added skin in its species' skin list, so saved animals
    /// keep their skin whatever mods are installed, removed or reordered.
    /// </summary>
    public sealed class SkinNumbers
    {
        public const string FileName = "skin-slots.json";

        /// <summary>
        /// Added skins start here (or after the vanilla skins, if a species has more), so they never sit in the low numbers
        /// a game update could give to new vanilla skins.
        /// </summary>
        public const int FirstNumber = 15;

        /// <summary>Higher numbers in the file are refused: each one below it becomes a skin entry in game.</summary>
        public const int MaxNumber = 9999;

        private static readonly IReadOnlyDictionary<string, int> None = new Dictionary<string, int>();
        private readonly Dictionary<string, Dictionary<string, int>> _species = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        public IEnumerable<string> Species => _species.Keys;

        /// <summary>True when any skin has a number: saved animals may wear added skins, so stand-ins and the clamp are needed.</summary>
        public bool HasAny => _species.Values.Any(n => n.Count > 0);

        /// <summary>
        /// Species that need set-up in game: those with installed skins plus those with numbers in the file, so a species whose
        /// skin mods are all gone still gets its stand-ins.
        /// </summary>
        public List<string> SpeciesToSetUp(IEnumerable<string> withInstalledSkins) =>
            withInstalledSkins.Concat(_species.Where(p => p.Value.Count > 0).Select(p => p.Key))
                .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();

        public IReadOnlyDictionary<string, int> Of(string species) => _species.TryGetValue(species, out var numbers) ? numbers : None;

        public static SkinNumbers Parse(string? json)
        {
            var result = new SkinNumbers();
            if (json == null) return result;
            if (!(Json.Parse(json) is Dictionary<string, object?> root) || !(root.TryGetValue("species", out var species) && species is Dictionary<string, object?> map))
                throw new FormatException("expected { \"format\": 1, \"species\": { ... } }");
            if (!(root.TryGetValue("format", out var format) && format is double f && f == 1))
                throw new FormatException("only \"format\": 1 is understood (a newer Tyrant wrote this file?)");
            foreach (var pair in map)
            {
                if (!(pair.Value is Dictionary<string, object?> keys)) throw new FormatException($"\"{pair.Key}\" must map skin keys to numbers");
                var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var key in keys)
                    numbers[key.Key] = key.Value is double d && d >= 0 && d <= MaxNumber && d == Math.Floor(d) ? (int)d : throw new FormatException($"\"{key.Key}\" needs a whole number from 0 to {MaxNumber}");
                result._species[pair.Key] = numbers;
            }
            return result;
        }

        public string ToJson() =>
            Json.Write(new Dictionary<string, object?>
            {
                ["format"] = 1,
                ["species"] = _species.OrderBy(s => s.Key, StringComparer.Ordinal).ToDictionary(
                    s => s.Key,
                    s => (object?)s.Value.OrderBy(k => k.Value).ToDictionary(k => k.Key, k => (object?)k.Value)),
            }) + "\n";

        /// <summary>
        /// Gives each key without a number the lowest free number at or above max(vanillaCount, FirstNumber), and moves numbers that a grown vanilla
        /// list now covers (a game update added skins). Keys are never removed here. True when anything changed.
        /// </summary>
        public bool Assign(string species, int vanillaCount, IEnumerable<string> keys, List<string> messages)
        {
            if (!_species.TryGetValue(species, out var numbers)) _species[species] = numbers = new Dictionary<string, int>(StringComparer.Ordinal);
            var used = new HashSet<int>(numbers.Values);
            var first = Math.Max(vanillaCount, FirstNumber);
            var changed = false;
            foreach (var clash in numbers.Where(p => p.Value < vanillaCount).OrderBy(p => p.Value).ToList())
            {
                used.Remove(clash.Value);
                var number = LowestFree(first, used);
                numbers[clash.Key] = number;
                used.Add(number);
                changed = true;
                messages.Add($"{species}: the game now has {vanillaCount} vanilla skins, so {clash.Key} moved from number {clash.Value} to {number}; " +
                             "saved animals that wore it now show the vanilla skin with that number.");
            }
            foreach (var key in keys.Distinct(StringComparer.Ordinal))
            {
                if (numbers.ContainsKey(key)) continue;
                var number = LowestFree(first, used);
                numbers[key] = number;
                used.Add(number);
                changed = true;
            }
            return changed;
        }

        public bool Forget(string species, string key) => _species.TryGetValue(species, out var numbers) && numbers.Remove(key);

        private static int LowestFree(int start, HashSet<int> used)
        {
            var n = start;
            while (used.Contains(n)) n++;
            return n;
        }
    }

    /// <summary>One position after the vanilla skins: an added skin (Key) or a hidden stand-in (Key null).</summary>
    public sealed class SkinLayoutEntry
    {
        public SkinLayoutEntry(int number, string? key, bool reserved = false)
        {
            Number = number;
            Key = key;
            Reserved = reserved;
        }

        public int Number { get; }
        public string? Key { get; }

        /// <summary>A stand-in below the first added-skin number: never used by any mod, just keeps the numbering.</summary>
        public bool Reserved { get; }
    }

    public static class SkinLayout
    {
        /// <summary>Every position from vanillaCount to the highest assigned number, in order: what to append to skinsData.</summary>
        public static List<SkinLayoutEntry> For(IReadOnlyDictionary<string, int> numbers, int vanillaCount)
        {
            var layout = new List<SkinLayoutEntry>();
            if (numbers.Count == 0) return layout;
            // A hand-edited file can give two keys one number: the first key in ordinal order gets it.
            var byNumber = numbers.Where(p => p.Value >= vanillaCount).GroupBy(p => p.Value)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).First());
            if (byNumber.Count == 0) return layout;
            for (var n = vanillaCount; n <= byNumber.Keys.Max(); n++)
            {
                var key = byNumber.TryGetValue(n, out var k) ? k : null;
                layout.Add(new SkinLayoutEntry(n, key, key == null && n < Math.Max(vanillaCount, SkinNumbers.FirstNumber)));
            }
            return layout;
        }
    }
}
