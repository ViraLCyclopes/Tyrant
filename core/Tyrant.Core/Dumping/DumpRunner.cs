using System.Diagnostics;
using System.Text.Json;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Dumping;

public interface IGameLauncher
{
    /// <summary>True when the game is already running (Steam would not start a second copy, so no dump could arrive).</summary>
    bool IsRunning(GameInstall install);

    void Launch(GameInstall install);
}

/// <summary>Starts the game through Steam (so Steam DRM and overlays behave normally).</summary>
public sealed class SteamLauncher : IGameLauncher
{
    public bool IsRunning(GameInstall install)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameInstall.ExeName));
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    public void Launch(GameInstall install)
    {
        if (install.SteamAppId is null)
            throw new TyrantException(TyrantErrorCode.DumpFailed,
                "The game's Steam app id is unknown (the game folder is not inside a Steam library), so Tyrant cannot start it for the data dump. Run the game from its Steam library folder.");
        try
        {
            Process.Start(new ProcessStartInfo($"steam://rungameid/{install.SteamAppId}") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new TyrantException(TyrantErrorCode.DumpFailed, $"Could not ask Steam to start the game (is Steam installed?): {ex.Message}", FixAction.None, ex);
        }
    }
}

/// <summary>Asks the installed mod for a dump, starts the game and moves the result into &lt;workspace&gt;/data.</summary>
public sealed class DumpRunner(IGameLauncher launcher, int moveAttempts = 50)
{
    public const string OutputName = "data";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MoveRetryDelay = TimeSpan.FromMilliseconds(100);

    public static string RequestPath(GameInstall install) => ModLoaderInstaller.RequestPath(install);

    public DumpManifestFile Run(GameInstall install, Workspace ws, TimeSpan timeout, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (ModLoaderInstaller.GetState(install) != InstallState.Installed)
            throw new TyrantException(TyrantErrorCode.DumperNotInstalled, "The dumper mod is not installed in the game folder. Install it first (Workspace → Install Tyrant in game, or 'tyrant dump install').",
                FixAction.InstallDumper);

        if (launcher.IsRunning(install))
            throw new TyrantException(TyrantErrorCode.DumpFailed, "Prehistoric Kingdom is already running; close it first, then run the data dump again (Workspace → Run data dump, or 'tyrant dump run').");

        var requestId = Guid.NewGuid().ToString("N");
        var tmp = Path.Combine(ws.Dir, $"data.tmp-{requestId}");
        var fingerprint = GameFingerprint.Compute(install);
        var requestPath = RequestPath(install);
        var success = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
            File.WriteAllText(requestPath, JsonSerializer.Serialize(new
            {
                requestId,
                outputDir = tmp,
                buildGuid = fingerprint.BuildGuid,
                timeoutSeconds = (float)Math.Max(60, timeout.TotalSeconds - 10),
                // The mod ignores a request older than this, so one left behind (crash, power loss) never fires a surprise dump.
                expiresUtc = DateTime.UtcNow.Add(timeout).ToString("o"),
                settleSeconds = 5f,
                quitWhenDone = true,
            }, Json));

            progress?.Report(new JobProgress(0, "Starting the game"));
            launcher.Launch(install);

            var manifestPath = Path.Combine(tmp, "manifest.json");
            var stopwatch = Stopwatch.StartNew();
            DumpManifestFile? manifest;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                manifest = DumpManifestFile.TryRead(manifestPath);
                if (manifest is not null && manifest.RequestId == requestId) break;
                if (stopwatch.Elapsed > timeout)
                    throw new TyrantException(TyrantErrorCode.DumpTimeout,
                        $"No dump arrived within {timeout.TotalSeconds:0} s (is Steam running? did the game start?). Last MelonLoader log lines:{Environment.NewLine}{LogTail(install)}");
                progress?.Report(new JobProgress(Math.Min(0.95, stopwatch.Elapsed / timeout), $"Waiting for the game ({stopwatch.Elapsed.TotalSeconds:0} s)"));
                Thread.Sleep(PollInterval);
            }

            if (manifest.Counts.Count == 0)
                throw new TyrantException(TyrantErrorCode.DumpFailed,
                    "The dumper ran but found no game data: " + string.Join("; ", manifest.Errors.Take(5)));

            var old = ws.DataDir + ".old-" + requestId;
            var hadOld = Directory.Exists(ws.DataDir);
            if (hadOld) MoveWithRetry(ws.DataDir, old, ct);
            try
            {
                MoveWithRetry(tmp, ws.DataDir, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                if (hadOld) MoveWithRetry(old, ws.DataDir, CancellationToken.None); // put the previous dump back
                throw new TyrantException(TyrantErrorCode.DumpFailed,
                    $"The new dump could not be moved into the workspace ({ex.Message}); the previous data was kept. Close anything that has files in the workspace open and run the dump again.", inner: ex);
            }
            if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
            ws.StampOutput(OutputName, fingerprint);
            success = true;
            progress?.Report(new JobProgress(1.0, "Done"));
            return manifest;
        }
        finally
        {
            TryDeleteFile(requestPath);
            if (!success && Directory.Exists(tmp)) TryDeleteDirectory(tmp);
        }
    }

    /// <summary>
    /// Windows refuses to move a folder while any file in it is open (the app may be reading the previous dump), so
    /// retry for a few seconds before giving up rather than throwing away a dump the user waited for.
    /// </summary>
    private void MoveWithRetry(string source, string destination, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < moveAttempts)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(MoveRetryDelay);
            }
        }
    }

    private static string LogTail(GameInstall install)
    {
        var log = ModLoaderInstaller.LogPath(install);
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            return string.Join(Environment.NewLine, lines.TakeLast(20).Select(l => "  " + l.TrimEnd('\r')));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "  (no MelonLoader log — MelonLoader may not have started)";
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
