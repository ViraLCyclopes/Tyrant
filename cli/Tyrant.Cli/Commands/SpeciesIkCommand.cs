using System.ComponentModel;
using System.Text.Json;
using Spectre.Console.Cli;
using Tyrant.Core.Blender;
using Tyrant.Core.Species;

namespace Tyrant.Cli.Commands;

/// <summary>A species' IK chains in the game (FABRIK), as Open in Blender builds them into IK controls.</summary>
public sealed class SpeciesIkCommand : Command<SpeciesIkCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Species name, key, or a unique part of it (see 'tyrant species list').")]
        public string Name { get; set; } = "";

        [CommandOption("--json")]
        [Description("Print the chains as JSON.")]
        public bool Json { get; set; }
    }

    /// <summary>One chain as a line: plain ASCII, since the CLI writes in the console's own code page.</summary>
    public static string Describe(BlenderIkChain chain)
    {
        var pole = BlenderIkReader.PoleFrom(chain) is { } from ? $", pole from {from}" : "";
        return $"  {chain.Name,-14} {string.Join(" -> ", chain.Joints.Select(j => j.Name))}{pole}";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var index = CliServices.LoadIndex(ws, install);
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(index), settings.Name);
        var chains = BlenderIkReader.From(CliServices.AssetReader.ReadPrefabModel(install, species.Prefab))?.Chains ?? [];
        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(chains, BlenderCli.Json));
            return ExitCodes.Ok;
        }
        if (chains.Count == 0)
        {
            Console.WriteLine($"{species.DisplayName} has no IK chains in the game.");
            return ExitCodes.Ok;
        }
        foreach (var chain in chains) Console.WriteLine(Describe(chain));
        Console.WriteLine($"{chains.Count} chain(s). Open in Blender builds them as IK controls (app: Open in Blender > Options > IK controls; " +
                          "'tyrant blender open' builds them unless --no-ik). Blender's IK is close to the game's, not identical.");
        return ExitCodes.Ok;
    }
}
