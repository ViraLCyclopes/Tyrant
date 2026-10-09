using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The game's list of all animals (the Nursery's Alphabet sort) is ordered by species id. A custom species' id starts
    /// "tyrant.", which would put it after every game species; it sorts by its scientific name instead, among the game's
    /// species of that name (a Giganotosaurus based on Carcharodontosaurus sits with the G's).
    /// </summary>
    public static class NurseryOrder
    {
        public static string Key(string speciesId, IReadOnlyDictionary<string, string> customNames) =>
            customNames.TryGetValue(speciesId, out var name) ? name : speciesId;

        /// <summary>Ordinal, like the game's own sort, so the game's species keep their order.</summary>
        public static int Compare(string a, string b, IReadOnlyDictionary<string, string> customNames) =>
            string.CompareOrdinal(Key(a, customNames), Key(b, customNames));
    }
}
