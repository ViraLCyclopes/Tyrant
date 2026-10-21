using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The check after a new species' Open Paleopedia: is the page's header the species' genus and species, who last set the
    /// page (a short call site of game methods), and which cases were already logged.
    /// </summary>
    public static class PaleopediaCheck
    {
        private static readonly Regex Tags = new Regex("<[^>]*>");
        private static readonly Regex Patched = new Regex(@"^DMD<(?<type>[^:>]+)::(?<method>[^>]+)>");

        private static string Clean(string? text) => Tags.Replace(text ?? "", "").Trim();

        /// <summary>The header's genus and species text against the species' own (case and rich text tags aside).</summary>
        public static bool ShowsSpecies(string? shownGenus, string? shownSpecies, string genus, string species)
        {
            var (g, s) = (Clean(shownGenus), Clean(shownSpecies));
            return g.Length > 0 && string.Equals(g, genus.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(s, species.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The header as a name for the log, e.g. "Allosaurus anax".</summary>
        public static string Shown(string? genus, string? species)
        {
            var name = (Clean(genus) + " " + Clean(species)).Trim();
            return name.Length == 0 ? "(empty header)" : name;
        }

        /// <summary>
        /// Up to <paramref name="max"/> game methods (PrehistoricKingdom.*) from a stack, innermost first, as "Type.Method &lt; …".
        /// Mod and engine frames and the recorded method itself are left out; a Harmony-patched game method shows under its own name.
        /// </summary>
        public static string CallSite(IEnumerable<(string? Type, string Method)> frames, string skipMethod, int max = 3)
        {
            var names = new List<string>();
            foreach (var (type, method) in frames)
            {
                var (t, m) = (type, method);
                var patched = Patched.Match(method ?? "");
                if (t == null && patched.Success) (t, m) = (patched.Groups["type"].Value, patched.Groups["method"].Value);
                if (t == null || !t.StartsWith("PrehistoricKingdom.", StringComparison.Ordinal) || m == skipMethod) continue;
                var name = t.Substring(t.LastIndexOf('.') + 1) + "." + m;
                if (names.Count > 0 && names[names.Count - 1] == name) continue;
                names.Add(name);
                if (names.Count == max) break;
            }
            return names.Count == 0 ? "no game method" : string.Join(" < ", names);
        }

        /// <summary>Remembers which (case, species) pairs were logged, so a case is logged once per species.</summary>
        public sealed class OncePerCase
        {
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

            public bool First(string kind, string species) => _seen.Add(kind + "\n" + species);
        }
    }
}
