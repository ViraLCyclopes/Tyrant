using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    public sealed class ModListEntry
    {
        public ModListEntry(string id, bool enabled)
        {
            Id = id;
            Enabled = enabled;
        }

        public string Id { get; }
        public bool Enabled { get; }
    }

    /// <summary>UserData/Tyrant/mods.json: which installed mods are on, in load order.</summary>
    public static class ModList
    {
        public const string FileName = "mods.json";

        /// <summary>The entries in order; empty for null. Throws FormatException for content that is not a mods list.</summary>
        public static List<ModListEntry> Parse(string? json)
        {
            var entries = new List<ModListEntry>();
            if (json == null) return entries;
            if (!(Json.Parse(json) is Dictionary<string, object?> root) || !(root.TryGetValue("mods", out var mods) && mods is List<object?> list))
                throw new FormatException("expected { \"format\": 1, \"mods\": [ ... ] }");
            foreach (var item in list)
            {
                if (!(item is Dictionary<string, object?> entry) || !(entry.TryGetValue("id", out var id) && id is string text))
                    throw new FormatException("every entry needs an \"id\"");
                var enabled = !entry.TryGetValue("enabled", out var on) || !(on is bool b) || b;
                if (entries.All(e => e.Id != text)) entries.Add(new ModListEntry(text, enabled));
            }
            return entries;
        }

        public static string ToJson(IEnumerable<ModListEntry> entries) =>
            Json.Write(new Dictionary<string, object?>
            {
                ["format"] = 1,
                ["mods"] = entries.Select(e => (object?)new Dictionary<string, object?> { ["id"] = e.Id, ["enabled"] = e.Enabled }).ToList(),
            }) + "\n";
    }
}
