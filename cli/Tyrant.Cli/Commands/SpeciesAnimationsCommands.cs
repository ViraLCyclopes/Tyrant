using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Spectre.Console.Cli;
using Tyrant.Core.Animation;
using Tyrant.Core.Rigging;
using Tyrant.Core.Species;

namespace Tyrant.Cli.Commands;

/// <summary>A species' animations in the game (its animation table in the data dump), as the app's Animations list shows them.</summary>
public sealed class SpeciesAnimationsCommand : Command<SpeciesAnimationsCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Species id or name, or a unique part of it (see 'tyrant species list').")]
        public string Name { get; set; } = "";

        [CommandOption("--json")]
        [Description("Print the list as JSON (ids, names, lengths in seconds, frame rates, loops, travels).")]
        public bool Json { get; set; }
    }

    /// <summary>One line per animation (plain ASCII: the CLI writes in the console's own code page).</summary>
    public static IEnumerable<string> Lines(IReadOnlyList<AnimationInfo> animations)
    {
        foreach (var a in animations)
        {
            var flags = string.Join(", ", new[] { a.Loops ? "loops" : null, a.Travels ? "travels" : null }.OfType<string>());
            yield return string.Create(CultureInfo.InvariantCulture, $"  {a.Name,-32} {a.Length,6:0.00} s  {a.FrameRate,3:0} fps  {flags}").TrimEnd();
        }
        yield return $"{animations.Count} animation(s). Open them in Blender (app: Species tab > Animations..., or 'tyrant blender open --animations <id>') " +
                     "or export them ('tyrant species export-animations').";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var index = CliServices.LoadIndex(ws, install);
        var species = ModCli.RequireSpecies(ws);
        var id = RigInfoService.SpeciesIdOf(settings.Name, species, SpeciesCatalog.FromIndex(index));
        var list = AnimationService.List(ws, install, index, species, CliServices.AssetReader, id);
        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(list, BlenderCli.Json));
            return ExitCodes.Ok;
        }
        Console.WriteLine($"{id}:");
        foreach (var line in Lines(list)) Console.WriteLine(line);
        return ExitCodes.Ok;
    }
}
