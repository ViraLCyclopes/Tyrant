using System.Security.Cryptography;
using System.Text.Json;

namespace Tyrant.Core.Blender;

/// <summary>
/// A Blender project's textures/ folder. Tyrant remembers, per file it wrote, the source and the file's hash
/// (.tyrant-sources.json), so an open never destroys work: a file whose source is unchanged is left as it is (it may have
/// been painted in Blender); a file that must be replaced (new source, or Start fresh) is first kept as &lt;name&gt;.old.png
/// when it was edited since Tyrant wrote it. Files Tyrant did not write are never touched.
/// </summary>
internal sealed class ProjectTextures(string dir, bool fresh)
{
    private const string Ledger = ".tyrant-sources.json";
    private sealed record Entry(string Source, string Hash);

    private readonly Dictionary<string, Entry> _entries = Load(Path.Combine(dir, Ledger));

    /// <summary>Makes sure the file holds the source's picture; write(path) puts it there (false = there is none). True when the file is there.</summary>
    public bool Put(string file, string source, Func<string, bool> write)
    {
        var path = Path.Combine(dir, file);
        _entries.TryGetValue(file, out var known);
        if (!fresh && known?.Source == source && File.Exists(path)) return true;
        if (File.Exists(path))
        {
            if (known is null || Hash(path) != known.Hash)
                File.Move(path, Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + ".old" + Path.GetExtension(file)), overwrite: true);
            else
                File.Delete(path);
        }
        Directory.CreateDirectory(dir);
        if (!write(path) || !File.Exists(path))
        {
            _entries.Remove(file);
            return false;
        }
        _entries[file] = new Entry(source, Hash(path));
        return true;
    }

    public void Save()
    {
        if (!Directory.Exists(dir)) return;
        File.WriteAllText(Path.Combine(dir, Ledger), JsonSerializer.Serialize(_entries));
    }

    private static Dictionary<string, Entry> Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return []; // unknown: every existing file counts as edited and is kept as .old
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
