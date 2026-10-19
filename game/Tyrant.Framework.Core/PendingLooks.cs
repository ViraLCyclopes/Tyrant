using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// Looks saved in the Nursery for the next animal incubated (the editor's "Next created"), armed in memory until an
    /// incubation started from the Nursery takes one: one per species, skin (a mod skin's key or a vanilla skin's, any case)
    /// and sex, by material name. An incubation takes the look of its own sex first; one that does not depend on sex (no mask
    /// from a sex's own map) also goes to the other sex. Everything handed in or out is a copy.
    /// </summary>
    public sealed class PendingLooks
    {
        private sealed class Entry
        {
            public Entry(string species, string? skin, string? sex)
            {
                Species = species;
                Skin = skin;
                Sex = sex;
            }

            public string Species { get; }
            public string? Skin { get; }
            public string? Sex { get; }
            public Dictionary<string, MaterialOverride> Materials { get; } = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
        }

        private readonly List<Entry> _entries = new List<Entry>();

        public int Count => _entries.Count;
        public bool IsEmpty => _entries.Count == 0;

        /// <summary>Sets one material of the look for this species, skin and sex (a later save replaces it); null or an empty block removes it.</summary>
        public void Set(string species, string? skin, string? sex, string materialName, MaterialOverride? block)
        {
            var entry = Find(species, skin, sex, exact: true);
            if (block == null || block.IsEmpty)
            {
                if (entry == null) return;
                entry.Materials.Remove(materialName);
                if (entry.Materials.Count == 0) _entries.Remove(entry);
                return;
            }
            if (entry == null) _entries.Add(entry = new Entry(species, skin, sex));
            entry.Materials[materialName] = Copy(block);
        }

        /// <summary>The look kept for exactly this species, skin and sex (what a Next created draft starts from); null when none.</summary>
        public Dictionary<string, MaterialOverride>? Get(string species, string? skin, string? sex) =>
            Find(species, skin, sex, exact: true) is Entry entry ? CopyAll(entry.Materials) : null;

        /// <summary>
        /// The look for a new animal of this species, skin and sex, removed from the store: the one kept for its sex, else one of
        /// the same species and skin that does not depend on sex. Null when none matches.
        /// </summary>
        public Dictionary<string, MaterialOverride>? Take(string species, string? skin, string? sex)
        {
            var entry = Find(species, skin, sex, exact: true) ?? Find(species, skin, sex, exact: false);
            if (entry == null) return null;
            _entries.Remove(entry);
            return CopyAll(entry.Materials);
        }

        /// <summary>Drops every look (the park was left: they are never saved).</summary>
        public void Clear() => _entries.Clear();

        /// <summary>True when a block takes its mask from a skin map, which each sex has its own of.</summary>
        public static bool DependsOnSex(IReadOnlyDictionary<string, MaterialOverride> materials) =>
            materials.Values.Any(m => m.Iridescence?.Mask?.From != null);

        private Entry? Find(string species, string? skin, string? sex, bool exact) =>
            _entries.FirstOrDefault(e => string.Equals(e.Species, species, StringComparison.Ordinal)
                && string.Equals(e.Skin, skin, StringComparison.OrdinalIgnoreCase)
                && (exact ? string.Equals(e.Sex, sex, StringComparison.Ordinal) : !DependsOnSex(e.Materials)));

        private static MaterialOverride Copy(MaterialOverride block) => MaterialTable.WithPaths(block, file => file);

        private static Dictionary<string, MaterialOverride> CopyAll(Dictionary<string, MaterialOverride> materials) =>
            materials.ToDictionary(pair => pair.Key, pair => Copy(pair.Value), StringComparer.Ordinal);
    }
}
