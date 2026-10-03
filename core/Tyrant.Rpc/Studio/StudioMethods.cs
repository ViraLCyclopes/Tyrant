using System.Runtime.InteropServices;
using System.Text;
using Tyrant.Core.Assets;
using Tyrant.Core.Decompile;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Studio;

/// <summary>app.*, install.*, workspace.*, dump.*, decompile.run and assets.index.</summary>
public sealed class StudioMethods(StudioSession session, JobManager jobs)
{
    public const int ProtocolVersion = 1;

    public static string AppVersion => typeof(StudioMethods).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private StudioOptions Options => session.Options;

    [RpcMethod("app.info")]
    public AppInfo Info() => new(AppVersion, ProtocolVersion, $".NET {Environment.Version}");

    [RpcMethod("install.detect")]
    public InstallInfo Detect(InstallDetectParams p)
    {
        var install = Locate(p.GamePath);
        return new InstallInfo(install.RootDir, install.SteamAppId, GameFingerprint.Compute(install).BuildGuid);
    }

    [RpcMethod("workspace.create")]
    public WorkspaceStatus Create(WorkspaceOpenParams p)
    {
        var install = Locate(p.GamePath);
        var ws = Workspace.Create(p.Dir, install);
        session.Set(ws, install);
        session.Log($"Created the workspace for {install.RootDir}.");
        return StatusOf(ws, install, Options.DumperDir);
    }

    [RpcMethod("workspace.open")]
    public WorkspaceStatus Open(WorkspaceOpenParams p)
    {
        var (ws, install, repointed) = WorkspaceOpener.Open(p.Dir, p.GamePath, Options.Locator);
        session.Set(ws, install);
        if (repointed) session.Log($"The workspace now points at {install.RootDir}.");
        return StatusOf(ws, install, Options.DumperDir);
    }

    [RpcMethod("workspace.status")]
    public WorkspaceStatus Status()
    {
        var (ws, install) = session.Current();
        return StatusOf(ws, install, Options.DumperDir);
    }

    [RpcMethod("dump.install", JobResult = typeof(DumperInstallResult))]
    public JobStarted DumpInstall()
    {
        var (ws, install) = session.Current();
        return jobs.Start("Install dumper", (progress, ct) =>
        {
            var before = ModLoaderInstaller.GetState(install);
            string? zip = null;
            if (before == InstallState.NotInstalled)
            {
                progress.Report(new JobProgress(0.05, $"Downloading {ModLoaderInstaller.LoaderName} {ModLoaderInstaller.Version}"));
                zip = Options.LoaderZip(ws, ct);
            }
            progress.Report(new JobProgress(0.6, "Installing into the game folder"));
            var record = Options.Installer().Install(install, zip, Options.DumperDir);
            progress.Report(new JobProgress(1, "Installed"));
            return new DumperInstallResult(record.InstalledLoader, ModLoaderInstaller.InstallSummary(before, record));
        });
    }

    [RpcMethod("dump.uninstall")]
    public DumperUninstallResult DumpUninstall()
    {
        var (_, install) = session.Current();
        EnsureNoJob();
        var result = Options.Installer().Uninstall(install);
        session.Log(ModLoaderInstaller.UninstallSummary(result));
        return new DumperUninstallResult(result.RemovedLoader, ModLoaderInstaller.UninstallSummary(result));
    }

