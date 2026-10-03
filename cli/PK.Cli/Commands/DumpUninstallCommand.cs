using PK.Core.Dumping;
using Spectre.Console.Cli;

namespace PK.Cli.Commands;

public sealed class DumpUninstallCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (_, install) = CliServices.OpenWorkspace(settings);
        var result = new BepInExInstaller().Uninstall(install);
        Console.WriteLine(result.RemovedBepInEx ? "Removed BepInEx and the dumper; the game folder is back to vanilla." : "Removed the dumper.");
        if (result.Note is not null) Console.WriteLine(result.Note);
        return ExitCodes.Ok;
    }
}
