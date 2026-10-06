using System.Security.Cryptography;
using System.Text;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;

namespace Tyrant.Core.Workspaces;

/// <summary>Keys for what Tyrant works out from the game once and keeps in the workspace's cache folder.</summary>
public static class WorkspaceCache
{
    /// <summary>Changes with the game build, the species' prefab bundle, the data dump and the caller's own format version.</summary>
    public static string Key(Workspace ws, GameInstall install, AssetRecord prefab, int version)
    {
        static string Stamp(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{info.Length}|{info.LastWriteTimeUtc.Ticks}" : "-";
        }
        string bundle;
        try
        {
            bundle = prefab.IsBuiltIn ? "builtin" : Stamp(Path.Combine(AssetSession.AaDirOf(install), prefab.Bundle.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (ArgumentException)
        {
            bundle = "-";
        }
        string build;
        try
        {
            build = GameFingerprint.Compute(install).BuildGuid;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            build = "-";
        }
        var text = $"{version}|{build}|{prefab.Ref}|{bundle}|{Stamp(Path.Combine(ws.DataDir, "manifest.json"))}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12].ToLowerInvariant();
    }
}
