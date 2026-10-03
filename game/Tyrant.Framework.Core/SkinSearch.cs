using System;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>The Nursery skin browser's search: every word of the query must appear in the skin's name or its mod's name.</summary>
    public static class SkinSearch
    {
        public static bool Matches(string? query, string skinName, string? modName)
        {
            var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => Contains(skinName, w) || (modName != null && Contains(modName, w)));
        }

        private static bool Contains(string text, string word) => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