    [RpcMethod("dump.run", JobResult = typeof(DumpRunResult))]
    public JobStarted DumpRun(DumpRunParams p)
    {
        var (ws, install) = session.Current();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(p.TimeoutSeconds, 30, 3600));
        return jobs.Start("Data dump", (progress, ct) => RunDump(ws, install, timeout, progress, ct));
    }

    [RpcMethod("decompile.run", JobResult = typeof(DecompileRunResult))]
    public JobStarted Decompile(DecompileParams p)
    {
        var (ws, install) = session.Current();
        return jobs.Start("Decompile", (progress, ct) => RunDecompile(ws, install, p.Assemblies, progress, ct));
    }

    [RpcMethod("assets.index", JobResult = typeof(AssetIndexRunResult))]
    public JobStarted IndexAssets()
    {
        var (ws, install) = session.Current();
        return jobs.Start("Asset index", (progress, ct) => RunIndex(ws, install, progress, ct));
    }

    [RpcMethod("workspace.refreshAll", JobResult = typeof(RefreshAllResult))]
    public JobStarted RefreshAll()
    {
        var (ws, install) = session.Current();
        return jobs.Start("Refresh all", (progress, ct) =>
        {
            var steps = new List<RefreshStep>();
            steps.Add(Step("Decompile", () =>
            {
                var result = RunDecompile(ws, install, null, new ScaledProgress(progress, 0, 0.3), ct);
                var failed = result.Assemblies.Where(a => !a.Success).ToList();
                return failed.Count == 0
                    ? (RefreshStepStatus.Ok, $"{result.Assemblies.Count} assemblies decompiled.")
                    : (RefreshStepStatus.Failed, $"{result.Assemblies.Count - failed.Count} of {result.Assemblies.Count} decompiled; {string.Join(" ", failed.Select(f => f.Error))}");
            }));
            steps.Add(Step("Asset index", () =>
            {
                var result = RunIndex(ws, install, new ScaledProgress(progress, 0.3, 0.6), ct);
                if (result.Assets == 0)
                    return (RefreshStepStatus.Failed, "No assets were found in the game's Addressables folder (StreamingAssets/aa).");
                return (RefreshStepStatus.Ok, $"{result.Assets:N0} assets indexed{(result.Failures > 0 ? $"; {result.Failures} bundles could not be read" : "")}.");
            }));
            steps.Add(Step("Data dump", () =>
            {
                if (ModLoaderInstaller.GetState(install) != InstallState.Installed)
                    return (RefreshStepStatus.Skipped, "The dumper is not installed; install it on the Home tab to include game data.");
                var result = RunDump(ws, install, TimeSpan.FromMinutes(5), new ScaledProgress(progress, 0.6, 1.0), ct);
                return (RefreshStepStatus.Ok, $"{result.Objects:N0} objects of {result.Types} types dumped.");
            }));
            progress.Report(new JobProgress(1, "Done"));
            return new RefreshAllResult(steps);
        });
    }

    [RpcMethod("app.diagnostics")]
    public DiagnosticsResult Diagnostics()
    {
        var text = new StringBuilder();
        text.AppendLine($"Tyrant diagnostics ({DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC)");
        text.AppendLine($"tyrant {AppVersion}, protocol {ProtocolVersion}, .NET {Environment.Version}, {RuntimeInformation.OSDescription}");
        try
        {
            var (ws, install) = session.Current();
            var fingerprint = GameFingerprint.Compute(install);
            text.AppendLine($"Game: {install.RootDir} (Steam app {install.SteamAppId ?? "unknown"})");
            text.AppendLine($"Build: {fingerprint.BuildGuid}, Assembly-CSharp SHA-256 {fingerprint.AssemblySha256}");
            text.AppendLine($"Workspace: {ws.Dir}");
            var stale = ws.StaleOutputs(fingerprint);
            foreach (var (name, stamp) in ws.Outputs().OrderBy(kv => kv.Key, StringComparer.Ordinal))
                text.AppendLine($"  {name}: {stamp.CreatedUtc:yyyy-MM-dd HH:mm} UTC{(stale.Contains(name) ? " (stale)" : "")}");
            text.AppendLine($"Dumper: {ModLoaderInstaller.GetState(install)}");
            if (DumpManifestFile.TryRead(Path.Combine(ws.DataDir, "manifest.json")) is { } dump)
            {
                text.AppendLine($"Data dump: {dump.CreatedUtc}, build {dump.BuildGuid}, dumper {dump.DumperVersion}, {dump.Counts.Values.Sum()} objects, {dump.Errors.Count} error(s)");
                foreach (var error in dump.Errors.Take(30)) text.AppendLine($"  {error}");
                if (dump.Errors.Count > 30) text.AppendLine($"  ... and {dump.Errors.Count - 30} more (data/manifest.json)");
            }
            AppendTail(text, "Studio log", Path.Combine(ws.LogsDir, StudioSession.LogFileName));
            AppendTail(text, "MelonLoader log", ModLoaderInstaller.LogPath(install));
        }
        catch (Exception ex) when (ex is TyrantException or IOException)
        {
            text.AppendLine($"No workspace: {ex.Message}");
        }
        return new DiagnosticsResult(text.ToString());
    }

    internal static WorkspaceStatus StatusOf(Workspace ws, GameInstall install, string componentDir)
    {
        var current = GameFingerprint.Compute(install);
        var stale = ws.StaleOutputs(current);
        var outputs = ws.Outputs().OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new OutputStatus(kv.Key, kv.Value.CreatedUtc, stale.Contains(kv.Key)))
            .ToList();
        return new WorkspaceStatus(ws.Dir, install.RootDir, install.SteamAppId, current.BuildGuid, stale.Count > 0, outputs,
            ModLoaderInstaller.GetState(install),
            File.Exists(Path.Combine(ws.DataDir, "manifest.json")),
            File.Exists(AssetIndex.PathIn(ws)),
            Directory.Exists(ws.SourceDir) && Directory.EnumerateDirectories(ws.SourceDir).Any(),
            ModLoaderInstaller.FrameworkStatus(install, componentDir));
    }

    private GameInstall Locate(string? gamePath) => gamePath is null ? Options.Locator.Detect() : Options.Locator.FromPath(gamePath);

    private void EnsureNoJob()
    {
        if (jobs.RunningJobId is not null)
            throw new TyrantException(TyrantErrorCode.JobRunning, "A task is still running; wait for it to finish or cancel it first.");
    }

    private DumpRunResult RunDump(Workspace ws, GameInstall install, TimeSpan timeout, IProgress<JobProgress> progress, CancellationToken ct)
    {
        var manifest = new DumpRunner(Options.Launcher).Run(install, ws, timeout, progress, ct);
        session.NotifyDataChanged();
        return new DumpRunResult(manifest.Counts.Values.Sum(), manifest.Counts.Count, manifest.Languages.Count, manifest.Errors);
    }

    private static DecompileRunResult RunDecompile(Workspace ws, GameInstall install, IReadOnlyList<string>? assemblies,
        IProgress<JobProgress> progress, CancellationToken ct)
    {
        var result = new DecompileService().DecompileGame(install, ws, assemblies, progress, ct);
        return new DecompileRunResult(result.Assemblies.Select(a => new DecompileItem(a.AssemblyName, a.Success, a.Error)).ToList());
    }

    private static AssetIndexRunResult RunIndex(Workspace ws, GameInstall install, IProgress<JobProgress> progress, CancellationToken ct)
    {
        var index = new AssetIndexer().BuildAndSave(install, ws, progress, ct);
        return new AssetIndexRunResult(index.Assets.Count, index.Failures.Count, index.MissingBundles.Count);
    }

    /// <summary>Runs one refresh step; a failure is reported in the result so later steps still run. Cancellation stops everything.</summary>
    private static RefreshStep Step(string name, Func<(RefreshStepStatus Status, string Message)> run)
    {
        try
        {
            var (status, message) = run();
            return new RefreshStep(name, status, message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RefreshStep(name, RefreshStepStatus.Failed, RpcErrors.From(ex, out _).Message);
        }
    }

    private static void AppendTail(StringBuilder text, string title, string path, int lines = 40)
    {
        text.AppendLine($"--- {title} ({path}) ---");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            foreach (var line in reader.ReadToEnd().Split('\n').TakeLast(lines)) text.AppendLine(line.TrimEnd('\r'));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text.AppendLine("(none)");
        }
    }
}
