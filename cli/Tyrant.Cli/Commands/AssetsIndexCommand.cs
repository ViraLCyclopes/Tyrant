using Tyrant.Core.Assets;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class AssetsIndexCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var index = new AssetIndexer().BuildAndSave(install, ws, new ConsoleProgress(), CancellationToken.None);

        var bundles = index.Assets.Select(a => a.Bundle).Distinct().Count();
        Console.WriteLine($"Indexed {index.Assets.Count} assets in {bundles} bundles -> {AssetIndex.PathIn(ws)}");
        if (index.MissingBundles.Count > 0)
        {
            Console.WriteLine($"  {index.MissingBundles.Count} bundle(s) listed in the game's catalog are not on disk. Usually that is DLC that is not "
                              + "downloaded (enable it in Steam > Properties > DLC, then re-run this); otherwise verify the game files in Steam.");
            foreach (var bundle in index.MissingBundles.Take(5)) Console.WriteLine($"    {bundle}");
            if (index.MissingBundles.Count > 5) Console.WriteLine($"    ... and {index.MissingBundles.Count - 5} more");
        }
        foreach (var warning in index.Warnings)
            Console.WriteLine($"  WARNING  {warning}");
        foreach (var failure in index.Failures.Take(10))
            Console.WriteLine($"  FAIL  {failure.Bundle}: {failure.Error}");
        if (index.Failures.Count > 10)
            Console.WriteLine($"  ... and {index.Failures.Count - 10} more unreadable bundles");
        return index.Failures.Count == 0 && index.Warnings.Count == 0 ? ExitCodes.Ok : ExitCodes.Partial;
    }
}
