using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>A distinct regular animal, inheriting a vanilla animal's rig, growth, animations and behavior.</summary>
    public sealed class SpeciesEntry
    {
        public string Id { get; set; } = "";
        public string Base { get; set; } = "";
        public string Name { get; set; } = "";
        public string Genus { get; set; } = "";
        public string Epithet { get; set; } = "";
        public int? Cost { get; set; }
        public Dictionary<string, double> Stats { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);
        public List<AnimalField> Fields { get; set; } = new List<AnimalField>();
        public List<AnimalText> Texts { get; set; } = new List<AnimalText>();
        public Dictionary<string, string> Localization { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public string? Icon { get; set; }
        public SpeciesPrefab? Prefab { get; set; }

        /// <summary>Stable save identity, independent of display names and mod load order.</summary>
        public string Key(string modId) => "tyrant." + modId + "." + Id;

        internal Dictionary<string, object?> ToMap()
        {
            var map = new Dictionary<string, object?>
            {
                ["id"] = Id, ["base"] = Base, ["name"] = Name, ["genus"] = Genus, ["epithet"] = Epithet,
            };
            if (Cost.HasValue) map["cost"] = Cost.Value;
            if (Stats.Count > 0) map["stats"] = Stats.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => (object?)p.Value);
            if (Fields.Count > 0) map["fields"] = Fields.OrderBy(p => p.Path, StringComparer.Ordinal).Select(f => (object?)f.ToMap()).ToList();
            if (Localization.Count > 0) map["localization"] = Localization.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => (object?)p.Value);
            if (Icon != null) map["icon"] = Icon;
            if (Prefab != null) map["prefab"] = new Dictionary<string, object?> { ["bundle"] = Prefab.Bundle, ["asset"] = Prefab.Asset };
            return map;
        }
    }

}
