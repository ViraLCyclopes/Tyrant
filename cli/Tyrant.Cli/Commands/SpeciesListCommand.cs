using System.ComponentModel;
using Tyrant.Core.Species;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class SpeciesListCommand : Command<SpeciesListCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--vivarium")]
        [Description("Only vivarium animals.")]
        public bool VivariumOnly { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var species = SpeciesCatalog.FromIndex(CliServices.LoadIndex(ws, install))
            .Where(s => !settings.VivariumOnly || s.Vivarium)
            .ToList();
        if (species.Count == 0)
        {
            Console.WriteLine("No species found in the asset index.");
            return ExitCodes.Ok;
        }
        foreach (var s in species)
            Console.WriteLine($"{s.Key,-34} {s.DisplayName,-34} {(s.Vivarium ? "vivarium" : "park")}");
        Console.WriteLine($"{species.Count} species.");
        return ExitCodes.Ok;
    }
}
