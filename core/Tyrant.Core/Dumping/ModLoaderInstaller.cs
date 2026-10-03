using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Dumping;

public enum FrameworkState
{
    Missing,
    Current,

    /// <summary>The game's Tyrant.Framework*.dll differ from the ones this Tyrant ships (Tyrant was updated).</summary>
    Outdated,
}

public enum InstallState
{
    /// <summary>No mod loader and no proxy DLL.</summary>
    NotInstalled,

    /// <summary>Someone's MelonLoader is present but our mod is not.</summary>
    LoaderOnly,

    /// <summary>Our mod (and its install record) is present.</summary>
    Installed,

    /// <summary>Another loader (BepInEx) or an unknown version.dll proxy — must not be touched.</summary>
    Conflict,
}

/// <summary>What we added to the game folder; stored in UserData/Tyrant/install.json.</summary>
public sealed record InstallRecord(bool InstalledLoader, List<string> Files, List<string> CreatedDirs);

public sealed record UninstallResult(bool RemovedLoader, string? Note);

/// <summary>
/// Installs MelonLoader (pinned, checksum-verified official release) and Tyrant's game mods (the dumper and the modding framework), reversibly. Installs roll back on
/// failure; uninstall deletes its record last so a failed uninstall can be retried.
/// </summary>
public sealed class ModLoaderInstaller(string expectedSha256 = ModLoaderInstaller.Sha256, Func<GameInstall, bool>? isGameRunning = null)
{
    public const string LoaderName = "MelonLoader";
    public const string Version = "0.7.3";
    public const string ZipFileName = "MelonLoader.x64.zip";
    public const string DownloadUrl = "https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/" + ZipFileName;
    public const string Sha256 = "5b2b2f3d1cd42b59ec886c5bdc2663edae87a0097a4f4a8f58c0965a99dda416";

    private const string ModFile = "Tyrant.Dumper.dll";
    public const string FrameworkFile = "Tyrant.Framework.dll";

    /// <summary>The DLLs that go into Mods/ (MelonLoader loads them); every other Tyrant.*.dll is a library for UserLibs/.</summary>
    private static readonly HashSet<string> MelonModFiles = new(StringComparer.OrdinalIgnoreCase) { ModFile, FrameworkFile };
    private const string RecordFile = "install.json";
    private const string RequestFile = "tyrant.dumper.request.json";

    /// <summary>Folders MelonLoader uses at runtime; pre-created on a fresh install so uninstall can remove them.</summary>
    private static readonly string[] RuntimeDirs = ["Mods", "Plugins", "UserData", "UserLibs"];

