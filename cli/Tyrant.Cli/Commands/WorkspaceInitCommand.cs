using System.ComponentModel;
using Spectre.Console.Cli;
using CoreWorkspace = Tyrant.Core.Workspaces.Workspace;

namespace Tyrant.Cli.Commands;

public sealed class WorkspaceInitCommand : Command<WorkspaceInitCommand.Settings>
{
    public sealed class Settings : GameSettings
    {
        [CommandArgument(0, "<DIR>")]
        [Description("Folder to create the workspace in.")]
        public string Dir { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var install = CliServices.ResolveInstall(settings.GamePath);
        var ws = CoreWorkspace.Create(settings.Dir, install);
        Console.WriteLine($"Workspace created: {ws.Dir}");
        Console.WriteLine($"Game build       : {ws.Data.Fingerprint!.BuildGuid}");
        return ExitCodes.Ok;
    }
}
