using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Mods;

/// <summary>A species' prefab settings fields as the game has them, read once per game build and cached in the workspace.</summary>
public static class PrefabFields
{
    private const int CacheVersion = 1;

    /// <param name="species">Must already include the mod's own species (<see cref="ModProject.WithSpecies"/>).</param>
    public static IReadOnlyList<PrefabFieldInfo> Read(Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string target)
    {
        var (_, prefab) = ModProject.ResolveModelTarget(index, species, target, null);
        var key = TextureExporter.Sanitize(prefab.Guid ?? prefab.Bundle + "_" + prefab.PathId) + "-" + WorkspaceCache.Key(ws, install, prefab, CacheVersion);
        var cache = Path.Combine(ws.CacheDir, "prefab-fields", key + ".json");
        if (File.Exists(cache))
        {
            try
            {
                return JsonSerializer.Deserialize<List<PrefabFieldInfo>>(File.ReadAllText(cache)) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // a damaged cache is read again from the game
            }
        }
        var components = reader.ReadPrefabComponents(install, prefab);
        using var enums = new PrefabEnumNames(install.ManagedDir);
        var tree = PrefabFieldTree.Build(components, enums.For);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllText(cache, JsonSerializer.Serialize(tree));
        return tree;
    }
}
