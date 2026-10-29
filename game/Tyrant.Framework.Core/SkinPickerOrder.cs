using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The order of a species' skins in the Nursery skin picker (mod.json "skinOrder"). Display only: skin numbers never change.
    /// </summary>
    public static class SkinPickerOrder
    {
        /// <summary>
        /// The ids to show, in order: the saved ids that still exist (the starting skin or one of <paramref name="otherIds"/>,
        /// first occurrence), then the rest - the starting skin first, then numbered skins by number, then the others in file order.
        /// </summary>
        public static List<string> Shown(IReadOnlyList<string>? saved, string startingId, IReadOnlyList<string> otherIds,
            IReadOnlyDictionary<string, int>? numbers)
        {
            var known = new HashSet<string>(otherIds, StringComparer.Ordinal) { startingId };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            if (saved != null)
                foreach (var id in saved)
                    if (known.Contains(id) && seen.Add(id)) result.Add(id);

            if (seen.Add(startingId)) result.Add(startingId);
            var rest = otherIds.Where(id => !seen.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
            bool Numbered(string id) => numbers != null && numbers.ContainsKey(id);
            result.AddRange(rest.Where(Numbered).OrderBy(id => numbers![id]));
            result.AddRange(rest.Where(id => !Numbered(id)));
            return result;
        }

        /// <summary>A copy of <paramref name="order"/> with the places of <paramref name="a"/> and <paramref name="b"/> exchanged; unchanged if either is missing.</summary>
        public static List<string> Swapped(IReadOnlyList<string> order, string a, string b)
        {
            var result = order.ToList();
            int i = result.IndexOf(a), j = result.IndexOf(b);
            if (i < 0 || j < 0) return result;
            result[i] = b;
            result[j] = a;
            return result;
        }

        /// <summary>The wanted indices (in 0..count-1, first occurrence), then the rest ascending.</summary>
        public static int[] Display(int count, IEnumerable<int> wanted)
        {
            var seen = new HashSet<int>();
            var result = new List<int>();
            foreach (var index in wanted)
                if (index >= 0 && index < count && seen.Add(index)) result.Add(index);
            for (int i = 0; i < count; i++)
                if (seen.Add(i)) result.Add(i);
            return result.ToArray();
        }

        /// <summary>
        /// The toggle moves that put skin <c>display[k]</c> in the place of slot k. <paramref name="siblings"/>[i] is the current
        /// sibling index of skin i's toggle (slot k holds skin k on screen); the result is sorted by sibling index, ascending,
        /// for applying <c>SetSiblingIndex</c> in that order.
        /// </summary>
        public static List<(int Skin, int Sibling)> Moves(IReadOnlyList<int> siblings, IReadOnlyList<int> display)
        {
            var moves = new List<(int Skin, int Sibling)>();
            for (int k = 0; k < display.Count && k < siblings.Count; k++)
                moves.Add((display[k], siblings[k]));
            return moves.OrderBy(m => m.Sibling).ToList();
        }
    }
}
