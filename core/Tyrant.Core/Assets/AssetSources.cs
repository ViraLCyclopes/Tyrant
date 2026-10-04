using System.Text.RegularExpressions;
using Tyrant.Core.Install;

namespace Tyrant.Core.Assets;

/// <summary>A file the asset index reads: an Addressables bundle, or one of the game's built-in files.</summary>
/// <param name="Key">What AssetRecord.Bundle holds: the bundle path relative to StreamingAssets/aa, or "@data/&lt;file&gt;".</param>
public sealed record AssetSource(string Key, string FullPath, bool BuiltIn);

public static class AssetSources
{
    private static readonly Regex BuiltInName = new(@"^(sharedassets\d+\.assets|resources\.assets|level\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Every bundle under StreamingAssets/aa, then the built-in files (sharedassets*.assets, resources.assets, level*), each sorted.</summary>
    public static IReadOnlyList<AssetSource> Discover(GameInstall install)
    {
        var aa = AssetSession.AaDirOf(install);
        var bundles = Directory.Exists(aa)
            ? Directory.GetFiles(aa, "*.bundle", SearchOption.AllDirectories)
                .Select(f => new AssetSource(Path.GetRelativePath(aa, f).Replace('\\', '/'), f, false))
                .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<AssetSource>();
        var builtIn = Directory.Exists(install.DataDir)
            ? Directory.GetFiles(install.DataDir).Where(f => BuiltInName.IsMatch(Path.GetFileName(f)))
                .Select(f => new AssetSource(AssetRecord.BuiltInPrefix + Path.GetFileName(f), f, true))
                .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<AssetSource>();
        return [.. bundles, .. builtIn];
    }
}
