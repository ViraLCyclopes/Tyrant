using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Species;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Data;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Assets;

/// <summary>assets.* and species.* — browse the asset index server-side; previews and exports read bundles through IAssetReader.</summary>
public sealed partial class AssetsMethods(StudioSession session, JobManager jobs)
{
    public const int MaxPageSize = 1000;
    private const StringComparison Ignore = StringComparison.OrdinalIgnoreCase;

    // Fields, not the primary-constructor parameters, so the second partial file (AssetsMethods.Files.cs) can use them.
    private readonly StudioSession _session = session;
    private readonly JobManager _jobs = jobs;
    private readonly object _lock = new();
    private (string Path, DateTime Stamp, AssetIndex Index)? _cache;

    private IAssetReader Reader => _session.Options.AssetReader;

    [RpcMethod("assets.summary")]
    public AssetsSummary Summary()
    {
        var (_, install, index) = Open();
        return new AssetsSummary(
            index.Assets.Count,
            index.Assets.Select(a => a.Bundle).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Counts(index.Assets.Select(a => SpeciesCatalog.BundleGroup(a.Bundle))),
            Counts(index.Assets.Select(a => a.Type)),
            index.Warnings,
            index.Failures.Count,
            index.MissingBundles.Count,
            index.Fingerprint != GameFingerprint.Compute(install) || index.IsOutdatedFormat,
            index.NowDownloaded(install).Count);
    }

    [RpcMethod("assets.bundles")]
    public AssetBundlesResult Bundles(AssetBundlesParams p)
    {
        var (_, _, index) = Open();
        return new AssetBundlesResult(Counts(index.Assets.Where(a => string.Equals(SpeciesCatalog.BundleGroup(a.Bundle), p.Group, Ignore)).Select(a => a.Bundle)));
    }

    [RpcMethod("assets.list")]
    public AssetListResult List(AssetListParams p)
    {
        var (_, _, index) = Open();
        var list = Matching(index, p.Filter, p.Type, p.Group, p.Bundle);
        var pageSize = Math.Clamp(p.PageSize, 1, MaxPageSize);
        var page = Math.Clamp(p.Page, 0, int.MaxValue / MaxPageSize);
        return new AssetListResult(list.Skip(page * pageSize).Take(pageSize).Select(Row).ToList(), list.Count, page, pageSize);
    }

    /// <summary>Every ref on a filter, unpaged: for Select all and Export group.</summary>
    [RpcMethod("assets.refs")]
    public AssetRefsResult Refs(AssetRefsParams p)
    {
        var (_, _, index) = Open();
        return new AssetRefsResult(Matching(index, p.Filter, p.Type, p.Group, p.Bundle).Select(a => a.Ref).ToList());
    }

    private static List<AssetRecord> Matching(AssetIndex index, string? filter, string? type, string? group, string? bundle)
    {
        var tokens = DataQuery.Tokens(filter);
        IEnumerable<AssetRecord> rows = index.Assets;
        if (type is not null) rows = rows.Where(a => string.Equals(a.Type, type, Ignore));
        if (group is not null) rows = rows.Where(a => string.Equals(SpeciesCatalog.BundleGroup(a.Bundle), group, Ignore));
        if (bundle is not null) rows = rows.Where(a => string.Equals(a.Bundle, bundle, Ignore));
        if (tokens.Length > 0) rows = rows.Where(a => tokens.All(t => Matches(a, t)));
        return rows.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Bundle, StringComparer.Ordinal).ThenBy(a => a.PathId).ToList();
    }

    [RpcMethod("assets.get")]
    public AssetDetails Get(AssetRefParams p)
    {
        var (_, install, index) = Open();
        var asset = index.Resolve(p.Ref);
        var inspection = Reader.Inspect(install, asset);
        var sameFile = index.Assets.Where(a => string.Equals(a.Bundle, asset.Bundle, Ignore)).GroupBy(a => a.PathId).ToDictionary(g => g.Key, g => g.First());
        var references = inspection.References.Take(AssetReferences.MaxShown).Select(r =>
        {
            if (r.FileId == 0)
                return sameFile.TryGetValue(r.PathId, out var target)
                    ? new AssetReferenceRow(r.Field, target.Ref, target.Type, target.Name, null)
                    : new AssetReferenceRow(r.Field, null, null, null, $"#{r.PathId} (not in the index)");
            var external = r.FileId - 1 < inspection.ExternalFiles.Count ? inspection.ExternalFiles[r.FileId - 1] : $"file {r.FileId}";
            return new AssetReferenceRow(r.Field, null, null, null, $"{external} #{r.PathId}");
        }).ToList();
        using var fields = JsonDocument.Parse(inspection.FieldsJson);
        return new AssetDetails(Row(asset), inspection.ByteSize, references, fields.RootElement.Clone(), inspection.References.Count > AssetReferences.MaxShown);
    }

    [RpcMethod("species.list")]
    public SpeciesListResult Species()
    {
        var (_, _, index) = Open();
        return new SpeciesListResult(SpeciesCatalog.FromIndex(index)
            .Select(s => new SpeciesRow(s.Key, s.DisplayName, s.Vivarium, s.Group, s.Prefab.Ref, SpeciesCatalog.TexturesFor(index, s).Count))
            .ToList());
    }

    internal static AssetRow Row(AssetRecord a) => new(a.Ref, a.Bundle, a.PathId, a.Type, a.Name, a.ContainerPath, a.Guid, a.Script);

    private static IReadOnlyList<AssetCount> Counts(IEnumerable<string> names) =>
        names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Select(g => new AssetCount(g.Key, g.Count()))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool Matches(AssetRecord a, string token) =>
        Has(a.Name, token) || Has(a.ContainerPath, token) || Has(a.Guid, token) || Has(a.Script, token) || Has(a.Bundle, token);

    private static bool Has(string? value, string token) => value is not null && value.Contains(token, Ignore);

    /// <summary>The open workspace's asset index, cached until the file changes (e.g. after assets.index).</summary>
    private (Workspace Workspace, GameInstall Install, AssetIndex Index) Open()
    {
        var (ws, install) = _session.Current();
        var path = AssetIndex.PathIn(ws);
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        lock (_lock)
            if (_cache is { } cached && cached.Path == path && cached.Stamp == stamp) return (ws, install, cached.Index);
        var index = AssetIndex.Load(path);
        lock (_lock) _cache = (path, stamp, index);
        return (ws, install, index);
    }
}
