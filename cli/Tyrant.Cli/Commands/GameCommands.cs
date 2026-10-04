using Spectre.Console.Cli;
using Tyrant.Core.Dumping;
using Tyrant.Framework.Core;

namespace Tyrant.Cli.Commands;

// Tyrant in the game: the same as the Workspace tab's "Tyrant in the game" card.

public sealed class GameStatusCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (_, install) = CliServices.OpenWorkspace(settings);
        var state = ModLoaderInstaller.FrameworkStatus(install, CliServices.GameModsDir);
        var inGame = state == FrameworkState.Missing ? "not installed" : ModLoaderInstaller.InstalledFrameworkVersion(install) ?? "installed (version unknown)";
        Console.WriteLine($"Framework: {inGame} in the game · {FrameworkInfo.Version} in this Tyrant"
            + (state == FrameworkState.Outdated ? " — out of date: run 'tyrant game update' (or Workspace tab → Update Tyrant in game)." : ""));
        var loader = ModLoaderInstaller.LoaderVersionInGame(install)
            ?? (ModLoaderInstaller.GetState(install) == InstallState.NotInstalled ? "not installed" : "installed");
        Console.WriteLine($"MelonLoader: {loader}");
        return ExitCodes.Ok;
    }
}

public sealed class GamePackageFrameworkCommand : Command<GamePackageFrameworkCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--out <FILE>")]
        [System.ComponentModel.Description("Where to write the zip (default: <workspace>\\exports\\Tyrant-Framework-<version>.zip).")]
        public string? Out { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        // With --out no workspace is needed (the release workflow runs it without the game).
        var target = settings.Out is not null
            ? Path.GetFullPath(settings.Out)
            : Path.Combine(CliServices.OpenWorkspace(settings).Workspace.Dir, "exports", FrameworkPackage.FileName);
        var path = FrameworkPackage.Write(CliServices.GameModsDir, target);
        Console.WriteLine($"Saved the framework zip: {path}. Players unzip it into the game folder after installing MelonLoader.");
        return ExitCodes.Ok;
    }
}
