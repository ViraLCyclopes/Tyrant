using System.Security.Cryptography;
using System.Text;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Textures;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public enum ModInstallState
{
    NotInstalled,
    Installed,

    /// <summary>The workspace copy differs from the installed one (edited since install).</summary>
    Changed,
}

/// <summary>An installed mod as found in &lt;game&gt;/UserData/Tyrant/Mods; Error is set when its mod.json cannot be read.</summary>
public sealed record InstalledMod(string Id, string Name, string Version, int Replacements, int Skins, bool Enabled, string Dir, string? Error)
{
    /// <summary>Species whose model or rig edit a later enabled mod in the load order overrides (the framework uses the later one).</summary>
    public IReadOnlyList<string> Clashes { get; init; } = [];

    /// <summary>"current", "stale" or "none" (see CookedPacks); null when the mod could not be read.</summary>
    public string? Cooked { get; init; }
}

/// <summary>
/// Installs mods into UserData/Tyrant/Mods (owned by Tyrant: the game uninstall removes it whole) and keeps
/// UserData/Tyrant/mods.json — which mods are on, in load order — up to date.
/// </summary>
public sealed class GameMods(Func<GameInstall, bool>? isGameRunning = null)
{
    private const string Staging = ".installing";
    private const string Previous = ".previous";

    public static string ListPath(GameInstall install) => Path.Combine(ModLoaderInstaller.RecordDir(install), ModList.FileName);

