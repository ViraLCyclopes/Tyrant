using System;
using System.Collections.Generic;
using System.IO;

namespace Tyrant.Framework.Core
{
    /// <summary>The game's skin objects (AnimalSkinData) Tyrant made, with the portraits each shows: slot → full PNG path.</summary>
    public sealed class PortraitTable
    {
        private readonly Dictionary<object, Dictionary<string, string>> _skins = new Dictionary<object, Dictionary<string, string>>(ReferenceComparer.Instance);

        public bool Any => _skins.Count > 0;

        /// <summary>
        /// A skin's portraits (paths relative to its mod), over those of the skin it was copied from when that one has some.
        /// Returns the slots skipped because their path is unusable or outside the mod.
        /// </summary>
        public IReadOnlyList<string> Register(object skin, string modDirectory, IReadOnlyDictionary<string, string>? portraits, object? inheritFrom = null)
        {
            var map = inheritFrom != null && _skins.TryGetValue(inheritFrom, out var inherited)
                ? new Dictionary<string, string>(inherited, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            var skipped = new List<string>();
            foreach (var pair in portraits ?? new Dictionary<string, string>())
            {
                if (!PortraitSlots.IsSlot(pair.Key)) continue;
                try
                {
                    var path = Path.GetFullPath(Path.Combine(modDirectory, pair.Value));
                    if (ModPaths.IsInside(path, modDirectory)) map[pair.Key] = path;
                    else skipped.Add(pair.Key);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
                {
                    skipped.Add(pair.Key);
                }
            }
            if (map.Count > 0) _skins[skin] = map;
            else _skins.Remove(skin);
            return skipped;
        }

        public string? PathFor(object skin, string slot) => _skins.TryGetValue(skin, out var map) && map.TryGetValue(slot, out var path) ? path : null;
    }
}
