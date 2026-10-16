using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The rules for mod.json "starting" skins: only on this mod's own custom species, one per species (the first in file
    /// order), with base "0". Other skins on that species with base 0 (or the starting skin's name) inherit its PNGs.
    /// </summary>
    public static class StartingSkins
    {
        /// <summary>Starting flags that break the rules, in file order; manifest.Species must already be loaded.</summary>
        public static IReadOnlyList<(SkinEntry Skin, string Problem)> Problems(ModManifest manifest)
        {
            var problems = new List<(SkinEntry, string)>();
            var taken = new Dictionary<string, SkinEntry>(StringComparer.Ordinal);
            foreach (var skin in manifest.Skins.Where(s => s.Starting))
            {
                if (!manifest.Species.Any(s => s.Key(manifest.Id) == skin.Species))
                    problems.Add((skin, $"\"{skin.Species}\" is not one of this mod's species."));
                else if (skin.Base != "0")
                    problems.Add((skin, $"a starting skin's base must be 0 (got \"{skin.Base}\")."));
                else if (taken.TryGetValue(skin.Species, out var first))
                    problems.Add((skin, $"{skin.Species} already has a starting skin (\"{first.Id}\")."));
                else taken[skin.Species] = skin;
            }
            return problems;
        }

        /// <summary>The species' valid starting skin in this mod, or null.</summary>
        public static SkinEntry? Of(ModManifest manifest, string speciesKey)
        {
            var bad = new HashSet<SkinEntry>(Problems(manifest).Select(p => p.Skin));
            return manifest.Skins.FirstOrDefault(s => s.Starting && s.Species == speciesKey && !bad.Contains(s));
        }

        /// <summary>True when the skin is based on its species' starting skin (base "0", or that skin's name, any case).</summary>
        public static bool InheritsFrom(ModManifest manifest, SkinEntry skin, out SkinEntry starting)
        {
            starting = null!;
            if (skin.Starting) return false;
            var found = Of(manifest, skin.Species);
            if (found == null || !(skin.Base == "0" || string.Equals(skin.Base, found.Name, StringComparison.OrdinalIgnoreCase))) return false;
            starting = found;
            return true;
        }
    }
}