    /// <param name="prepare">Runs on the staging copy before it replaces the installed one (Tyrant cooks the textures there); if it throws, nothing is installed.</param>
    public void Install(GameInstall install, ModProject mod, Action<string>? prepare = null)
    {
        RefuseWhileRunning(install, "installing a mod");
        if (!ModLoaderInstaller.HasFramework(install))
            throw new TyrantException(TyrantErrorCode.FrameworkMissing,
                "Tyrant's framework is not in the game yet. Install Tyrant into the game first (Workspace tab), or install the mod from the Mods tab, which does it for you.",
                FixAction.InstallDumper);

        var target = Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id);
        var staging = target + Staging;
        var previous = target + Previous;
        var movedAside = false;
        try
        {
            TryDeleteDirectory(previous); // left by an earlier install that could not clean up
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            CopyDirectory(mod.Dir, staging);
            if (prepare is not null)
            {
                try { prepare(staging); }
                catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException))
                {
                    TryDeleteDirectory(staging);
                    throw;
                }
            }
            // Swap whole folders: a move either happens completely or not at all, so a failure never leaves a half-deleted copy.
            if (Directory.Exists(target))
            {
                Directory.Move(target, previous);
                movedAside = true;
            }
            Directory.Move(staging, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (movedAside && !Directory.Exists(target))
                try { Directory.Move(previous, target); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            TryDeleteDirectory(staging);
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed,
                $"Copying '{mod.Id}' into the game failed (is the game or an editor holding a file?); the installed copy was left as it was: {ex.Message}", FixAction.None, ex);
        }
        TryDeleteDirectory(previous); // the old copy; a locked file there is cleaned up by the next install

        var entries = ReadList(install);
        var at = entries.FindIndex(e => e.Id == mod.Id);
        if (at >= 0) entries[at] = new ModListEntry(mod.Id, true);
        else entries.Add(new ModListEntry(mod.Id, true));
        WriteList(install, entries);
        GameLooks.Stamp(install, mod.Id, target); // what went in, so looks the in-game editor saves later can be told apart
    }

    public void Remove(GameInstall install, string id)
    {
        var target = InstalledFolder(install, id);
        RefuseWhileRunning(install, "removing a mod");
        var entries = ReadList(install);
        var listed = entries.RemoveAll(e => e.Id == id) > 0;
        if (!Directory.Exists(target) && !listed)
            throw new TyrantException(TyrantErrorCode.ModNotFound, $"'{id}' is not installed in the game.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        WriteList(install, entries);
        GameLooks.Forget(install, id);
    }

    public void SetEnabled(GameInstall install, string id, bool enabled)
    {
        if (!Directory.Exists(InstalledFolder(install, id)))
            throw new TyrantException(TyrantErrorCode.ModNotFound, $"'{id}' is not installed in the game.");
        var entries = ReadList(install);
        var at = entries.FindIndex(e => e.Id == id);
        if (at >= 0) entries[at] = new ModListEntry(id, enabled);
        else entries.Add(new ModListEntry(id, enabled));
        WriteList(install, entries);
    }

    public IReadOnlyList<InstalledMod> List(GameInstall install)
    {
        var root = ModLoaderInstaller.ModsDir(install);
        if (!Directory.Exists(root)) return [];
        var entries = ReadList(install);
        var result = new List<InstalledMod>();
        var manifests = new Dictionary<string, ModManifest>(StringComparer.Ordinal);
        foreach (var dir in Directory.GetDirectories(root).Where(d => !d.EndsWith(Staging, StringComparison.Ordinal) && !d.EndsWith(Previous, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var folder = Path.GetFileName(dir);
            var enabled = entries.FirstOrDefault(e => e.Id == folder)?.Enabled ?? true;
            try
            {
                var path = Path.Combine(dir, ModManifest.FileName);
                if (!File.Exists(path)) throw new ManifestException("mod.json is missing.");
                var manifest = ModManifest.Parse(File.ReadAllText(path));
                manifest.Species.AddRange(AnimalDatabaseFile.Read(Path.Combine(dir, AnimalDatabaseFile.FileName)));
                // As the framework does: a bad materials.json is skipped (its log says why) and the rest load.
                MaterialFolders.Load(manifest, dir, []);
                if (enabled) manifests[folder] = manifest;
                result.Add(new InstalledMod(folder, manifest.Name, manifest.Version, manifest.Replace.Count, manifest.Skins.Count, enabled, dir, null) { Cooked = CookedPacks.StateOf(dir, manifest) });
            }
            catch (Exception ex) when (ex is ManifestException or IOException or UnauthorizedAccessException)
            {
                result.Add(new InstalledMod(folder, folder, "", 0, 0, enabled, dir, ex.Message));
            }
        }
        // The framework's load order: mods.json's order, then mods it does not list by name; Vanilla Looks always last.
        var order = entries.Select(e => e.Id).Where(manifests.ContainsKey)
            .Concat(manifests.Keys.Where(id => entries.All(e => e.Id != id)).Order(StringComparer.Ordinal))
            .OrderBy(id => id == ModCatalog.VanillaLooksId)
            .Select(id => (id, manifests[id])).ToList();
        var clashes = ModelTable.Clashes(order);
        var prefabClashes = PrefabTable.Clashes(order.Select(o => (o.id, Path.Combine(root, o.id))).ToList());
        var materialClashes = MaterialTable.Clashes(order);
        IEnumerable<string> ClashesOf(string id) =>
            clashes.Where(c => c.Loser == id).Select(c => c.Message).Concat(prefabClashes.Where(c => c.Loser == id).Select(c => c.Message))
                .Concat(materialClashes.Where(c => c.Loser == id).Select(c => c.Message));
        return result.OrderBy(m => m.Id == ModCatalog.VanillaLooksId) // listed where it loads
            .Select(m => ClashesOf(m.Id).Any() ? m with { Clashes = ClashesOf(m.Id).ToList() } : m).ToList();
    }

    public ModInstallState StateOf(GameInstall install, ModProject mod)
    {
        var target = Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id);
        if (!Directory.Exists(target)) return ModInstallState.NotInstalled;
        var pack = TryReadPack(Path.Combine(target, TexturePack.FileName));
        // The PNGs a slim install left out: compared through the installed pack, not file by file.
        var inPack = pack?.Entries.Select(e => TexturePack.NormalSource(e.Source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var covered = inPack is null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : SlimCopy.Droppable(mod.Manifest).Where(inPack.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ContentHash(target, skip: null) != ContentHash(mod.Dir, skip: covered)) return ModInstallState.Changed;
        return pack is null || CookedPacks.Matches(pack, mod.Manifest, mod.Dir, covered) ? ModInstallState.Installed : ModInstallState.Changed;
    }

    private static TexturePack? TryReadPack(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return TexturePack.Read(stream);
        }
        catch (Exception ex) when (ex is TexturePackException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// UserData/Tyrant/Mods/&lt;id&gt; for an id that is one plain folder name; anything else ("..", ".", a path) is refused
    /// before a single file is touched, so remove can never delete outside the mods folder.
    /// </summary>
    private static string InstalledFolder(GameInstall install, string id)
    {
        var root = Path.GetFullPath(ModLoaderInstaller.ModsDir(install));
        var plain = !string.IsNullOrWhiteSpace(id) && id is not ("." or "..") && !Path.IsPathRooted(id)
                    && Path.GetFileName(id) == id && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        var target = plain ? Path.GetFullPath(Path.Combine(root, id)) : null;
        if (target is null || !string.Equals(Path.GetDirectoryName(target), root, StringComparison.OrdinalIgnoreCase))
            throw new TyrantException(TyrantErrorCode.ModIdInvalid, $"'{id}' is not a mod id: use the name of an installed mod (its folder under UserData/Tyrant/Mods).");
        return target;
    }

    /// <summary>File hashes by path, size and write time: listing mods re-reads only files that changed since the last call.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime Written, byte[] Hash)> FileHashCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> HashReads = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times a file was actually read for hashing (tests check the cache).</summary>
    internal static int TimesHashed(string path) => HashReads.TryGetValue(Path.GetFullPath(path), out var n) ? n : 0;

    /// <summary>
    /// One hash of a mod folder's files (not textures.tpack, auto thumbnails or <paramref name="skip"/>, which a slim install
    /// compares through its pack); mod.json is hashed as compared (<see cref="ManifestHash"/>).
    /// </summary>
    private static string ContentHash(string dir, IReadOnlySet<string>? skip)
    {
        using var sha = SHA256.Create();
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                     .Where(f => !AnimalDatabaseFile.IsAuxiliary(f))
                     .Select(f => (Path: f, Relative: Path.GetRelativePath(dir, f).Replace('\\', '/').ToLowerInvariant()))
                     .Where(f => !string.Equals(f.Relative, TexturePack.FileName, StringComparison.OrdinalIgnoreCase))
                     .Where(f => (skip is null || !skip.Contains(f.Relative)) && !SlimCopy.IsAutoThumbnail(f.Relative))
                     .OrderBy(f => f.Relative, StringComparer.Ordinal))
        {
            var name = Encoding.UTF8.GetBytes(file.Relative + "\n");
            sha.TransformBlock(name, 0, name.Length, null, 0);
            var content = file.Relative == AnimalDatabaseFile.FileName
                ? SHA256.HashData(Encoding.UTF8.GetBytes(AnimalDatabaseFile.Snapshot(AnimalDatabaseFile.Read(file.Path))))
                : file.Relative == ModManifest.FileName ? ManifestHash(file.Path)
                : FileHash(file.Path);
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    /// <summary>mod.json as compared: parsed, auto thumbnails taken off, written again (install's rewrite is not a change).</summary>
    private static byte[] ManifestHash(string path)
    {
        try
        {
            var manifest = ModManifest.Parse(File.ReadAllText(path));
            SlimCopy.WithoutAutoThumbnails(manifest);
            return SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToJson()));
        }
        catch (ManifestException) { return FileHash(path); }
    }

    internal static byte[] FileHash(string path)
    {
        var info = new FileInfo(path);
        if (FileHashCache.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Written == info.LastWriteTimeUtc) return cached.Hash;
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        HashReads.AddOrUpdate(Path.GetFullPath(path), 1, (_, n) => n + 1);
        FileHashCache[path] = (info.Length, info.LastWriteTimeUtc, hash);
        return hash;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // still in use; the next install retries
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (AnimalDatabaseFile.IsAuxiliary(file)) continue;
            if (string.Equals(Path.GetRelativePath(from, file), TexturePack.FileName, StringComparison.OrdinalIgnoreCase)) continue; // only prepare writes the pack
            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Path.GetRelativePath(from, file) == AnimalDatabaseFile.FileName) AnimalDatabaseFile.CopySnapshot(file, destination);
            else File.Copy(file, destination);
        }
        Directory.CreateDirectory(to);
    }

    private static List<ModListEntry> ReadList(GameInstall install)
    {
        var path = ListPath(install);
        if (!File.Exists(path)) return [];
        try
        {
            return ModList.Parse(File.ReadAllText(path));
        }
        catch (FormatException)
        {
            return []; // a hand-broken list is rewritten from what Tyrant knows
        }
    }

    private static void WriteList(GameInstall install, IEnumerable<ModListEntry> entries)
    {
        Directory.CreateDirectory(ModLoaderInstaller.RecordDir(install));
        File.WriteAllText(ListPath(install), ModList.ToJson(entries));
    }

    private void RefuseWhileRunning(GameInstall install, string action)
    {
        if (isGameRunning?.Invoke(install) == true)
            throw new TyrantException(TyrantErrorCode.GameRunning, $"Prehistoric Kingdom is running; close the game before {action}.");
    }
}
