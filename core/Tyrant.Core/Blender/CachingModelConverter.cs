using System.Security.Cryptography;
using System.Text;

namespace Tyrant.Core.Blender;

/// <summary>
/// Keeps each FBX's converted glb, keyed by the FBX's full path, size and last write time, so reading a model's rig edit
/// and then adding it converts the FBX once. Everything else goes to <paramref name="inner"/>.
/// </summary>
public sealed class CachingModelConverter(IModelConverter inner, string cacheDir) : IModelConverter
{
    /// <summary>The newest glbs kept; older ones are deleted when a new one is cached.</summary>
    private const int Kept = 8;

    public void FbxToGlb(string fbx, string glb)
    {
        var cached = CachedPath(fbx);
        if (File.Exists(cached))
        {
            File.Copy(cached, glb, overwrite: true);
            return;
        }
        inner.FbxToGlb(fbx, glb);
        try
        {
            Directory.CreateDirectory(cacheDir);
            var temp = cached + $".{Guid.NewGuid():N}.tmp";
            File.Copy(glb, temp, overwrite: true);
            File.Move(temp, cached, overwrite: true);
            Prune();
        }
        catch (IOException)
        {
            // the cache is only a shortcut: converting again next time is fine
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyDictionary<string, string> GlbToFbx(IReadOnlyList<(string Glb, string Fbx)> pairs) => inner.GlbToFbx(pairs);

    private string CachedPath(string fbx)
    {
        var file = new FileInfo(Path.GetFullPath(fbx));
        var key = $"{file.FullName.ToUpperInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        return Path.Combine(cacheDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant() + ".glb");
    }

    private void Prune()
    {
        foreach (var old in new DirectoryInfo(cacheDir).GetFiles("*.glb").OrderByDescending(f => f.LastWriteTimeUtc).Skip(Kept))
            old.Delete();
    }
}
