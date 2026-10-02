using PK.Core.Install;
using Spectre.Console.Cli;

namespace PK.Cli.Commands;

public sealed class WorkspaceStatusCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings.Workspace);
        var current = GameFingerprint.Compute(install);
        Console.WriteLine($"Workspace : {ws.Dir}");
        Console.WriteLine($"Game      : {install.RootDir}");
        Console.WriteLine($"Build     : {current.BuildGuid}");
        if (ws.Data.Fingerprint != current)
            Console.WriteLine("WARNING   : the game has been updated since this workspace was created; refresh outputs.");

        var stale = ws.StaleOutputs(current);
        if (ws.Data.Outputs.Count == 0) Console.WriteLine("Outputs   : (none yet)");
        foreach (var (name, stamp) in ws.Data.Outputs.OrderBy(kv => kv.Key))
            Console.WriteLine($"  {name,-8} {stamp.CreatedUtc:yyyy-MM-dd HH:mm} UTC{(stale.Contains(name) ? "  (stale)" : "")}");
        return ExitCodes.Ok;
    }
}
