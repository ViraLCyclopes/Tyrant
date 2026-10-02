using System.ComponentModel;
using PK.Core.Errors;
using PK.Core.Install;
using PK.Core.Jobs;
using Spectre.Console.Cli;
using CoreWorkspace = PK.Core.Workspaces.Workspace;

namespace PK.Cli;

internal static class CliServices
{
    public static GameInstall ResolveInstall(string? gamePath)
    {
        var locator = new GameInstallLocator(new RegistrySteamRootProvider());
        return gamePath is null ? locator.Detect() : locator.FromPath(gamePath);
    }

    public static (CoreWorkspace Workspace, GameInstall Install) OpenWorkspace(string dir)
    {
        var ws = CoreWorkspace.Open(dir);
        return (ws, ResolveInstall(ws.Data.GameRoot));
    }

    public static void PrintError(PkException ex)
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

public class WorkspaceSettings : CommandSettings
{
    [CommandOption("-w|--workspace <DIR>")]
    [Description("Workspace folder (default: current folder).")]
    public string Workspace { get; set; } = ".";
}
