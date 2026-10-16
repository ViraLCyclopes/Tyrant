using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>One portrait as the game will show it: the mod's own file, else the base skin's game texture; null when neither exists.</summary>
public sealed record ShownPortrait(string Slot, string? Source, bool Own, bool Rendered, bool Inherited = false);

/// <summary>What each of a skin's 12 portraits will show in game ("file:&lt;path&gt;", "guid:&lt;texture guid&gt;", or null).</summary>
public static class ShownPortraits
{
    /// <summary>A skin of this mod: its own, then (on this mod's custom species) the species' starting skin's, then the base skin's.</summary>
    public static IReadOnlyList<ShownPortrait> ForSkin(ModProject mod, SkinEntry skin, VanillaSkin? based)
    {
        var species = mod.Manifest.Species.FirstOrDefault(s => s.Key(mod.Id) == skin.Species);
        IReadOnlyDictionary<string, string>? inherited = species?.Portraits;
        // Based on the starting skin: its portraits, laid over the species' (as the game registers skin 0's).
        if (StartingSkins.InheritsFrom(mod.Manifest, skin, out var starting) && starting.Portraits is { Count: > 0 } startPortraits)
        {
            var merged = new Dictionary<string, string>(inherited ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            foreach (var (slot, file) in startPortraits) merged[slot] = file;
            inherited = merged;
        }
        return Rows(mod, skin.Portraits, inherited, based);
    }

    /// <summary>A custom species' starting skin: its own, then the template skin's.</summary>
    public static IReadOnlyList<ShownPortrait> ForSpecies(ModProject mod, SpeciesEntry entry, VanillaSkin? start) => Rows(mod, entry.Portraits, null, start);

    private static IReadOnlyList<ShownPortrait> Rows(ModProject mod, IReadOnlyDictionary<string, string>? own, IReadOnlyDictionary<string, string>? inherited, VanillaSkin? based) =>
        PortraitSlots.All.Select(slot =>
        {
            if (own is not null && own.TryGetValue(slot, out var file)) return new ShownPortrait(slot, "file:" + Path.Combine(mod.Dir, file), true, PortraitSlots.IsRendered(file));
            if (inherited is not null && inherited.TryGetValue(slot, out var parent)) return new ShownPortrait(slot, "file:" + Path.Combine(mod.Dir, parent), false, false, Inherited: true);
            return new ShownPortrait(slot, based is not null && based.Portraits.TryGetValue(slot, out var guid) ? "guid:" + guid : null, false, false);
        }).ToList();
}
