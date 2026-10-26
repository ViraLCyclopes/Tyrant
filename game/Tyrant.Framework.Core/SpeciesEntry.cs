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
        /// <summary>The Nursery picture (the game's heroRender: the Nursery card and species panel), a PNG in the mod; null keeps the template's.</summary>
        public string? Icon { get; set; }
        /// <summary>The species' Paleopedia page picture (its own copy of the template's info sign), a PNG in the mod; null keeps the template's.</summary>
        public string? PaleopediaPicture { get; set; }
        public SpeciesPrefab? Prefab { get; set; }
        /// <summary>The template skin (its index in the template's skinsData) the species starts with: its only skin before mods add theirs.</summary>
        public int StartingSkin { get; set; }
        /// <summary>The starting skin's name in game; null keeps the template skin's name.</summary>
        public string? StartingSkinName { get; set; }
        /// <summary>The starting skin's swatch (skin picker thumbnail), a PNG path inside the mod; null keeps the template's.</summary>
        public string? Swatch { get; set; }
        /// <summary>The starting skin's portraits: slot (PortraitSlots.All) → PNG in the mod; a slot left out keeps the template's.</summary>
        public Dictionary<string, string> Portraits { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The combat behaviour (starting profile and rules); null keeps the template's profile.</summary>
        public SpeciesBehaviour? Behaviour { get; set; }

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
            if (PaleopediaPicture != null) map["paleopediaPicture"] = PaleopediaPicture;
            if (Prefab != null) map["prefab"] = new Dictionary<string, object?> { ["bundle"] = Prefab.Bundle, ["asset"] = Prefab.Asset };
            if (StartingSkin != 0) map["startingSkin"] = StartingSkin;
            if (StartingSkinName != null) map["startingSkinName"] = StartingSkinName;
            if (Swatch != null) map["swatch"] = Swatch;
            if (Portraits.Count > 0) map["portraits"] = PortraitSlots.All.Where(Portraits.ContainsKey).ToDictionary(k => k, k => (object?)Portraits[k]);
            if (Behaviour != null) map["behaviour"] = Behaviour.ToMap();
            return map;
        }
    }

}
