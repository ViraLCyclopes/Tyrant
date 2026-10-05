using System.ComponentModel;
using System.Text.Json;
using Spectre.Console.Cli;
using Tyrant.Core.Mods;
using Tyrant.Core.Rigging;
using Tyrant.Core.Species;

namespace Tyrant.Cli.Commands;

/// <summary>What a species' skeleton does in game, for rig edits: bones its animations move, bones its growth positions or scales.</summary>
public sealed class SpeciesRigInfoCommand : Command<SpeciesRigInfoCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Species id or name, or a unique part of it (see 'tyrant species list').")]
        public string Name { get; set; } = "";

        [CommandOption("--json")]
        [Description("Print the lists as JSON.")]
        public bool Json { get; set; }
    }

    /// <summary>The report's lines (plain ASCII: the CLI writes in the console's own code page).</summary>
    public static IEnumerable<string> Lines(RigInfo info)
    {
        static string List(IReadOnlyList<string> names) => names.Count == 0 ? "(none)" : string.Join(", ", names);
        yield return $"Bones: {info.Bones.Count}";
        yield return $"Moved by the game's animations: {List(info.ClipMoved)}";
        yield return $"Positioned by growth: {List(info.GrowthMoved)}";
        yield return $"Scaled by growth: {List(info.GrowthScaled)}";
        yield return RigLimits.GrowthBonesSupported
            ? "Rig edits on growth bones are re-applied after the game's growth."
            : "Rig edits on growth bones are not supported yet (the game's growth puts them back).";
        foreach (var failure in info.Failures) yield return $"  WARN   {failure}";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var index = CliServices.LoadIndex(ws, install);
        var species = ModCli.RequireSpecies(ws);
        var id = RigInfoService.SpeciesIdOf(settings.Name, species, SpeciesCatalog.FromIndex(index));
        var info = RigInfoService.For(ws, install, index, species, CliServices.AssetReader, id);
        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(info, BlenderCli.Json));
            return ExitCodes.Ok;
        }
        Console.WriteLine($"{id}:");
        foreach (var line in Lines(info)) Console.WriteLine(line);
        return ExitCodes.Ok;
    }
}
