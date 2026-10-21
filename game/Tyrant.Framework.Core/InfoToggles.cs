using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The editor's info buttons: which row's explanation is open on each tab. At most one is open per tab; pressing the open
    /// one closes it, pressing another moves it there. Kept across redraws (the window rebuilds its rows on every choice).
    /// </summary>
    public sealed class InfoToggles
    {
        private readonly Dictionary<string, string> _open = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool IsOpen(string tab, string row) => _open.TryGetValue(tab, out var open) && open == row;

        /// <summary>The row open on this tab, or null.</summary>
        public string? Open(string tab) => _open.TryGetValue(tab, out var open) ? open : null;

        public void Toggle(string tab, string row)
        {
            if (IsOpen(tab, row)) _open.Remove(tab);
            else _open[tab] = row;
        }
    }
}
