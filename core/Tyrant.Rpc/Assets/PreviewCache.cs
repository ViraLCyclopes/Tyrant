using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tyrant.Core.Assets;
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
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);

    public static string Root(Workspace ws) => Path.Combine(ws.CacheDir, "previews");

    public AssetPreview GetOrCreate(Workspace ws, string buildGuid, AssetRecord asset, Func<string, AssetPreview> create)
    {
        var dir = Path.Combine(Root(ws), Safe(buildGuid.Length > 0 ? buildGuid : "unknown-build"), FolderFor(asset));
        lock (_locks.GetOrAdd(dir, _ => new object())) // two requests for one asset must not write the same files at once
        {
            var meta = Path.Combine(dir, MetaFile);
            if (TryRead(meta) is { } cached) return cached;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); // a previous attempt failed half-way
            Directory.CreateDirectory(dir);
            var preview = create(dir);
            File.WriteAllText(meta, JsonSerializer.Serialize(preview, RpcJson.Options));
            return preview;
        }
    }

    private static AssetPreview? TryRead(string meta)
    {
        try
        {
            if (!File.Exists(meta)) return null;
            var preview = JsonSerializer.Deserialize<AssetPreview>(File.ReadAllText(meta), RpcJson.Options);
            return preview is not null && preview.Files.All(File.Exists) ? preview : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A short, unique, file-system-safe folder name: a hash of the ref plus a readable name.</summary>
    private static string FolderFor(AssetRecord asset)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(asset.Ref)))[..12].ToLowerInvariant();
        var name = Safe(asset.Name);
        return $"{hash}_{(name.Length > 40 ? name[..40] : name)}";
    }

    private static string Safe(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(text.Select(c => invalid.Contains(c) || c == '.' ? '_' : c).ToArray()).Trim();
        return cleaned.Length > 0 ? cleaned : "_";
    }
}
