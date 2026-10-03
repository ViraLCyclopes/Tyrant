using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Tyrant.Core.Catalog;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Assets;

/// <summary>One object inside a bundle. Bundle is relative to StreamingAssets/aa with '/' separators.</summary>
public sealed record AssetRecord(string Bundle, long PathId, string Type, string Name, string? ContainerPath, string? Guid, string? Script)
{
    /// <summary>Unambiguous reference accepted by <see cref="AssetIndex.Resolve"/>: "bundle#pathId".</summary>
    [JsonIgnore]
    public string Ref => $"{Bundle}#{PathId}";
}

public sealed record IndexFailure(string Bundle, string Error);

/// <summary>Every object in the game's Addressables bundles, with catalog GUIDs where known.</summary>
public sealed class AssetIndex
{
    public const string FileName = "asset-index.json";
    public const string OutputName = "assets/index";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex GuidPattern = new("^[0-9a-f]{32}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int SchemaVersion { get; set; } = 1;
    public GameFingerprint? Fingerprint { get; set; }
    public List<AssetRecord> Assets { get; set; } = [];
    public List<IndexFailure> Failures { get; set; } = [];

    /// <summary>Bundles the catalog lists that are not on disk (e.g. DLC that is not downloaded).</summary>
    public List<string> MissingBundles { get; set; } = [];

    /// <summary>Problems that degraded the index without stopping it (e.g. an unreadable catalog, so no GUIDs).</summary>
    public List<string> Warnings { get; set; } = [];

    public static string PathIn(Workspace ws) => Path.Combine(ws.CacheDir, FileName);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, this, Json);
        File.Move(tmp, path, overwrite: true);
    }

    public static AssetIndex Load(string path)
    {
        if (!File.Exists(path)) throw Missing("No asset index yet. Run 'tyrant assets index' first.");
        try
        {
            using var stream = File.OpenRead(path);
            var index = JsonSerializer.Deserialize<AssetIndex>(stream, Json) ?? throw Missing("The asset index is empty. Run 'tyrant assets index' again.");
            index.Assets ??= [];
            index.Failures ??= [];
            index.MissingBundles ??= [];
            index.Warnings ??= [];
            if (index.Assets.Any(a => a is null || a.Bundle is null || a.Type is null || a.Name is null))
                throw Missing("The asset index is corrupt (incomplete records). Run 'tyrant assets index' again.");
            index.Failures.RemoveAll(f => f is null);
            index.MissingBundles.RemoveAll(b => b is null);
            index.Warnings.RemoveAll(w => w is null);
            return index;
        }
        catch (JsonException ex)
        {
            throw Missing($"The asset index is corrupt ({ex.Message}). Run 'tyrant assets index' again.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Missing($"Cannot read the asset index: {ex.Message}", ex);
        }
    }

    public IEnumerable<AssetRecord> Query(string? type, string? filter) =>
        Assets
            .Where(a => type is null || string.Equals(a.Type, type, StringComparison.OrdinalIgnoreCase))
            .Where(a => filter is null || Has(a.Name, filter) || Has(a.ContainerPath, filter) || Has(a.Guid, filter)
                        || Has(a.Script, filter) || Has(a.Bundle, filter));

    /// <summary>Finds one asset by "bundle#pathId", catalog GUID or container path; type narrows the match.</summary>
    public AssetRecord Resolve(string key, string? type = null)
    {
        IEnumerable<AssetRecord> candidates;
        var hash = key.LastIndexOf('#');
        if (hash > 0 && long.TryParse(key[(hash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pathId))
        {
            var bundle = key[..hash];
            candidates = Assets.Where(a => a.PathId == pathId && string.Equals(a.Bundle, bundle, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            candidates = Assets.Where(a => string.Equals(a.Guid, key, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(a.ContainerPath, key, StringComparison.OrdinalIgnoreCase));
        }
        if (type is not null) candidates = candidates.Where(a => string.Equals(a.Type, type, StringComparison.OrdinalIgnoreCase));

        var matches = candidates.Take(11).ToList();
        return matches.Count switch
        {
            0 => throw new TyrantException(TyrantErrorCode.AssetNotFound,
                $"No asset matches '{key}'{(type is null ? "" : $" with type {type}")}. Use 'tyrant assets list --filter <text>' to search."),
            1 => matches[0],
            _ => throw new TyrantException(TyrantErrorCode.AssetAmbiguous,
                $"'{key}' matches several assets; pass --type or use one of these refs:{Environment.NewLine}"
                + string.Join(Environment.NewLine, matches.Take(10).Select(a => $"  {a.Type,-16} {a.Ref}"))),
        };
    }

    /// <summary>Copies catalog GUIDs onto records by container path, preferring the catalog entry of the same type.</summary>
    public static List<AssetRecord> AttachCatalogKeys(IEnumerable<AssetRecord> records, AddressablesCatalog catalog)
    {
        var byPath = new Dictionary<string, List<(string Type, string Guid)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.Entries)
        {
            if (entry.IsBundle) continue;
            var guid = entry.Keys.FirstOrDefault(k => GuidPattern.IsMatch(k));
            if (guid is null) continue;
            if (!byPath.TryGetValue(entry.InternalId, out var options)) byPath[entry.InternalId] = options = [];
            options.Add((entry.ShortResourceType, guid));
        }

        return records.Select(r =>
        {
            if (r.ContainerPath is null || !byPath.TryGetValue(r.ContainerPath, out var options)) return r;
            var exact = options.FirstOrDefault(o => string.Equals(o.Type, r.Type, StringComparison.OrdinalIgnoreCase));
            var guid = exact.Guid ?? (options.Count == 1 ? options[0].Guid : null);
            return guid is null ? r : r with { Guid = guid };
        }).ToList();
    }

    private static bool Has(string? value, string filter) =>
        value is not null && value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static TyrantException Missing(string message, Exception? inner = null) =>
        new(TyrantErrorCode.AssetIndexMissing, message, FixAction.RefreshWorkspace, inner);
}
