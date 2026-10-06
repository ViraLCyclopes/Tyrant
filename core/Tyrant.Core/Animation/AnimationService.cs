using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Animation;

/// <summary>
/// A species' animations, from its animation table in the data dump: the list (no keys) and clips sampled into per-bone keys,
/// both worked out once per game build and data dump and kept in the workspace's cache.
/// </summary>
public static class AnimationService
{
    private const int CacheVersion = 1;

    private static readonly JsonSerializerOptions Json = new(DataStore.ReadableJson) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyList<AnimationInfo> List(Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species,
        IAssetReader reader, string speciesId)
    {
        var (id, prefab, dir) = Where(ws, install, index, species, speciesId);
        var file = Path.Combine(dir, "list.json");
        if (Load<List<AnimationInfo>>(file) is { } known) return known;
        var records = Records(ws, index, id);
        var model = reader.ReadPrefabModel(install, prefab);
        var (clips, _) = reader.ReadRawClips(install, records.Values.ToList());
        var byName = clips.GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var list = records.Keys.Where(byName.ContainsKey).Select(name => ClipSampler.Info(byName[name], model.Root)).ToList();
        Save(file, list);
        return list;
    }

    /// <summary>Each asked clip sampled (from the cache when it was before), or why it could not be.</summary>
    public static IReadOnlyList<(string Id, ClipAnimation? Clip, string? Failure)> Clips(Workspace ws, GameInstall install, AssetIndex index,
        IReadOnlyList<SpeciesSkins> species, IAssetReader reader, string speciesId, IReadOnlyList<string> ids)
    {
        var (id, prefab, dir) = Where(ws, install, index, species, speciesId);
        var asked = ids.Distinct(StringComparer.Ordinal).ToList();
        var results = new Dictionary<string, (ClipAnimation? Clip, string? Failure)>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var clipId in asked)
        {
            if (Load<ClipAnimation>(FileOf(dir, clipId)) is { } known) results[clipId] = (known, null);
            else missing.Add(clipId);
        }
        if (missing.Count > 0)
        {
            var records = Records(ws, index, id);
            foreach (var clipId in missing.Where(m => !records.ContainsKey(m)))
                results[clipId] = (null, $"'{clipId}' is not one of {id}'s animations in the game (game updated? run the data dump again: Workspace → Run data dump, or 'tyrant dump run').");
            var wanted = missing.Where(records.ContainsKey).ToList();
            if (wanted.Count > 0)
            {
                var model = reader.ReadPrefabModel(install, prefab);
                var (clips, failures) = reader.ReadRawClips(install, wanted.Select(w => records[w]).ToList());
                var byName = clips.GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                foreach (var clipId in wanted)
                {
                    if (!byName.TryGetValue(clipId, out var raw))
                    {
                        results[clipId] = (null, failures.FirstOrDefault(f => f.Contains(clipId, StringComparison.Ordinal)) ?? $"'{clipId}' could not be read.");
                        continue;
                    }
                    var sampled = ClipSampler.Sample(raw, model.Root);
                    Save(FileOf(dir, clipId), sampled);
                    results[clipId] = (sampled, null);
                }
            }
        }
        return asked.Select(i => (i, results[i].Clip, results[i].Failure)).ToList();
    }

    private static (string Id, AssetRecord Prefab, string Dir) Where(Workspace ws, GameInstall install, AssetIndex index,
        IReadOnlyList<SpeciesSkins> species, string speciesId)
    {
        var (id, prefab) = ModProject.ResolveModelTarget(index, species, speciesId, null);
        var dir = Path.Combine(ws.CacheDir, "animations", $"{TextureExporter.Sanitize(id)}-{WorkspaceCache.Key(ws, install, prefab, CacheVersion)}");
        return (id, prefab, dir);
    }

    /// <summary>The species' clips in its animation table's order, each matched to its index record (the first by name).</summary>
    private static Dictionary<string, AssetRecord> Records(Workspace ws, AssetIndex index, string speciesId)
    {
        var store = BlenderGrowthReader.TryStore(ws);
        var names = store is null ? [] : ClipReader.ClipNames(store, speciesId);
        if (names.Count == 0)
            throw new TyrantException(TyrantErrorCode.DataMissing,
                $"{speciesId}'s animations are not in the game data: run the data dump again (Workspace → Run data dump, or 'tyrant dump run').");
        var wanted = names.ToHashSet(StringComparer.Ordinal);
        var byName = index.Assets.Where(a => a.Type == "AnimationClip" && wanted.Contains(a.Name))
            .GroupBy(a => a.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var ordered = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);
        foreach (var name in names)
            if (byName.TryGetValue(name, out var record)) ordered[name] = record;
        return ordered;
    }

    private static string FileOf(string dir, string clipId) => Path.Combine(dir, TextureExporter.Sanitize(clipId) + ".json");

    private static T? Load<T>(string file) where T : class
    {
        if (!File.Exists(file)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static void Save<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(value, Json));
    }
}
