using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>One skin's place in the mod editor's list: the order of mod.json's skins, and its group (null: none).</summary>
public sealed record SkinPlace(string Id, string? Group);

/// <summary>Ordering and grouping a mod's skins (Tyrant only: the game numbers skins by id and ignores both).</summary>
public sealed partial class ModProject
{
    /// <summary>
    /// Puts the skins in this order and these groups (the editor's move, group, rename-group and ungroup actions). Ungrouped skins
    /// come first; each group's skins stay together where its first skin is; a name matching a group whatever its case joins it.
    /// </summary>
    public void ArrangeSkins(IReadOnlyList<SkinPlace> order)
    {
        var current = Manifest.Skins.ToDictionary(s => s.Id, StringComparer.Ordinal);
        if (order.Count != current.Count || order.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != order.Count
            || order.Any(p => !current.ContainsKey(p.Id)))
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                $"That order does not name each skin of '{Id}' once ({string.Join(", ", current.Keys)}); reload the mod and try again.");
        foreach (var place in order) current[place.Id].Group = SkinEntry.GroupName(place.Group);
        var arranged = Arranged(order.Select(p => current[p.Id]));
        Manifest.Skins.Clear();
        Manifest.Skins.AddRange(arranged);
        Save();
    }

    /// <summary>Ungrouped skins first, then each group (spelled as its first skin has it) in order of its first skin.</summary>
    internal static List<SkinEntry> Arranged(IEnumerable<SkinEntry> skins)
    {
        var ungrouped = new List<SkinEntry>();
        var groups = new List<(string Name, List<SkinEntry> Skins)>();
        foreach (var skin in skins)
        {
            if (skin.Group is null)
            {
                ungrouped.Add(skin);
                continue;
            }
            var at = groups.FindIndex(g => string.Equals(g.Name, skin.Group, StringComparison.OrdinalIgnoreCase));
            if (at < 0)
            {
                groups.Add((skin.Group, []));
                at = groups.Count - 1;
            }
            skin.Group = groups[at].Name;
            groups[at].Skins.Add(skin);
        }
        return [.. ungrouped, .. groups.SelectMany(g => g.Skins)];
    }
}
