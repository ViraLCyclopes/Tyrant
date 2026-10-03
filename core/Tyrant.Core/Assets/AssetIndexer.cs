using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tyrant.Core.Catalog;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Assets;

/// <summary>Scans every Addressables bundle into an <see cref="AssetIndex"/>.</summary>
public sealed class AssetIndexer
{
    public AssetIndex Build(GameInstall install, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var aaDir = AssetSession.AaDirOf(install);
        var catalogPath = Path.Combine(aaDir, "catalog.json");
        var bundles = Directory.Exists(aaDir)
            ? Directory.GetFiles(aaDir, "*.bundle", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
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
        var failures = new List<IndexFailure>();
        using var session = new AssetSession(install);
        for (var i = 0; i < bundles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(aaDir, bundles[i]).Replace('\\', '/');
            if (i % 100 == 0) progress?.Report(new JobProgress((double)i / bundles.Count, $"Indexing {relative}"));
            try
            {
                records.AddRange(ScanBundle(session.Manager, bundles[i], relative));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // AssetsTools.NET throws plain Exceptions for unsupported data; one bad bundle must not stop the index.
                failures.Add(new IndexFailure(relative, ex.Message));
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
            MissingBundles = catalog is null ? [] : MissingBundles(catalog, aaDir),
            Warnings = warnings,
        };
    }

    public AssetIndex BuildAndSave(GameInstall install, Workspace ws, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var index = Build(install, progress, ct);
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

    private static List<AssetRecord> ScanBundle(AssetsManager manager, string path, string relative)
    {
        var bundle = manager.LoadBundleFile(path, true);
        var result = new List<AssetRecord>();
        for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
        {
            if (!bundle.file.IsAssetsFile(i)) continue;
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
