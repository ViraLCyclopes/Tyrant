using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

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
        var indexPath = AssetIndex.PathIn(ws);
        if (File.Exists(indexPath))
        {
            try
            {
                var downloaded = AssetIndex.Load(indexPath).NowDownloaded(install);
                if (downloaded.Count > 0)
                    Console.WriteLine($"WARNING   : {downloaded.Count} bundle(s) were downloaded since the asset index was made (DLC?); run 'tyrant assets index' to include them.");
            }
            catch (TyrantException)
            {
                // an unreadable index is reported by the commands that use it
            }
        }
        var outputs = ws.Outputs();
        if (outputs.Count == 0) Console.WriteLine("Outputs   : (none yet)");
        foreach (var (name, stamp) in outputs.OrderBy(kv => kv.Key))
            Console.WriteLine($"  {name,-8} {stamp.CreatedUtc:yyyy-MM-dd HH:mm} UTC{(stale.Contains(name) ? "  (stale)" : "")}");
        return ExitCodes.Ok;
    }
}
