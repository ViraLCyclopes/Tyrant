using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PK.Core.Errors;
using PK.Core.Install;

namespace PK.Core.Dumping;

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

/// <summary>What we added to the game folder; stored in UserData/PKModStudio/install.json.</summary>
public sealed record InstallRecord(bool InstalledLoader, List<string> Files, List<string> CreatedDirs);

public sealed record UninstallResult(bool RemovedLoader, string? Note);

/// <summary>Installs MelonLoader (pinned, checksum-verified official release) and the dumper mod, reversibly.</summary>
public sealed class ModLoaderInstaller(string expectedSha256 = ModLoaderInstaller.Sha256)
{
    public const string LoaderName = "MelonLoader";
    public const string Version = "0.7.3";
    public const string ZipFileName = "MelonLoader.x64.zip";
    public const string DownloadUrl = "https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/" + ZipFileName;
    public const string Sha256 = "5b2b2f3d1cd42b59ec886c5bdc2663edae87a0097a4f4a8f58c0965a99dda416";

    private const string ModFile = "PK.Dumper.dll";
    private const string RecordFile = "install.json";

    /// <summary>Folders MelonLoader uses at runtime; pre-created on a fresh install so uninstall can remove them.</summary>
    private static readonly string[] RuntimeDirs = ["Mods", "Plugins", "UserData", "UserLibs"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string RecordDir(GameInstall install) => Path.Combine(install.RootDir, "UserData", "PKModStudio");

    public static string RequestPath(GameInstall install) => Path.Combine(install.RootDir, "UserData", "pk.dumper.request.json");

    public static string LogPath(GameInstall install) => Path.Combine(install.RootDir, "MelonLoader", "Latest.log");

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
        var state = GetState(install);
        if (state == InstallState.Conflict)
            throw new PkException(PkErrorCode.ModLoaderConflict,
                "The game folder has another mod loader (BepInEx) or an unknown version.dll. Remove it before installing MelonLoader.");
        if (state == InstallState.NotInstalled)
        {
            if (loaderZip is null)
                throw new PkException(PkErrorCode.DumperInstallFailed, $"The {LoaderName} {Version} archive is needed to install it.");
            VerifyChecksum(loaderZip);
        }

        var dumperFiles = Directory.Exists(dumperDir) ? Directory.GetFiles(dumperDir, "PK.Dumper*.dll") : [];
        if (!dumperFiles.Any(f => string.Equals(Path.GetFileName(f), ModFile, StringComparison.OrdinalIgnoreCase)))
            throw new PkException(PkErrorCode.DumperInstallFailed, $"The dumper mod files are missing from '{dumperDir}'. Rebuild PK Mod Studio.");

        var previous = ReadRecord(install);
        var files = new List<string>(previous?.Files ?? []);
        var createdDirs = new List<string>(previous?.CreatedDirs ?? []);
        var installedLoader = previous?.InstalledLoader ?? false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install.RootDir)) + Path.DirectorySeparatorChar;

        try
        {
            if (state == InstallState.NotInstalled)
            {
                using var zip = ZipFile.OpenRead(loaderZip!);
                var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                foreach (var entry in entries)
                    if (!Path.GetFullPath(Path.Combine(root, entry.FullName)).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new PkException(PkErrorCode.DumperInstallFailed, $"The MelonLoader archive contains an unsafe path '{entry.FullName}'.");

                var topDirs = entries.Select(e => e.FullName.Replace('\\', '/')).Where(n => n.Contains('/')).Select(n => n[..n.IndexOf('/')])
                    .Concat(RuntimeDirs).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var dir in topDirs)
                    if (!Directory.Exists(Path.Combine(root, dir)) && !createdDirs.Contains(dir))
                    {
                        Directory.CreateDirectory(Path.Combine(root, dir));
                        createdDirs.Add(dir);
                    }

                foreach (var entry in entries)
                {
                    var relative = entry.FullName.Replace('\\', '/');
                    var destination = Path.GetFullPath(Path.Combine(root, relative));
                    if (File.Exists(destination)) continue; // never overwrite anything already in the game folder
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                    if (!files.Contains(relative)) files.Add(relative);
                }
                installedLoader = true;
            }

            foreach (var file in dumperFiles)
            {
                var name = Path.GetFileName(file);
                var relative = string.Equals(name, ModFile, StringComparison.OrdinalIgnoreCase) ? $"Mods/{name}" : $"UserLibs/{name}";
                var destination = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true); // our own files
                if (!files.Contains(relative)) files.Add(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new PkException(PkErrorCode.DumperInstallFailed, $"Installing into the game folder failed (is the game running?): {ex.Message}", FixAction.None, ex);
        }

        var record = new InstallRecord(installedLoader, files, createdDirs);
        Directory.CreateDirectory(RecordDir(install));
        File.WriteAllText(Path.Combine(RecordDir(install), RecordFile), JsonSerializer.Serialize(record, Json));
        return record;
    }

    public UninstallResult Uninstall(GameInstall install)
    {
        var record = ReadRecord(install)
            ?? throw new PkException(PkErrorCode.DumperNotInstalled, "PK Mod Studio's dumper is not installed in this game folder.");
        var root = install.RootDir;

        try
        {
            foreach (var file in record.Files.Where(IsOurModFile))
                DeleteFile(Path.Combine(root, file));
            Directory.Delete(RecordDir(install), recursive: true);

            if (!record.InstalledLoader)
                return new UninstallResult(false, null);

            var foreign = new[] { "Mods", "Plugins" }
                .Select(d => Path.Combine(root, d))
                .Any(d => Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any());
            if (foreign)
                return new UninstallResult(false, "MelonLoader was kept because other mods or plugins are installed.");

            foreach (var dir in record.CreatedDirs)
            {
                var path = Path.Combine(root, dir);
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true); // ours, including runtime logs/config
            }
            foreach (var file in record.Files)
                DeleteFile(Path.Combine(root, file));
            return new UninstallResult(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PkException(PkErrorCode.DumperInstallFailed, $"Uninstalling failed (is the game running?): {ex.Message}", FixAction.None, ex);
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
            throw new PkException(PkErrorCode.DumperInstallFailed, $"Downloading MelonLoader {Version} failed: {ex.Message}", FixAction.None, ex);
        }
    }

    private static bool IsOurModFile(string relative) =>
        relative.StartsWith("Mods/PK.Dumper", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("UserLibs/PK.Dumper", StringComparison.OrdinalIgnoreCase);

    private static void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private void VerifyChecksum(string zip)
    {
        if (!File.Exists(zip)) throw new PkException(PkErrorCode.DumperInstallFailed, $"MelonLoader archive '{zip}' not found.");
        if (!string.Equals(HashOf(zip), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new PkException(PkErrorCode.DumperInstallFailed,
                $"The MelonLoader archive failed its checksum (expected the official {Version} release). Nothing was installed.");
    }

    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
