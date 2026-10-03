using System.Security.Cryptography;
using System.Text;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
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
public sealed record InstalledMod(string Id, string Name, string Version, int Replacements, bool Enabled, string Dir, string? Error);

/// <summary>
/// Installs mods into UserData/Tyrant/Mods (owned by Tyrant: the game uninstall removes it whole) and keeps
/// UserData/Tyrant/mods.json — which mods are on, in load order — up to date.
/// </summary>
public sealed class GameMods(Func<GameInstall, bool>? isGameRunning = null)
{
    private const string Staging = ".installing";

    public static string ListPath(GameInstall install) => Path.Combine(ModLoaderInstaller.RecordDir(install), ModList.FileName);

    public void Install(GameInstall install, ModProject mod)
    {
        RefuseWhileRunning(install, "installing a mod");
        if (!ModLoaderInstaller.HasFramework(install))
            throw new TyrantException(TyrantErrorCode.FrameworkMissing,
                "Tyrant's framework is not in the game yet. Install Tyrant into the game first (Home tab), or install the mod from the Mods tab, which does it for you.",
                FixAction.InstallDumper);

        var target = Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id);
        var staging = target + Staging;
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            CopyDirectory(mod.Dir, staging);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true); // a reinstall leaves no stale files
            Directory.Move(staging, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed,
                $"Copying '{mod.Id}' into the game failed (is the game or an editor holding a file?): {ex.Message}", FixAction.None, ex);
        }

        var entries = ReadList(install);
        var at = entries.FindIndex(e => e.Id == mod.Id);
        if (at >= 0) entries[at] = new ModListEntry(mod.Id, true);
        else entries.Add(new ModListEntry(mod.Id, true));
        WriteList(install, entries);
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
        foreach (var dir in Directory.GetDirectories(root).Where(d => !d.EndsWith(Staging, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var folder = Path.GetFileName(dir);
            var enabled = entries.FirstOrDefault(e => e.Id == folder)?.Enabled ?? true;
            try
            {
                var path = Path.Combine(dir, ModManifest.FileName);
                if (!File.Exists(path)) throw new ManifestException("mod.json is missing.");
                var manifest = ModManifest.Parse(File.ReadAllText(path));
                result.Add(new InstalledMod(folder, manifest.Name, manifest.Version, manifest.Replace.Count, enabled, dir, null));
            }
            catch (Exception ex) when (ex is ManifestException or IOException or UnauthorizedAccessException)
            {
                result.Add(new InstalledMod(folder, folder, "", 0, enabled, dir, ex.Message));
            }
        }
        return result;
    }

    public ModInstallState StateOf(GameInstall install, ModProject mod)
    {
        var target = Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id);
        if (!Directory.Exists(target)) return ModInstallState.NotInstalled;
        return ContentHash(target) == ContentHash(mod.Dir) ? ModInstallState.Installed : ModInstallState.Changed;
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

    private static string ContentHash(string dir)
    {
        using var sha = SHA256.Create();
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                     .Select(f => (Path: f, Relative: Path.GetRelativePath(dir, f).Replace('\\', '/').ToLowerInvariant()))
                     .OrderBy(f => f.Relative, StringComparer.Ordinal))
        {
            var name = Encoding.UTF8.GetBytes(file.Relative + "\n");
            sha.TransformBlock(name, 0, name.Length, null, 0);
            var content = File.ReadAllBytes(file.Path);
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
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
