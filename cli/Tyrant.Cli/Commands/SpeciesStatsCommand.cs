using System.ComponentModel;
using System.Globalization;
using Spectre.Console.Cli;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Cli.Commands;

/// <summary>A species' purchase cost and basic stats from the game data dump: what a new species based on it inherits.</summary>
public sealed class SpeciesStatsCommand : Command<SpeciesStatsCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<SPECIES>")]
        [Description("The species id as the game data names it, e.g. Spinosaurus (see 'tyrant species list').")]
        public string Species { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var stats = SpeciesDatabase.TemplateStats(ws, settings.Species)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound,
                $"No species '{settings.Species}' in the game data dump; check the name with 'tyrant species list', or run 'tyrant dump run' first.");
        Console.WriteLine($"{settings.Species}");
        Console.WriteLine($"  Purchase cost: {(stats.Cost is int cost ? cost.ToString(CultureInfo.InvariantCulture) : "(not set)")}");
        foreach (var field in SpeciesStats.Fields)
            if (stats.Stats.TryGetValue(field.Id, out var value))
                Console.WriteLine($"  {field.Label} ({field.Unit}): {value.ToString(CultureInfo.InvariantCulture)}");
        return ExitCodes.Ok;
    }
}
