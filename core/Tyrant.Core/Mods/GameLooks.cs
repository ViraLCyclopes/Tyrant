using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>
/// A material folder (materials/&lt;skins, species or vanilla-skins&gt;/&lt;folder&gt;) that is not what Tyrant installed:
/// changed in the game (the in-game editor saved into it), in the workspace, or both.
/// </summary>
public sealed record LooksChange(string Folder, bool GameChanged, bool ProjectChanged);

/// <summary>
/// Looks made in the game: the in-game editor saves into the installed copy's material folders, which the next install or a
/// remove would throw away. Install records each folder's hash in UserData/Tyrant/install-stamps.json (outside the mod folder,
/// so <see cref="GameMods.StateOf"/> does not see it); a folder whose installed copy no longer matches its stamp was changed
/// in the game and can be brought into the workspace first.
/// </summary>
public static class GameLooks
{
    public const string StampFile = "install-stamps.json";
    private const int Format = 1;

    /// <summary>The three kinds of material folder under materials/ (<see cref="MaterialFolders"/>).</summary>
    private static readonly string[] Kinds = ["skins", "species", "vanilla-skins"];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string StampPath(GameInstall install) => Path.Combine(ModLoaderInstaller.RecordDir(install), StampFile);

    /// <summary>Records the hash of every material folder of the copy just installed at <paramref name="installedDir"/>.</summary>
    public static void Stamp(GameInstall install, string modId, string installedDir)
    {
        var skip = LeftOut(installedDir);
        var stamps = ReadStamps(install);
        stamps[modId] = Folders(installedDir).ToDictionary(f => f, f => Hash(installedDir, f, skip)!, StringComparer.OrdinalIgnoreCase);
        WriteStamps(install, stamps);
    }

    /// <summary>Drops the mod's stamps (it left the game).</summary>
    public static void Forget(GameInstall install, string modId)
    {
        if (!File.Exists(StampPath(install))) return;
        var stamps = ReadStamps(install);
        if (stamps.Remove(modId)) WriteStamps(install, stamps);
    }

