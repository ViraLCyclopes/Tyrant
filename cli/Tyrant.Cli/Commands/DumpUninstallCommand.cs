using Tyrant.Core.Dumping;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class DumpUninstallCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (_, install) = CliServices.OpenWorkspace(settings);
        var result = new ModLoaderInstaller(isGameRunning: CliServices.Launcher.IsRunning).Uninstall(install);
        Console.WriteLine(ModLoaderInstaller.UninstallSummary(result));
        return ExitCodes.Ok;
    }
}
