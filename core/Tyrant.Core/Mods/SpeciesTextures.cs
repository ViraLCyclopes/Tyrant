using Tyrant.Core.Assets;

namespace Tyrant.Core.Mods;

/// <summary>A texture a species' skins use: what it is (adult colour, baby normal…), which skins, and which other species use it too.</summary>
public sealed record SpeciesTexture(string Texture, string Slot, IReadOnlyList<string> Skins, IReadOnlyList<string> SharedWith);

/// <summary>The textures of a species' vanilla skins (for replacing them in a mod), from the data dump and the asset index.</summary>
public static class SpeciesTextures
{
    private static readonly string[] Order = ["diffuse", "normal", "extra", "pattern", "fur", "infantDiffuse", "infantNormal", "infantExtra", "infantPattern", "infantFur"];

    /// <summary>The species (by id in any case, or its asset key), each texture once: adult maps first, then baby ones.</summary>
    public static IReadOnlyList<SpeciesTexture> For(IReadOnlyList<SpeciesSkins> species, AssetIndex index, string speciesIdOrKey)
    {
        var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, speciesIdOrKey, StringComparison.OrdinalIgnoreCase))
            ?? species.FirstOrDefault(s => Key(s.SpeciesId) == Key(speciesIdOrKey));
        if (target is null) return [];
        var names = index.Assets.Where(a => a.Type == "Texture2D" && a.Guid is { Length: > 0 })
            .GroupBy(a => a.Guid!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        // Every species using each texture, for "shared with".
        var users = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var s in species)
            foreach (var guid in s.Skins.SelectMany(k => k.Male.Values.Concat(k.Female.Values)))
                if (names.TryGetValue(guid, out var name))
                {
                    if (!users.TryGetValue(name, out var set)) users[name] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    set.Add(s.SpeciesId);
                }

        var found = new Dictionary<string, (int Rank, string Slot, SortedSet<string> Skins)>(StringComparer.Ordinal);
        foreach (var skin in target.Skins)
            foreach (var (slot, guid) in skin.Male.Concat(skin.Female))
            {
                var rank = Array.IndexOf(Order, slot);
                if (rank < 0 || !names.TryGetValue(guid, out var name)) continue;
                if (!found.TryGetValue(name, out var entry) || rank < entry.Rank)
                    found[name] = entry = (rank, Label(slot), entry.Skins ?? new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
                entry.Skins.Add(skin.Name);
            }

        return found.OrderBy(f => f.Value.Rank).ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .Select(f => new SpeciesTexture(f.Key, f.Value.Slot, f.Value.Skins.ToList(),
                users.TryGetValue(f.Key, out var u) ? u.Where(s => s != target.SpeciesId).ToList() : []))
            .ToList();
    }

    /// <summary>"infantNormal" → "baby normal", "diffuse" → "adult colour".</summary>
    public static string Label(string slot)
    {
        var baby = slot.StartsWith("infant", StringComparison.Ordinal);
        var kind = (baby ? slot["infant".Length..] : slot).ToLowerInvariant() switch
        {
            "diffuse" => "colour",
            var other => other,
        };
        return $"{(baby ? "baby" : "adult")} {kind}";
    }

    private static string Key(string id) => new(id.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
}
