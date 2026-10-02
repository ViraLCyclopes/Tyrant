using PK.Core.Install;
using Spectre.Console.Cli;

namespace PK.Cli.Commands;

public sealed class WorkspaceStatusCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var current = GameFingerprint.Compute(install);
        Console.WriteLine($"Workspace : {ws.Dir}");
        Console.WriteLine($"Game      : {install.RootDir}");
        Console.WriteLine($"Build     : {current.BuildGuid}");

        var stale = ws.StaleOutputs(current);
        if (stale.Count > 0)
            Console.WriteLine("WARNING   : the game has been updated; outputs marked (stale) came from an older build - re-run them.");
        if (ws.Data.Outputs.Count == 0) Console.WriteLine("Outputs   : (none yet)");
        foreach (var (name, stamp) in ws.Data.Outputs.OrderBy(kv => kv.Key))
            Console.WriteLine($"  {name,-8} {stamp.CreatedUtc:yyyy-MM-dd HH:mm} UTC{(stale.Contains(name) ? "  (stale)" : "")}");
        return ExitCodes.Ok;
    }
}