    /// <summary>
    /// The material folders that differ between the game's copy, the workspace and the stamp, sorted. A folder counts as
    /// changed in the game when its installed copy differs from its stamp or was never stamped; one the same on both sides
    /// is no change (there is nothing to bring in). Empty when the mod is not in the game.
    /// </summary>
    public static IReadOnlyList<LooksChange> Changes(GameInstall install, ModProject mod)
    {
        var installed = Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id);
        if (!Directory.Exists(installed)) return [];
        var stamped = ReadStamps(install).GetValueOrDefault(mod.Id);
        var skip = LeftOut(installed);
        var changes = new List<LooksChange>();
        foreach (var folder in Folders(installed).Concat(Folders(mod.Dir)).Concat(stamped?.Keys ?? Enumerable.Empty<string>())
                     .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            var game = Hash(installed, folder, skip);
            var project = Hash(mod.Dir, folder, skip);
            if (game == project) continue;
            var stamp = stamped?.GetValueOrDefault(folder);
            changes.Add(new LooksChange(folder, game != stamp, project != stamp));
        }
        return changes;
    }

    /// <summary>The folders changed in the game, in the words the CLI and RPC errors use.</summary>
    public static string Describe(IEnumerable<LooksChange> changes) => string.Join(", ", changes.Where(c => c.GameChanged).Select(c => c.Folder));

    /// <summary>
    /// Replaces these material folders in the workspace with the game's copy (a folder gone from the game goes from the
    /// workspace too), stamps them as brought in, and reads the project's materials again. A mask a slim install left out
    /// (it is in the installed pack) keeps the workspace's PNG. Returns how many folders were brought in.
    /// </summary>
    public static int BringIn(GameInstall install, ModProject mod, IReadOnlyCollection<string> folders)
    {
        var chosen = folders.Select(f => f.Replace('\\', '/').Trim('/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var folder in chosen)
            if (!IsFolder(folder))
                throw new TyrantException(TyrantErrorCode.ModInvalid,
                    $"'{folder}' is not a material folder (materials/<skins, species or vanilla-skins>/<folder>).");
        var installed = Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id);
        if (!Directory.Exists(installed)) throw new TyrantException(TyrantErrorCode.ModNotFound, $"'{mod.Id}' is not installed in the game.");
        var skip = LeftOut(installed);
        var stamps = ReadStamps(install);
        var stamped = stamps.TryGetValue(mod.Id, out var s) ? s : stamps[mod.Id] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var folder in chosen)
            {
                var from = Path.Combine(installed, folder.Replace('/', Path.DirectorySeparatorChar));
                var to = Path.Combine(mod.Dir, folder.Replace('/', Path.DirectorySeparatorChar));
                Replace(from, to, folder, skip);
                if (Hash(installed, folder, skip) is { } hash) stamped[folder] = hash;
                else stamped.Remove(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                $"Bringing the game's looks into '{mod.Id}' failed (is a file open in another program?): {ex.Message}", FixAction.None, ex);
        }
        WriteStamps(install, stamps);
        mod.ReloadMaterials();
        return chosen.Count;
    }

    /// <summary>Whether an installed mod has material folders at all (a game-only mod such as Vanilla Looks: looks made in game).</summary>
    public static bool HasMaterials(string installedDir) => Folders(installedDir).Any();

    private static void Replace(string from, string to, string folder, IReadOnlySet<string> skip)
    {
        if (!Directory.Exists(from))
        {
            if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
            return;
        }
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(to, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(to, file);
            if (!File.Exists(Path.Combine(from, relative)) && !skip.Contains(Relative(folder, relative))) File.Delete(file);
        }
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            File.SetLastWriteTimeUtc(destination, DateTime.UtcNow); // a copy keeps its source's date: caches keyed by it would not notice
        }
        foreach (var dir in Directory.GetDirectories(to, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    private static bool IsFolder(string folder)
    {
        var parts = folder.Split('/');
        return parts.Length == 3 && parts[0] == MaterialFolders.Root && Kinds.Contains(parts[1])
               && parts[2].Length > 0 && parts[2] is not ("." or "..") && MaterialFolders.Sanitize(parts[2]) == parts[2];
    }

    /// <summary>Every materials/&lt;kind&gt;/&lt;folder&gt; in a mod folder, mod-relative with '/'.</summary>
    private static IEnumerable<string> Folders(string modDir)
    {
        foreach (var kind in Kinds)
        {
            var root = Path.Combine(modDir, MaterialFolders.Root, kind);
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root)) yield return $"{MaterialFolders.Root}/{kind}/{Path.GetFileName(dir)}";
        }
    }

    /// <summary>
    /// The PNGs a slim install left out of the installed copy (the installed pack holds them), mod-relative and lower case:
    /// neither side's hash counts them, so a cooked mask is no change.
    /// </summary>
    private static IReadOnlySet<string> LeftOut(string installedDir)
    {
        var path = Path.Combine(installedDir, TexturePack.FileName);
        var left = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(path) || !HasMaterials(installedDir)) return left;
        try
        {
            using var stream = File.OpenRead(path);
            foreach (var entry in TexturePack.Read(stream).Entries)
            {
                var source = TexturePack.NormalSource(entry.Source);
                if (!File.Exists(Path.Combine(installedDir, source.Replace('/', Path.DirectorySeparatorChar)))) left.Add(source.ToLowerInvariant());
            }
        }
        catch (Exception ex) when (ex is TexturePackException or IOException or UnauthorizedAccessException)
        {
            // an unreadable pack: every file counts
        }
        return left;
    }

    private static string Relative(string folder, string relativeToFolder) => (folder + "/" + relativeToFolder.Replace('\\', '/')).ToLowerInvariant();

    /// <summary>One hash of a material folder's files (not <paramref name="skip"/>); null when the folder is missing.</summary>
    private static string? Hash(string modDir, string folder, IReadOnlySet<string> skip)
    {
        var dir = Path.Combine(modDir, folder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(dir)) return null;
        using var sha = SHA256.Create();
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                     .Select(f => (Path: f, Relative: Relative(folder, Path.GetRelativePath(dir, f))))
                     .Where(f => !skip.Contains(f.Relative))
                     .OrderBy(f => f.Relative, StringComparer.Ordinal))
        {
            var name = Encoding.UTF8.GetBytes(file.Relative + "\n");
            sha.TransformBlock(name, 0, name.Length, null, 0);
            var content = GameMods.FileHash(file.Path);
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static Dictionary<string, Dictionary<string, string>> ReadStamps(GameInstall install)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var path = StampPath(install);
        if (!File.Exists(path)) return result;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path))?["mods"] is not JsonObject mods) return result;
            foreach (var (id, folders) in mods)
                if (folders is JsonObject map)
                    result[id] = map.Where(f => f.Value is JsonValue v && v.TryGetValue<string>(out _))
                        .ToDictionary(f => f.Key, f => f.Value!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // a hand-broken file: as if nothing were stamped (every folder in the game then counts as changed there)
        }
        return result;
    }

    private static void WriteStamps(GameInstall install, Dictionary<string, Dictionary<string, string>> stamps)
    {
        var mods = new JsonObject();
        foreach (var (id, folders) in stamps.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var map = new JsonObject();
            foreach (var (folder, hash) in folders.OrderBy(p => p.Key, StringComparer.Ordinal)) map[folder] = hash;
            mods[id] = map;
        }
        Directory.CreateDirectory(ModLoaderInstaller.RecordDir(install));
        AtomicFile.Write(StampPath(install), new JsonObject { ["format"] = Format, ["mods"] = mods }.ToJsonString(Indented) + "\n");
    }
}
