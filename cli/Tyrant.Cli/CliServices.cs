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
        return index;
    }

    public static void PrintError(TyrantException ex)
    {
        var fix = ex.Fix == FixAction.None ? "" : $" (fix: {ex.Fix})";
        Console.Error.WriteLine($"error {ex.Code.ToWire()}: {ex.Message}{fix}");
    }
}

/// <summary>Synchronous progress printer (System.Progress would reorder output).</summary>
internal sealed class ConsoleProgress : IProgress<JobProgress>
{
    public void Report(JobProgress value) => Console.WriteLine($"[{value.Fraction,4:P0}] {value.Message}");
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
