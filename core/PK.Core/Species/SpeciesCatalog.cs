using PK.Core.Assets;
using PK.Core.Errors;

namespace PK.Core.Species;

/// <summary>An animal the game ships: its prefab and the asset group holding its skins.</summary>
public sealed record SpeciesEntry(string Key, string DisplayName, bool Vivarium, AssetRecord Prefab, string Group);

/// <summary>Finds species in the asset index by their prefab locations.</summary>
public static class SpeciesCatalog
{
    private const string MainPrefix = "Assets/Prefabs/Animals/V2-MainPrefabs/";
    private const string VivariumPrefix = "Assets/Prefabs/Animals/Vivarium/";

    public static IReadOnlyList<SpeciesEntry> FromIndex(AssetIndex index) =>
        index.Assets
            .Where(a => a.Type == "GameObject" && a.ContainerPath is { } path
                        && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                        && (path.StartsWith(MainPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(VivariumPrefix, StringComparison.OrdinalIgnoreCase)))
            .Select(a =>
            {
                var path = a.ContainerPath!;
                var file = path[(path.LastIndexOf('/') + 1)..];
                var display = file.EndsWith(".V2.prefab", StringComparison.OrdinalIgnoreCase) ? file[..^".V2.prefab".Length] : file[..^".prefab".Length];
                return new SpeciesEntry(KeyOf(display), display, path.StartsWith(VivariumPrefix, StringComparison.OrdinalIgnoreCase), a, BundleGroup(a.Bundle));
            })
            .GroupBy(e => e.Key)
            .Select(g => g.First())
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>"Allosaurus Anax" → "allosaurusanax".</summary>
    public static string KeyOf(string name)
    {
        var key = new string(name.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return key.Length > 0 ? key : "species";
    }

    public static SpeciesEntry Find(IReadOnlyList<SpeciesEntry> all, string query)
    {
        var key = KeyOf(query);
        var exact = all.FirstOrDefault(e => e.Key == key);
        if (exact is not null) return exact;
        var partial = all.Where(e => e.Key.Contains(key, StringComparison.Ordinal)).ToList();
        return partial.Count switch
        {
            1 => partial[0],
            0 => throw new PkException(PkErrorCode.AssetNotFound, $"No species matches '{query}'. Use 'pk species list' to see them all."),
            _ => throw new PkException(PkErrorCode.AssetAmbiguous,
                $"'{query}' matches several species: {string.Join(", ", partial.Select(e => e.Key))}."),
        };
    }

    /// <summary>The asset group a bundle belongs to: path up to the first "*_assets_assets" folder or "*.bundle" file.</summary>
    public static string BundleGroup(string bundle)
    {
        var parts = bundle.Split('/');
        for (var i = 0; i < parts.Length; i++)
            if (parts[i].EndsWith("_assets_assets", StringComparison.OrdinalIgnoreCase) || parts[i].EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                return string.Join('/', parts[..(i + 1)]);
        return bundle;
    }

    /// <summary>Textures in the species' asset group (related species, e.g. two Stegosaurus, share one group).</summary>
    public static IReadOnlyList<AssetRecord> TexturesFor(AssetIndex index, SpeciesEntry species) =>
        index.Assets.Where(a => a.Type == "Texture2D" && string.Equals(BundleGroup(a.Bundle), species.Group, StringComparison.OrdinalIgnoreCase)).ToList();
}
