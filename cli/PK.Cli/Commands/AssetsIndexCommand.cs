using PK.Core.Assets;
using Spectre.Console.Cli;

namespace PK.Cli.Commands;

public sealed class AssetsIndexCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var index = new AssetIndexer().BuildAndSave(install, ws, new ConsoleProgress(), CancellationToken.None);

        var bundles = index.Assets.Select(a => a.Bundle).Distinct().Count();
        Console.WriteLine($"Indexed {index.Assets.Count} assets in {bundles} bundles -> {AssetIndex.PathIn(ws)}");
        if (index.MissingBundles.Count > 0)
            Console.WriteLine($"  {index.MissingBundles.Count} bundles listed in the catalog are not downloaded "
                              + "(DLC: if you own it, enable it in Steam > Properties > DLC, then re-run this).");
        foreach (var failure in index.Failures.Take(10))
            Console.WriteLine($"  FAIL  {failure.Bundle}: {failure.Error}");
        if (index.Failures.Count > 10)
            Console.WriteLine($"  ... and {index.Failures.Count - 10} more unreadable bundles");
        return index.Failures.Count == 0 ? ExitCodes.Ok : ExitCodes.Partial;
    }
}
