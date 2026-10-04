using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Assets;

/// <summary>Opens objects from the game's Addressables bundles and built-in files. Not thread-safe; Release() frees loaded files.</summary>
public sealed class AssetSession : IDisposable
{
    private readonly AssetsManager _manager = new();
    private readonly string _aaDir;
    private readonly string _dataDir;

    public AssetSession(GameInstall install)
    {
        _aaDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AaDirOf(install)));
        _dataDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install.DataDir));
        _manager.MonoTempGenerator = new MonoCecilTempGenerator(install.ManagedDir);
    }

    internal AssetsManager Manager => _manager;

    private bool _packageLoaded;
    private readonly HashSet<string> _databases = new(StringComparer.Ordinal);

    /// <summary>
    /// Files without type trees (the game's built-in files) are read with the class database of their Unity version. The
    /// package is loaded on first need, so sessions that only read bundles never pay for it.
    /// </summary>
    internal void UseClassDatabase(AssetsFileInstance file)
    {
        if (file.file.Metadata.TypeTreeEnabled) return;
        if (!_packageLoaded)
        {
            _manager.LoadClassPackage(ClassDatabase.Open());
            _packageLoaded = true;
        }
        if (_databases.Add(file.file.Metadata.UnityVersion)) _manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
    }

    public static string AaDirOf(GameInstall install) => Path.Combine(install.StreamingAssetsDir, "aa");

    /// <summary>Full path of an indexed bundle; refuses anything outside StreamingAssets/aa.</summary>
    public string BundlePath(string relativeBundle)
    {
        var full = Path.GetFullPath(Path.Combine(_aaDir, relativeBundle.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_aaDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new TyrantException(TyrantErrorCode.AssetNotFound,
                $"Bundle path '{relativeBundle}' points outside the game's Addressables folder. Index the assets again (Workspace → Index assets, or 'tyrant assets index').",
                FixAction.ReindexAssets);
        return full;
    }

    /// <summary>Full path of a built-in file; refuses anything outside Prehistoric Kingdom_Data.</summary>
    public string DataPath(string dataFile)
    {
        var full = Path.GetFullPath(Path.Combine(_dataDir, dataFile.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_dataDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new TyrantException(TyrantErrorCode.AssetNotFound,
                $"'{dataFile}' points outside the game's data folder. Index the assets again (Workspace → Index assets, or 'tyrant assets index').",
                FixAction.ReindexAssets);
        return full;
    }

    public (AssetsFileInstance File, AssetTypeValueField BaseField) Open(AssetRecord asset)
    {
        if (asset.IsBuiltIn) return OpenBuiltIn(asset);
        var path = BundlePath(asset.Bundle);
        if (!File.Exists(path))
            throw new TyrantException(TyrantErrorCode.AssetNotFound,
                $"Bundle '{asset.Bundle}' no longer exists (game updated?). Index the assets again (Workspace → Index assets, or 'tyrant assets index').", FixAction.ReindexAssets);

        try
        {
            var bundle = _manager.LoadBundleFile(path, true);
            for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
            {
                if (!bundle.file.IsAssetsFile(i)) continue;
                var file = _manager.LoadAssetsFileFromBundle(bundle, i, false);
                var info = file.file.AssetInfos.FirstOrDefault(a => a.PathId == asset.PathId);
                if (info is null) continue;
                // A game update can reuse an object number for another kind of object: never hand back the wrong one.
                if (Enum.TryParse<AssetClassID>(asset.Type, out var expected) && info.TypeId != (int)expected)
                    throw new TyrantException(TyrantErrorCode.AssetNotFound,
                        $"Object {asset.PathId} in '{asset.Bundle}' is no longer a {asset.Type} (game updated?). Index the assets again (Workspace → Index assets, or 'tyrant assets index').", FixAction.ReindexAssets);
                return (file, _manager.GetBaseField(file, info));
            }
        }
        catch (Exception ex) when (ex is not TyrantException and not OperationCanceledException)
        {
            // AssetsTools.NET throws plain Exceptions for data it cannot parse (e.g. a bundle changed by a game update).
            throw new TyrantException(TyrantErrorCode.AssetUnreadable,
                $"Bundle '{asset.Bundle}' could not be read ({ex.Message}). If the game was updated, index the assets again (Workspace → Index assets, or 'tyrant assets index').",
                FixAction.ReindexAssets, ex);
        }
        throw new TyrantException(TyrantErrorCode.AssetNotFound,
            $"Object {asset.PathId} is not in '{asset.Bundle}' (game updated?). Index the assets again (Workspace → Index assets, or 'tyrant assets index').", FixAction.ReindexAssets);
    }

    private (AssetsFileInstance File, AssetTypeValueField BaseField) OpenBuiltIn(AssetRecord asset)
    {
        var path = DataPath(asset.DataFile!);
        if (!File.Exists(path))
            throw new TyrantException(TyrantErrorCode.AssetNotFound,
                $"'{asset.DataFile}' no longer exists (game updated?). Index the assets again (Workspace → Index assets, or 'tyrant assets index').", FixAction.ReindexAssets);
        try
        {
            var file = _manager.LoadAssetsFile(path, true);
            UseClassDatabase(file);
            var info = file.file.AssetInfos.FirstOrDefault(a => a.PathId == asset.PathId)
                ?? throw new TyrantException(TyrantErrorCode.AssetNotFound,
                    $"Object {asset.PathId} is not in '{asset.DataFile}' (game updated?). Index the assets again (Workspace → Index assets, or 'tyrant assets index').", FixAction.ReindexAssets);
            if (Enum.TryParse<AssetClassID>(asset.Type, out var expected) && info.TypeId != (int)expected)
                throw new TyrantException(TyrantErrorCode.AssetNotFound,
                    $"Object {asset.PathId} in '{asset.DataFile}' is no longer a {asset.Type} (game updated?). Index the assets again (Workspace → Index assets, or 'tyrant assets index').", FixAction.ReindexAssets);
            return (file, _manager.GetBaseField(file, info));
        }
        catch (Exception ex) when (ex is not TyrantException and not OperationCanceledException)
        {
            throw new TyrantException(TyrantErrorCode.AssetUnreadable,
                $"'{asset.DataFile}' could not be read ({ex.Message}). If the game was updated, index the assets again (Workspace → Index assets, or 'tyrant assets index').",
                FixAction.ReindexAssets, ex);
        }
    }

    public void Release() => _manager.UnloadAll();

    public void Dispose() => _manager.UnloadAll(true);
}
