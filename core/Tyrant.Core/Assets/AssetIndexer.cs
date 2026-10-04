using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tyrant.Core.Catalog;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Assets;

/// <summary>Scans every Addressables bundle and the game's built-in files into an <see cref="AssetIndex"/>.</summary>
public sealed class AssetIndexer
{
    private readonly Func<AssetSession, AssetSource, Dictionary<string, string>, List<AssetRecord>> _scan;

    /// <param name="scan">Reads one source into records, adding its archive names (tests pass a fake); null reads the game's files.</param>
    public AssetIndexer(Func<AssetSession, AssetSource, Dictionary<string, string>, List<AssetRecord>>? scan = null) =>
        _scan = scan ?? ((session, source, archives) => source.BuiltIn ? ScanBuiltIn(session, source, archives) : ScanBundle(session.Manager, source.FullPath, source.Key, archives));

    /// <param name="previous">The last index: sources whose size and write time are unchanged are copied from it instead of read again.</param>
    public AssetIndex Build(GameInstall install, IProgress<JobProgress>? progress, CancellationToken ct, AssetIndex? previous = null)
    {
        var aaDir = AssetSession.AaDirOf(install);
        var catalogPath = Path.Combine(aaDir, "catalog.json");
        var sources = AssetSources.Discover(install);
        var bundles = sources.Where(s => !s.BuiltIn).ToList();
        var warnings = new List<string>();
        AddressablesCatalog? catalog = null;
        if (File.Exists(catalogPath))
        {
            try
            {
                catalog = AddressablesCatalog.Load(catalogPath);
            }
            catch (TyrantException ex) when (ex.Code == TyrantErrorCode.CatalogInvalid)
            {
                warnings.Add($"The Addressables catalog could not be read, so GUIDs and missing-bundle detection are unavailable: {ex.Message}");
            }
        }
        else if (bundles.Count > 0)
        {
            warnings.Add("No catalog.json next to the bundles (new Addressables format?), so GUIDs and missing-bundle detection are unavailable.");
        }

        var records = new List<AssetRecord>();
        var archives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<IndexFailure>();
        var stamps = new List<IndexedSource>();
        var reusable = previous is { IsOutdatedFormat: false } ? previous : null; // an older format is rebuilt in full
        var known = reusable?.Sources.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, IndexedSource>(StringComparer.OrdinalIgnoreCase);
        var oldRecords = reusable?.Assets.ToLookup(a => a.Bundle, StringComparer.OrdinalIgnoreCase);
        using var session = new AssetSession(install);
        for (var i = 0; i < sources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var source = sources[i];
            var info = new FileInfo(source.FullPath);
            var stamp = new IndexedSource(source.Key, info.Length, info.LastWriteTimeUtc.Ticks);
            if (known.TryGetValue(source.Key, out var old) && old == stamp && oldRecords is not null)
            {
                records.AddRange(oldRecords[source.Key]);
                foreach (var (name, owner) in reusable!.Archives) if (string.Equals(owner, source.Key, StringComparison.OrdinalIgnoreCase)) archives.TryAdd(name, owner);
                stamps.Add(stamp);
                continue;
            }
            if (i % 100 == 0 || source.BuiltIn) progress?.Report(new JobProgress((double)i / sources.Count, $"Indexing {source.Key}"));
            try
            {
                records.AddRange(_scan(session, source, archives));
                stamps.Add(stamp); // a failed source gets no stamp, so it is read again next time
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // AssetsTools.NET throws plain Exceptions for unsupported data; one bad file must not stop the index.
                failures.Add(new IndexFailure(source.Key, ex.Message));
            }
            finally
            {
                session.Release();
            }
        }
        progress?.Report(new JobProgress(1.0, "Done"));

        return new AssetIndex
        {
            Fingerprint = GameFingerprint.Compute(install),
            Assets = catalog is null ? records : AssetIndex.AttachCatalogKeys(records, catalog),
            Failures = failures,
            Sources = stamps,
            MissingBundles = catalog is null ? [] : MissingBundles(catalog, aaDir),
            Warnings = warnings,
            Archives = archives,
        };
    }

