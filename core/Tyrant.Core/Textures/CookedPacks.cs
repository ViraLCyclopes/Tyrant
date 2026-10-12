using System.Collections.Concurrent;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Textures;

/// <summary>Whether an installed mod's textures.tpack still matches its PNGs (the Mods tab's "Reinstall to cook textures").</summary>
public static class CookedPacks
{
    private static readonly ConcurrentDictionary<string, (long Length, DateTime Written, TextureFingerprint Print)> Prints = new(StringComparer.OrdinalIgnoreCase);

    public static string StateOf(string modDir, ModManifest manifest)
    {
        var files = ModTextureCooker.TextureFiles(manifest);
        if (files.Count == 0) return "none";
        var path = Path.Combine(modDir, TexturePack.FileName);
        if (!File.Exists(path)) return "stale";
        TexturePack pack;
        try
        {
            using var stream = File.OpenRead(path);
            pack = TexturePack.Read(stream);
        }
        catch (Exception ex) when (ex is TexturePackException or IOException or UnauthorizedAccessException)
        {
            return "stale";
        }
        foreach (var file in files)
        {
            var png = ModTextureCooker.InsidePath(modDir, file);
            if (png is null || !File.Exists(png)) continue; // missing or outside the mod folder: Check's problem, not cooking's
            var print = Print(png);
            if (!pack.Entries.Any(e => string.Equals(e.Source, file, StringComparison.OrdinalIgnoreCase) && e.Fingerprint.Equals(print))) return "stale";
        }
        return "current";
    }

    /// <summary>
    /// Every one of <paramref name="files"/> that exists under <paramref name="pngDir"/> has an entry in the pack with its
    /// fingerprint and the compression the manifest asks for (a slim install's PNGs are compared through its pack).
    /// </summary>
    public static bool Matches(TexturePack pack, ModManifest manifest, string pngDir, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var png = ModTextureCooker.InsidePath(pngDir, file);
            if (png is null || !File.Exists(png)) continue;
            var print = Print(png);
            var mode = manifest.Textures.For(file);
            if (!pack.Entries.Any(e => string.Equals(TexturePack.NormalSource(e.Source), file, StringComparison.OrdinalIgnoreCase)
                                       && e.Fingerprint.Equals(print) && e.Mode == mode))
                return false;
        }
        return true;
    }

    /// <summary>A PNG's fingerprint, read again only when its size or write time changed.</summary>
    internal static TextureFingerprint Print(string path)
    {
        var info = new FileInfo(path);
        if (Prints.TryGetValue(info.FullName, out var cached) && cached.Length == info.Length && cached.Written == info.LastWriteTimeUtc) return cached.Print;
        var print = TextureFingerprint.Of(info.FullName);
        Prints[info.FullName] = (info.Length, info.LastWriteTimeUtc, print);
        return print;
    }
}
