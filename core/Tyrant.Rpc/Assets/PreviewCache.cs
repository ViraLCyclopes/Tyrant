using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Assets;

/// <summary>
/// Previews live in &lt;workspace&gt;/cache/previews/&lt;build&gt;/&lt;asset&gt;/ next to a preview.json describing them,
/// so each asset is decoded once per game build and a new build never shows an old preview.
/// </summary>
internal sealed class PreviewCache
{
    private const string MetaFile = "preview.json";

    /// <summary>
    /// Bumped whenever previews are made differently (2: packed normal maps rebuilt; 3: found by their pixels; 4: models carry their materials), so a tool update never shows
    /// previews made the old way. Part of the build folder name; older folders are removed like old builds.
    /// </summary>
    internal const int FormatVersion = 4;
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);

    public static string Root(Workspace ws) => Path.Combine(ws.CacheDir, "previews");

    /// <param name="key">What the preview shows, within one build (an asset ref, plus anything else that changes the result).</param>
    /// <param name="name">A readable part of the folder name.</param>
    public T GetOrCreate<T>(Workspace ws, GameFingerprint build, string key, string name, Func<string, T> create) where T : class, IPreviewFiles
    {
        // The build id alone is "unknown" when boot.config has none; the assembly hash still tells builds apart.
        var buildFolder = Path.Combine(Root(ws), Safe($"{build.BuildGuid}-{build.AssemblySha256[..Math.Min(8, build.AssemblySha256.Length)]}-p{FormatVersion}"));
        if (!Directory.Exists(buildFolder)) RemoveOtherBuilds(Root(ws), buildFolder);
        var dir = Path.Combine(buildFolder, FolderFor(key, name));
        lock (_locks.GetOrAdd(dir, _ => new object())) // two requests for one preview must not write the same files at once
        {
            var meta = Path.Combine(dir, MetaFile);
            if (TryRead<T>(meta) is { } cached) return cached;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); // a previous attempt failed half-way
            Directory.CreateDirectory(dir);
            var preview = create(dir);
            File.WriteAllText(meta, JsonSerializer.Serialize(preview, RpcJson.Options));
            return preview;
        }
    }

    /// <summary>The first preview of a new build clears the previous builds' previews, so the cache never grows without bound.</summary>
    private static void RemoveOtherBuilds(string root, string keep)
    {
        if (!Directory.Exists(root)) return;
        foreach (var folder in Directory.GetDirectories(root))
        {
            if (string.Equals(Path.GetFullPath(folder), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a file still open (e.g. shown in the app); it goes next time
            }
        }
    }

    private static T? TryRead<T>(string meta) where T : class, IPreviewFiles
    {
        try
        {
            if (!File.Exists(meta)) return null;
            var preview = JsonSerializer.Deserialize<T>(File.ReadAllText(meta), RpcJson.Options);
            return preview is not null && preview.Files.All(File.Exists) && (((IPreviewFiles)preview).Textures ?? []).All(File.Exists) ? preview : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A short, unique, file-system-safe folder name: a hash of the key plus a readable name.</summary>
    private static string FolderFor(string key, string name)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12].ToLowerInvariant();
        var safe = Safe(name);
        return $"{hash}_{(safe.Length > 40 ? safe[..40] : safe)}";
    }

    private static string Safe(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(text.Select(c => invalid.Contains(c) || c == '.' ? '_' : c).ToArray()).Trim();
        return cleaned.Length > 0 ? cleaned : "_";
    }
}
