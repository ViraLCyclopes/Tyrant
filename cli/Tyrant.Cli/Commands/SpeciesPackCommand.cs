using System.ComponentModel;
using Tyrant.Core.Species;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class SpeciesPackCommand : Command<SpeciesPackCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Species name, key, or a unique part of it (see 'tyrant species list').")]
        public string Name { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var index = CliServices.LoadIndex(ws, install);
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(index), settings.Name);

        var result = new SpeciesPackExporter().Export(install, ws, index, species, new ConsoleProgress(), CancellationToken.None);

        var modelFailures = result.Models.Where(m => !m.Success).ToList();
        var textureFailures = result.Textures.Where(t => !t.Success).ToList();
        Console.WriteLine($"{species.DisplayName} -> {result.Directory}");
        Console.WriteLine($"  models:   {result.Models.Count - modelFailures.Count} exported, {modelFailures.Count} failed");
        Console.WriteLine($"  textures: {result.Textures.Count - textureFailures.Count} exported, {textureFailures.Count} failed");
        Console.WriteLine($"  targets:  {result.TargetsPath}");
        foreach (var m in modelFailures) Console.WriteLine($"  FAIL  model {m.Name}: {m.Error}");
        foreach (var t in textureFailures.Take(10)) Console.WriteLine($"  FAIL  texture {t.Asset.Ref}: {t.Error}");
        foreach (var note in result.Notes) Console.WriteLine($"  NOTE  {note}");
        return modelFailures.Count == 0 && textureFailures.Count == 0 ? ExitCodes.Ok : ExitCodes.Partial;
    }
}