    public AssetIndex BuildAndSave(GameInstall install, Workspace ws, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        AssetIndex? previous = null;
        try
        {
            previous = File.Exists(AssetIndex.PathIn(ws)) ? AssetIndex.Load(AssetIndex.PathIn(ws)) : null;
        }
        catch (TyrantException)
        {
            // an unreadable or newer index is simply rebuilt in full
        }
        var index = Build(install, progress, ct, previous);
        index.Save(AssetIndex.PathIn(ws));
        ws.StampOutput(AssetIndex.OutputName, index.Fingerprint!);
        return index;
    }

    internal static List<string> MissingBundles(AddressablesCatalog catalog, string aaDir) =>
        catalog.Entries
            .Where(e => e.IsBundle)
            .Select(e => AddressablesCatalog.BundleRelativePath(e.InternalId))
            .OfType<string>()
            .Where(rel => !File.Exists(Path.Combine(aaDir, rel.Replace('/', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<AssetRecord> ScanBundle(AssetsManager manager, string path, string relative, Dictionary<string, string> archives)
    {
        var bundle = manager.LoadBundleFile(path, true);
        var result = new List<AssetRecord>();
        for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
        {
            if (!bundle.file.IsAssetsFile(i)) continue;
            archives.TryAdd(bundle.file.BlockAndDirInfo.DirectoryInfos[i].Name, relative); // "CAB-…", as other bundles reference it
            var file = manager.LoadAssetsFileFromBundle(bundle, i, false);
            var containers = ReadContainers(manager, file);
            foreach (var info in file.file.AssetInfos)
            {
                var type = (AssetClassID)info.TypeId;
                var baseField = manager.GetBaseField(file, info);
                var name = baseField["m_Name"];
                result.Add(new AssetRecord(
                    relative,
                    info.PathId,
                    type.ToString(),
                    name.IsDummy ? "" : name.AsString,
                    containers.GetValueOrDefault(info.PathId),
                    null,
                    type == AssetClassID.MonoBehaviour ? ScriptClass(manager, file, baseField) : null));
            }
        }
        return result;
    }

    /// <summary>One of the game's built-in files (no type trees: read with the class database). Other files reference it by name.</summary>
    private static List<AssetRecord> ScanBuiltIn(AssetSession session, AssetSource source, Dictionary<string, string> archives)
    {
        var manager = session.Manager;
        var file = manager.LoadAssetsFile(source.FullPath, false);
        session.UseClassDatabase(file);
        archives.TryAdd(Path.GetFileName(source.FullPath), source.Key);
        var result = new List<AssetRecord>();
        foreach (var info in file.file.AssetInfos)
        {
            var type = (AssetClassID)info.TypeId;
            AssetTypeValueField? baseField = null;
            try
            {
                baseField = manager.GetBaseField(file, info);
            }
            catch (Exception)
            {
                // a type the class database does not describe still gets a record, without a name
            }
            var name = baseField?["m_Name"];
            result.Add(new AssetRecord(source.Key, info.PathId, type.ToString(), name is null || name.IsDummy ? "" : name.AsString, null, null,
                type == AssetClassID.MonoBehaviour && baseField is not null ? ScriptClass(manager, file, baseField) : null));
        }
        return result;
    }

    private static Dictionary<long, string> ReadContainers(AssetsManager manager, AssetsFileInstance file)
    {
        var map = new Dictionary<long, string>();
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.AssetBundle))
            foreach (var entry in manager.GetBaseField(file, info)["m_Container.Array"].Children)
                map.TryAdd(entry["second.asset.m_PathID"].AsLong, entry["first"].AsString);
        return map;
    }

    private static string? ScriptClass(AssetsManager manager, AssetsFileInstance file, AssetTypeValueField baseField)
    {
        try
        {
            var script = manager.GetExtAsset(file, baseField["m_Script"]);
            var className = script.baseField?["m_ClassName"];
            return className is null || className.IsDummy ? null : className.AsString;
        }
        catch (Exception)
        {
            return null; // script lives in a bundle we cannot resolve; the record is still useful without it
        }
    }
}
