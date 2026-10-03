using System.ComponentModel;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Workspaces;
using Spectre.Console.Cli;
using CoreWorkspace = Tyrant.Core.Workspaces.Workspace;

namespace Tyrant.Cli;

internal static class CliServices
{
    public static GameInstall ResolveInstall(string? gamePath)
    {
        var locator = new GameInstallLocator(new RegistrySteamRootProvider());
        return gamePath is null ? locator.Detect() : locator.FromPath(gamePath);
    }

    /// <summary>Opens a workspace; a --game override re-points it at a moved install and is saved.</summary>
    public static (CoreWorkspace Workspace, GameInstall Install) OpenWorkspace(WorkspaceSettings settings)
    {
        var (ws, install, repointed) = WorkspaceOpener.Open(settings.Workspace, settings.GamePath, new GameInstallLocator(new RegistrySteamRootProvider()));
        if (repointed) Console.WriteLine($"Workspace now points at: {install.RootDir}");
        return (ws, install);
    }

    public static AssetIndex LoadIndex(CoreWorkspace ws, GameInstall install)
    {
        var index = AssetIndex.Load(AssetIndex.PathIn(ws));
        if (index.Fingerprint != GameFingerprint.Compute(install))
            Console.Error.WriteLine("warning: the asset index is from an older game build; run 'tyrant assets index' to refresh it.");
        else if (index.IsOutdatedFormat)
            Console.Error.WriteLine("warning: the asset index was made by an older Tyrant; run 'tyrant assets index' to refresh it (textured previews need it).");
        return index;
    }

    public static void PrintError(TyrantException ex)
    {
        var fix = ex.Fix switch
        {
            FixAction.PickGameFolder => " (fix: point Tyrant at the game folder with --game <folder>)",
            FixAction.PickWorkspaceFolder => " (fix: use a workspace folder outside the game folder, e.g. -w D:\\tyrant-workspace)",
            FixAction.RefreshWorkspace => " (fix: refresh the workspace: 'tyrant decompile', 'tyrant assets index' or 'tyrant dump run')",
            FixAction.InstallDumper => " (fix: run 'tyrant dump install')",
            _ => "",
        };
        Console.Error.WriteLine($"error {ex.Code.ToWire()}: {ex.Message}{fix}");
    }
}

/// <summary>
/// Synchronous progress printer (System.Progress would reorder output). It prints when the percentage changes or a second
/// has passed, so a job with thousands of steps doesn't print thousands of lines while a slow one still shows it is alive.
/// </summary>
public sealed class ConsoleProgress(TextWriter? output = null, Func<DateTime>? clock = null) : IProgress<JobProgress>
{
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private int _lastPercent = -1;
    private DateTime _lastPrinted = DateTime.MinValue;

    public void Report(JobProgress value)
    {
        var percent = (int)Math.Floor(Math.Clamp(value.Fraction, 0, 1) * 100);
        var now = _clock();
        if (percent == _lastPercent && now - _lastPrinted < TimeSpan.FromSeconds(1)) return;
        (_lastPercent, _lastPrinted) = (percent, now);
        (output ?? Console.Out).WriteLine($"[{value.Fraction,4:P0}] {value.Message}");
    }
}

public class GameSettings : CommandSettings
{
    [CommandOption("--game <PATH>")]
    [Description("Game folder or path to 'Prehistoric Kingdom.exe'. Skips auto-detection.")]
    public string? GamePath { get; set; }
}

public class WorkspaceSettings : GameSettings
{
    [CommandOption("-w|--workspace <DIR>")]
    [Description("Workspace folder (default: current folder).")]
    public string Workspace { get; set; } = ".";
}
