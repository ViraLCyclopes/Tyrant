using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>The game species a custom species ("tyrant.&lt;mod&gt;.&lt;id&gt;") copies: its template, whose sounds it plays.</summary>
public static class CustomSpeciesTemplate
{
    /// <summary>The template of one of this mod's species or another mod's; null for a game species or one not found.</summary>
    public static string? Of(string species, ModManifest? mod, IEnumerable<OtherSpecies>? others)
    {
        if (SpeciesUse.ModOf(species) is null) return null;
        if (mod?.Species.FirstOrDefault(e => e.Key(mod.Id) == species) is { } own) return own.Base;
        return others?.FirstOrDefault(o => o.Key == species)?.Entry.Base;
    }

    /// <summary>As above, from every mod Tyrant can see (the workspace's and the game's); they are read only for a custom species id.</summary>
    public static string? Of(Workspace ws, Func<IReadOnlyList<InstalledMod>> installed, string species) =>
        SpeciesUse.ModOf(species) is null ? null : Of(species, null, OtherModSpecies.Load(ws, installed(), null).Species);
}
