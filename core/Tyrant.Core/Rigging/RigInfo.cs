using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Rigging;

/// <summary>
/// What a species' skeleton does in game, for rig editing's warnings: its bones, the bones its animations move (not only
/// hold), and the bones its growth positions or scales. Failures says what could not be read (the lists are then partial).
/// </summary>
public sealed record RigInfo(IReadOnlyList<string> Bones, IReadOnlyList<string> ClipMoved, IReadOnlyList<string> GrowthMoved,
    IReadOnlyList<string> GrowthScaled, IReadOnlyList<string> Failures);

public static class RigInfoService
{
    private const int CacheVersion = 1;

    private static readonly JsonSerializerOptions Json = new(DataStore.ReadableJson) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The species' rig info, worked out once per game version and data dump (cached in the workspace).</summary>
    public static RigInfo For(Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string speciesId)
    {
        var (id, prefab) = ModProject.ResolveModelTarget(index, species, speciesId, null);
        var cache = Path.Combine(ws.CacheDir, "rig-info", $"{TextureExporter.Sanitize(id)}-{WorkspaceCache.Key(ws, install, prefab, CacheVersion)}.json");
        if (File.Exists(cache))
        {
            try
            {
                if (JsonSerializer.Deserialize<RigInfo>(File.ReadAllText(cache), Json) is { } known) return known;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // worked out again below
            }
        }

        var model = reader.ReadPrefabModel(install, prefab);
        var names = model.Root.DepthFirst().Select(n => n.Name).ToList();
        var inSkeleton = names.ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();
        var store = BlenderGrowthReader.TryStore(ws);
        var clipNames = store is null ? [] : ClipReader.ClipNames(store, id);
        IReadOnlyList<string> clipMoved = [];
        if (clipNames.Count == 0)
            failures.Add("The game's animations for this species were not found in the data dump (run the data dump again), so the bones they move are not listed.");
        else
        {
            var wanted = clipNames.ToHashSet(StringComparer.Ordinal);
            var records = index.Assets.Where(a => a.Type == "AnimationClip" && wanted.Contains(a.Name))
                .GroupBy(a => a.Name, StringComparer.Ordinal).Select(g => g.First()).ToList();
            var (clips, clipFailures) = reader.ReadClips(install, records);
            failures.AddRange(clipFailures);
            clipMoved = ClipReader.MovedBones(clips, model.Root);
        }
        var growth = BlenderGrowthReader.Read(store, id, names);
        var info = new RigInfo(names, clipMoved,
            growth?.Bones.Where(b => b.Translation && inSkeleton.Contains(b.Name)).Select(b => b.Name).ToList() ?? [],
            growth?.Bones.Where(b => b.Scale && inSkeleton.Contains(b.Name)).Select(b => b.Name).ToList() ?? [],
            failures);
        if (failures.Count == 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            File.WriteAllText(cache, JsonSerializer.Serialize(info, Json));
        }
        return info;
    }

    /// <summary>The data dump's species id for a name typed by the user: an exact id, else the species the catalog finds.</summary>
    public static string SpeciesIdOf(string name, IReadOnlyList<SpeciesSkins> species, IReadOnlyList<Species.SpeciesEntry> catalog)
    {
        var exact = species.FirstOrDefault(s => string.Equals(s.SpeciesId, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.SpeciesId;
        var entry = Species.SpeciesCatalog.Find(catalog, name);
        return species.FirstOrDefault(s => string.Equals(s.PrefabGuid, entry.Prefab.Guid, StringComparison.OrdinalIgnoreCase))?.SpeciesId
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"{entry.DisplayName} is not in the game data; run the data dump again (Workspace → Run data dump, or 'tyrant dump run').");
    }
}
