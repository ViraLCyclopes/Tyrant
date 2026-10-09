using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed record SpeciesUseCount(string ModId, int Skins);

/// <summary>Which other mods' custom species a mod's skins are for ("tyrant.&lt;mod&gt;.&lt;species&gt;").</summary>
public static class SpeciesUse
{
    /// <summary>The mod id in a custom species id, or null for a game species.</summary>
    public static string? ModOf(string species)
    {
        var parts = species.Split('.');
        return parts.Length == 3 && parts[0] == "tyrant" && ModId.IsValid(parts[1]) && ModId.IsValid(parts[2]) ? parts[1] : null;
    }

    public static IReadOnlyList<SpeciesUseCount> Of(ModManifest manifest) =>
        manifest.Skins.Select(s => ModOf(s.Species)).OfType<string>().Where(m => m != manifest.Id)
            .GroupBy(m => m, StringComparer.Ordinal).Select(g => new SpeciesUseCount(g.Key, g.Count()))
            .OrderBy(u => u.ModId, StringComparer.Ordinal).ToList();
}
