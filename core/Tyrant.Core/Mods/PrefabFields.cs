using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Mods;

/// <summary>A species' prefab settings fields as the game has them, read once per game build and cached in the workspace.</summary>
public static class PrefabFields
{
    private const int CacheVersion = 2;

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
                if (JsonSerializer.Deserialize<List<PrefabFieldInfo>>(File.ReadAllText(cache)) is { } cached) return cached;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // a damaged cache is read again from the game
            }
        }
        var components = reader.ReadPrefabComponents(install, prefab);
        using var enums = new PrefabEnumNames(install.ManagedDir);
        var tree = PrefabFieldTree.Build(components, enums.For);
        var temp = cache + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(tree));
            File.Move(temp, cache, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the cache is only a speed-up
            try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return tree;
    }
}
