using Tyrant.Core.Dumping;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class DumpUninstallCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (_, install) = CliServices.OpenWorkspace(settings);
        var result = new ModLoaderInstaller(isGameRunning: new SteamLauncher().IsRunning).Uninstall(install);
        Console.WriteLine(result.RemovedLoader ? "Removed MelonLoader and the dumper mod; the game folder is back to vanilla." : "Removed the dumper mod.");
        if (result.Note is not null) Console.WriteLine(result.Note);
        return ExitCodes.Ok;
    }
}
