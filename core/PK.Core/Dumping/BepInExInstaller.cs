using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PK.Core.Errors;
using PK.Core.Install;

namespace PK.Core.Dumping;

public enum InstallState
{
    /// <summary>No BepInEx and no proxy DLL.</summary>
    NotInstalled,

    /// <summary>Someone's BepInEx is present but our plugin is not.</summary>
    BepInExOnly,

    /// <summary>Our plugin (and its install record) is present.</summary>
    Installed,

    /// <summary>A winhttp.dll proxy exists without BepInEx — another loader we must not touch.</summary>
    Conflict,
}

/// <summary>What we added to the game folder; stored in BepInEx/plugins/PKModStudio/install.json.</summary>
public sealed record InstallRecord(bool InstalledBepInEx, List<string> Files);

public sealed record UninstallResult(bool RemovedBepInEx, string? Note);

/// <summary>Installs BepInEx (from the pinned, checksum-verified release) and the dumper plugin, reversibly.</summary>
public sealed class BepInExInstaller(string expectedSha256 = BepInExInstaller.Sha256)
{
    public const string Version = "5.4.23.5";
    public const string ZipFileName = "BepInEx_win_x64_5.4.23.5.zip";
    public const string DownloadUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/" + ZipFileName;
    public const string Sha256 = "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4";

    private const string PluginRelative = "BepInEx/plugins/PKModStudio";
    private const string RecordFile = "install.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string PluginDir(GameInstall install) => Path.Combine(install.RootDir, "BepInEx", "plugins", "PKModStudio");

    public static InstallState GetState(GameInstall install)
    {
        if (File.Exists(Path.Combine(PluginDir(install), RecordFile))) return InstallState.Installed;
        if (File.Exists(Path.Combine(install.RootDir, "BepInEx", "core", "BepInEx.dll"))) return InstallState.BepInExOnly;
        return File.Exists(Path.Combine(install.RootDir, "winhttp.dll")) ? InstallState.Conflict : InstallState.NotInstalled;
    }

    public static InstallRecord? ReadRecord(GameInstall install)
    {
        var path = Path.Combine(PluginDir(install), RecordFile);
        if (!File.Exists(path)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path), Json);
            return record is null ? null : record with { Files = record.Files ?? [] };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public InstallRecord Install(GameInstall install, string bepInExZip, string dumperDir)
    {
        VerifyChecksum(bepInExZip);
        var state = GetState(install);
        if (state == InstallState.Conflict)
            throw new PkException(PkErrorCode.BepInExConflict,
                "The game folder has a winhttp.dll that is not BepInEx (another mod loader?). Remove it before installing.");

        var dumperFiles = Directory.Exists(dumperDir) ? Directory.GetFiles(dumperDir, "PK.Dumper*.dll") : [];
        if (dumperFiles.Length == 0)
            throw new PkException(PkErrorCode.DumperInstallFailed, $"The dumper plugin files are missing from '{dumperDir}'. Rebuild PK Mod Studio.");

        var previous = ReadRecord(install);
        var added = new List<string>(previous?.Files ?? []);
        var installedBepInEx = previous?.InstalledBepInEx ?? false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install.RootDir)) + Path.DirectorySeparatorChar;

        try
        {
            if (state == InstallState.NotInstalled)
            {
                using var zip = ZipFile.OpenRead(bepInExZip);
                var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                foreach (var entry in entries)
                    if (!Path.GetFullPath(Path.Combine(root, entry.FullName)).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new PkException(PkErrorCode.DumperInstallFailed, $"The BepInEx archive contains an unsafe path '{entry.FullName}'.");

                foreach (var entry in entries)
                {
                    var relative = entry.FullName.Replace('\\', '/');
                    var destination = Path.GetFullPath(Path.Combine(root, relative));
                    if (File.Exists(destination)) continue; // never overwrite anything already in the game folder
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                    if (!added.Contains(relative)) added.Add(relative);
                }
                installedBepInEx = true;
            }

            Directory.CreateDirectory(PluginDir(install));
            foreach (var file in dumperFiles)
            {
                var relative = $"{PluginRelative}/{Path.GetFileName(file)}";
                File.Copy(file, Path.Combine(PluginDir(install), Path.GetFileName(file)), overwrite: true); // our own files
                if (!added.Contains(relative)) added.Add(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new PkException(PkErrorCode.DumperInstallFailed, $"Installing into the game folder failed: {ex.Message}", FixAction.None, ex);
        }

        var record = new InstallRecord(installedBepInEx, added);
        File.WriteAllText(Path.Combine(PluginDir(install), RecordFile), JsonSerializer.Serialize(record, Json));
        return record;
    }

    public UninstallResult Uninstall(GameInstall install)
    {
        var record = ReadRecord(install)
            ?? throw new PkException(PkErrorCode.DumperNotInstalled, "PK Mod Studio's dumper is not installed in this game folder.");
        var bepInExDir = Path.Combine(install.RootDir, "BepInEx");
        var pluginsDir = Path.Combine(bepInExDir, "plugins");

        try
        {
            Directory.Delete(PluginDir(install), recursive: true);
            var foreignPlugins = Directory.Exists(pluginsDir) && Directory.EnumerateFileSystemEntries(pluginsDir).Any();
            if (!record.InstalledBepInEx)
                return new UninstallResult(false, null);
            if (foreignPlugins)
                return new UninstallResult(false, "BepInEx was kept because other plugins are installed in BepInEx/plugins.");

            if (Directory.Exists(bepInExDir)) Directory.Delete(bepInExDir, recursive: true); // ours, including its runtime logs/config
            foreach (var file in record.Files.Where(f => !f.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)))
            {
                var path = Path.Combine(install.RootDir, file);
                if (File.Exists(path)) File.Delete(path);
            }
            return new UninstallResult(true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PkException(PkErrorCode.DumperInstallFailed, $"Uninstalling failed (is the game running?): {ex.Message}", FixAction.None, ex);
        }
    }

    /// <summary>Downloads the pinned BepInEx release into cacheDir (reused when its checksum matches).</summary>
    public static async Task<string> DownloadAsync(HttpClient http, string cacheDir, CancellationToken ct)
    {
        Directory.CreateDirectory(cacheDir);
        var path = Path.Combine(cacheDir, ZipFileName);
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
            throw new PkException(PkErrorCode.DumperInstallFailed, $"Downloading BepInEx {Version} failed: {ex.Message}", FixAction.None, ex);
        }
    }

    private void VerifyChecksum(string zip)
    {
        if (!File.Exists(zip)) throw new PkException(PkErrorCode.DumperInstallFailed, $"BepInEx archive '{zip}' not found.");
        if (!string.Equals(HashOf(zip), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new PkException(PkErrorCode.DumperInstallFailed,
                $"The BepInEx archive failed its checksum (expected the official {Version} release). Nothing was installed.");
    }

    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
