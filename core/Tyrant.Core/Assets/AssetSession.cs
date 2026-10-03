using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Assets;

/// <summary>Opens objects from the game's Addressables bundles. Not thread-safe; Release() frees loaded bundles.</summary>
public sealed class AssetSession : IDisposable
{
    private readonly AssetsManager _manager = new();
    private readonly string _aaDir;

    public AssetSession(GameInstall install)
    {
        _aaDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AaDirOf(install)));
        _manager.MonoTempGenerator = new MonoCecilTempGenerator(install.ManagedDir);
    }

    internal AssetsManager Manager => _manager;

    public static string AaDirOf(GameInstall install) => Path.Combine(install.StreamingAssetsDir, "aa");

    /// <summary>Full path of an indexed bundle; refuses anything outside StreamingAssets/aa.</summary>
    public string BundlePath(string relativeBundle)
    {
        var full = Path.GetFullPath(Path.Combine(_aaDir, relativeBundle.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_aaDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new TyrantException(TyrantErrorCode.AssetNotFound,
                $"Bundle path '{relativeBundle}' points outside the game's Addressables folder. Re-run 'tyrant assets index'.",
                FixAction.RefreshWorkspace);
        return full;
    }

    public (AssetsFileInstance File, AssetTypeValueField BaseField) Open(AssetRecord asset)
    {
        var path = BundlePath(asset.Bundle);
        if (!File.Exists(path))
            throw new TyrantException(TyrantErrorCode.AssetNotFound,
                $"Bundle '{asset.Bundle}' no longer exists (game updated?). Re-run 'tyrant assets index'.", FixAction.RefreshWorkspace);

        try
        {
            var bundle = _manager.LoadBundleFile(path, true);
            for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
            {
                if (!bundle.file.IsAssetsFile(i)) continue;
                var file = _manager.LoadAssetsFileFromBundle(bundle, i, false);
                var info = file.file.AssetInfos.FirstOrDefault(a => a.PathId == asset.PathId);
                if (info is not null) return (file, _manager.GetBaseField(file, info));
            }
        }
        catch (Exception ex) when (ex is not TyrantException and not OperationCanceledException)
        {
            // AssetsTools.NET throws plain Exceptions for data it cannot parse (e.g. a bundle changed by a game update).
            throw new TyrantException(TyrantErrorCode.AssetUnreadable,
                $"Bundle '{asset.Bundle}' could not be read ({ex.Message}). If the game was updated, re-run 'tyrant assets index'.",
                FixAction.RefreshWorkspace, ex);
        }
        throw new TyrantException(TyrantErrorCode.AssetNotFound,
            $"Object {asset.PathId} is not in '{asset.Bundle}' (game updated?). Re-run 'tyrant assets index'.", FixAction.RefreshWorkspace);
    }

    public void Release() => _manager.UnloadAll();

    public void Dispose() => _manager.UnloadAll(true);
}
