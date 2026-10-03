using Tyrant.Core.Assets;
using Tyrant.Core.Species;

namespace Tyrant.Rpc.Assets;

internal static class PreviewSkins
{
    /// <summary>The diffuse textures (names ending "_D", portraits excluded) of the prefab's species group, by name.</summary>
    public static IReadOnlyList<AssetRecord> Candidates(AssetIndex index, AssetRecord prefab)
    {
        var group = SpeciesCatalog.BundleGroup(prefab.Bundle);
        return index.Assets
            .Where(a => a.Type == "Texture2D"
                        && a.Name.EndsWith("_D", StringComparison.OrdinalIgnoreCase)
                        && !a.Name.StartsWith("port_", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(SpeciesCatalog.BundleGroup(a.Bundle), group, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Ref, StringComparer.Ordinal)
            .ToList();
    }
}