    /// <summary>Entries MelonLoader itself (or we) put in UserData; anything else belongs to another mod.</summary>
    private static readonly HashSet<string> KnownUserData = new(StringComparer.OrdinalIgnoreCase)
    {
        "MelonPreferences.cfg", "Loader.cfg", "MelonStartScreen", "Tyrant", RequestFile,
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string RecordDir(GameInstall install) => Path.Combine(install.RootDir, "UserData", "Tyrant");

    /// <summary>Where installed Tyrant mods live: &lt;game&gt;/UserData/Tyrant/Mods/&lt;id&gt;.</summary>
    public static string ModsDir(GameInstall install) => Path.Combine(RecordDir(install), "Mods");

    public static bool HasFramework(GameInstall install) =>
        File.Exists(Path.Combine(install.RootDir, "Mods", FrameworkFile)) && File.Exists(Path.Combine(RecordDir(install), RecordFile));

    /// <summary>Compares the framework DLLs Tyrant ships (componentDir) with the game's, by SHA-256.</summary>
    public static FrameworkState FrameworkStatus(GameInstall install, string componentDir)
    {
        if (!HasFramework(install)) return FrameworkState.Missing;
        var shipped = Directory.Exists(componentDir) ? Directory.GetFiles(componentDir, "Tyrant.Framework*.dll") : [];
        foreach (var file in shipped)
        {
            var name = Path.GetFileName(file);
            var installed = Path.Combine(install.RootDir, MelonModFiles.Contains(name) ? "Mods" : "UserLibs", name);
            if (!File.Exists(installed) || HashOf(installed) != HashOf(file)) return FrameworkState.Outdated;
        }
        return FrameworkState.Current;
    }

    public static string RequestPath(GameInstall install) => Path.Combine(install.RootDir, "UserData", RequestFile);

    public static string LogPath(GameInstall install) => Path.Combine(install.RootDir, "MelonLoader", "Latest.log");

    /// <summary>One-line result of an install, shared by the CLI and the app.</summary>
    public static string InstallSummary(InstallState before, InstallRecord record) => before switch
    {
        InstallState.Installed => "Updated Tyrant's dumper and framework; they were already installed.",
        InstallState.NotInstalled => $"Installed {LoaderName} {Version} and Tyrant's dumper and framework ({record.Files.Count} files added to the game folder).",
        _ => $"Added Tyrant's dumper and framework to your existing {LoaderName}.",
    };

    /// <summary>One-line result of an uninstall, shared by the CLI and the app.</summary>
    public static string UninstallSummary(UninstallResult result) =>
        (result.RemovedLoader
            ? "Removed MelonLoader, Tyrant's dumper and framework, and the installed mods; the game folder is back to vanilla."
            : "Removed Tyrant's dumper, framework and installed mods.")
        + (result.Note is null ? "" : " " + result.Note);

    public static InstallState GetState(GameInstall install)
    {
        var root = install.RootDir;
        if (File.Exists(Path.Combine(RecordDir(install), RecordFile))) return InstallState.Installed;
        var hasBepInEx = File.Exists(Path.Combine(root, "BepInEx", "core", "BepInEx.dll")) || File.Exists(Path.Combine(root, "winhttp.dll"));
        if (hasBepInEx) return InstallState.Conflict;
        var hasMelonLoader = File.Exists(Path.Combine(root, "version.dll")) && Directory.Exists(Path.Combine(root, "MelonLoader"));
        if (hasMelonLoader) return InstallState.LoaderOnly;
        return File.Exists(Path.Combine(root, "version.dll")) ? InstallState.Conflict : InstallState.NotInstalled;
    }

    public static InstallRecord? ReadRecord(GameInstall install)
    {
        var path = Path.Combine(RecordDir(install), RecordFile);
        if (!File.Exists(path)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path), Json);
            return record is null ? null : record with { Files = record.Files ?? [], CreatedDirs = record.CreatedDirs ?? [] };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Installs MelonLoader from loaderZip when the game has none (the archive is ignored otherwise) plus the dumper mod.</summary>
    public InstallRecord Install(GameInstall install, string? loaderZip, string dumperDir)
    {
        RefuseWhileRunning(install, "installing");
        var state = GetState(install);
        if (state == InstallState.Conflict)
            throw new TyrantException(TyrantErrorCode.ModLoaderConflict,
                "The game folder has another mod loader (BepInEx) or an unknown version.dll. Remove it before installing MelonLoader.");
        if (state == InstallState.NotInstalled)
        {
            if (loaderZip is null)
                throw new TyrantException(TyrantErrorCode.DumperInstallFailed, $"The {LoaderName} {Version} archive is needed to install it.");
            VerifyChecksum(loaderZip);
        }

        var dumperFiles = Directory.Exists(dumperDir)
            ? Directory.GetFiles(dumperDir, "Tyrant.Dumper*.dll").Concat(Directory.GetFiles(dumperDir, "Tyrant.Framework*.dll")).ToArray()
            : [];
        if (!dumperFiles.Any(f => string.Equals(Path.GetFileName(f), ModFile, StringComparison.OrdinalIgnoreCase)))
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed, $"The dumper mod files are missing from '{dumperDir}'. Rebuild Tyrant.");
        if (!dumperFiles.Any(f => string.Equals(Path.GetFileName(f), FrameworkFile, StringComparison.OrdinalIgnoreCase)))
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed, $"{FrameworkFile} is missing from '{dumperDir}'. Rebuild Tyrant.");

        var previous = ReadRecord(install);
        var files = new List<string>(previous?.Files ?? []);
        var createdDirs = new List<string>(previous?.CreatedDirs ?? []);
        var installedLoader = previous?.InstalledLoader ?? false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install.RootDir)) + Path.DirectorySeparatorChar;

        // What this call added, so a failure can be rolled back without touching anything that was there before.
        var addedFiles = new List<string>();
        var addedDirs = new List<string>();
        void EnsureTopDir(string dir)
        {
            if (Directory.Exists(Path.Combine(root, dir)) || createdDirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(Path.Combine(root, dir));
            addedDirs.Add(dir);
        }

        try
        {
            if (state == InstallState.NotInstalled)
            {
                using var zip = ZipFile.OpenRead(loaderZip!);
                var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                foreach (var entry in entries)
                    if (!Path.GetFullPath(Path.Combine(root, entry.FullName)).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new TyrantException(TyrantErrorCode.DumperInstallFailed, $"The MelonLoader archive contains an unsafe path '{entry.FullName}'.");

                foreach (var dir in entries.Select(e => e.FullName.Replace('\\', '/')).Where(n => n.Contains('/')).Select(n => n[..n.IndexOf('/')])
                             .Concat(RuntimeDirs).Distinct(StringComparer.OrdinalIgnoreCase))
                    EnsureTopDir(dir);

                foreach (var entry in entries)
                {
                    var relative = entry.FullName.Replace('\\', '/');
                    var destination = Path.GetFullPath(Path.Combine(root, relative));
                    if (File.Exists(destination)) continue; // never overwrite anything already in the game folder
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                    addedFiles.Add(relative);
                }
                installedLoader = true;
            }

            EnsureTopDir("Mods");
            EnsureTopDir("UserLibs");
            EnsureTopDir("UserData");
            foreach (var file in dumperFiles)
            {
                var name = Path.GetFileName(file);
                var relative = MelonModFiles.Contains(name) ? $"Mods/{name}" : $"UserLibs/{name}";
                var destination = Path.Combine(root, relative);
                var existed = File.Exists(destination);
                File.Copy(file, destination, overwrite: true); // our own files
                if (!existed) addedFiles.Add(relative);
            }

            files.AddRange(addedFiles.Where(f => !files.Contains(f)));
            createdDirs.AddRange(addedDirs);
            var record = new InstallRecord(installedLoader, files, createdDirs);
            Directory.CreateDirectory(RecordDir(install));
            File.WriteAllText(Path.Combine(RecordDir(install), RecordFile), JsonSerializer.Serialize(record, Json));
            return record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or TyrantException)
        {
            RollBack(root, addedFiles, addedDirs);
            if (ex is TyrantException) throw;
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed,
                $"Installing into the game folder failed and was rolled back (antivirus? game running?): {ex.Message}", FixAction.None, ex);
        }
    }

    public UninstallResult Uninstall(GameInstall install)
    {
        RefuseWhileRunning(install, "uninstalling");
        var record = ReadRecord(install)
            ?? throw new TyrantException(TyrantErrorCode.DumperNotInstalled, "Tyrant's dumper is not installed in this game folder.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install.RootDir));
        ValidateRecord(record, root);

        try
        {
            foreach (var file in record.Files.Where(IsOurModFile))
                DeleteFile(Path.Combine(root, file));
            DeleteFile(RequestPath(install));

            UninstallResult result;
            var foreign = ForeignContent(root);
            if (record.InstalledLoader && foreign is null)
            {
                foreach (var dir in record.CreatedDirs.Where(d => !string.Equals(d, "UserData", StringComparison.OrdinalIgnoreCase)))
                {
                    var path = Path.Combine(root, dir);
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true); // ours, including runtime logs/config
                }
                foreach (var file in record.Files)
                    DeleteFile(Path.Combine(root, file));
                result = new UninstallResult(true, null);
            }
            else
            {
                result = new UninstallResult(false, record.InstalledLoader ? $"MelonLoader was kept because {foreign}." : null);
            }

            // The record goes last, so a failure above leaves the install recorded and the uninstall can be retried.
            Directory.Delete(RecordDir(install), recursive: true);
            var userData = Path.Combine(root, "UserData");
            if (record.CreatedDirs.Contains("UserData", StringComparer.OrdinalIgnoreCase) && Directory.Exists(userData)
                && (result.RemovedLoader || !Directory.EnumerateFileSystemEntries(userData).Any()))
                Directory.Delete(userData, recursive: true);
            if (!result.RemovedLoader)
                foreach (var dir in record.CreatedDirs)
                {
                    var path = Path.Combine(root, dir);
                    if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
                }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed,
                $"Uninstalling failed (is the game running?); uninstall again once it is closed (Workspace → Uninstall from game, or 'tyrant dump uninstall'): {ex.Message}", FixAction.None, ex);
        }
    }

    /// <summary>Downloads the pinned MelonLoader release into cacheDir (reused when its checksum matches).</summary>
    public static async Task<string> DownloadAsync(HttpClient http, string cacheDir, CancellationToken ct)
    {
        Directory.CreateDirectory(cacheDir);
        var path = Path.Combine(cacheDir, $"MelonLoader-{Version}.x64.zip");
        if (File.Exists(path) && HashOf(path) == Sha256) return path;
        var tmp = path + ".download";
        try
        {
            await using (var source = await http.GetStreamAsync(DownloadUrl, ct))
            await using (var target = File.Create(tmp))
                await source.CopyToAsync(target, ct);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed, $"Downloading MelonLoader {Version} failed: {ex.Message}", FixAction.None, ex);
        }
    }

    /// <summary>Describes content other mods left in MelonLoader's folders, or null when there is none.</summary>
    private static string? ForeignContent(string root)
    {
        foreach (var dir in new[] { "Mods", "Plugins", "UserLibs" })
        {
            var path = Path.Combine(root, dir);
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any(e => !Path.GetFileName(e).StartsWith("Tyrant.", StringComparison.OrdinalIgnoreCase)))
                return $"other mods have files in {dir}";
        }
        var userData = Path.Combine(root, "UserData");
        if (Directory.Exists(userData) && Directory.EnumerateFileSystemEntries(userData).Any(e => !KnownUserData.Contains(Path.GetFileName(e))))
            return "other mods have files in UserData";
        return null;
    }

    /// <summary>Refuses records that point outside the game folder (damaged or edited install.json).</summary>
    private static void ValidateRecord(InstallRecord record, string root)
    {
        var prefix = root + Path.DirectorySeparatorChar;
        bool Safe(string relative, bool topLevelOnly) =>
            !string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative)
            && !relative.Split('/', '\\').Any(s => s is "" or "." or "..")
            && (!topLevelOnly || !relative.Contains('/') && !relative.Contains('\\'))
            && Path.GetFullPath(Path.Combine(root, relative)).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        if (record.Files.Any(f => !Safe(f, false)) || record.CreatedDirs.Any(d => !Safe(d, true)))
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed,
                "The install record (UserData/Tyrant/install.json) is damaged or points outside the game folder; nothing was removed.");
    }

    private void RefuseWhileRunning(GameInstall install, string action)
    {
        if (isGameRunning?.Invoke(install) == true)
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed, $"Prehistoric Kingdom is running; close the game before {action}.");
    }

    private static void RollBack(string root, List<string> addedFiles, List<string> addedDirs)
    {
        foreach (var file in addedFiles)
            try { DeleteFile(Path.Combine(root, file)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var dir in addedDirs)
            try { if (Directory.Exists(Path.Combine(root, dir))) Directory.Delete(Path.Combine(root, dir), recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsOurModFile(string relative) =>
        relative.StartsWith("Mods/Tyrant.", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("UserLibs/Tyrant.", StringComparison.OrdinalIgnoreCase);

    private static void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private void VerifyChecksum(string zip)
    {
        if (!File.Exists(zip)) throw new TyrantException(TyrantErrorCode.DumperInstallFailed, $"MelonLoader archive '{zip}' not found.");
        if (!string.Equals(HashOf(zip), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed,
                $"The MelonLoader archive failed its checksum (expected the official {Version} release). Nothing was installed.");
    }

    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
