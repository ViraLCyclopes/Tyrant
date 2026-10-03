using System.ComponentModel;
using Tyrant.Core.Models;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class AssetsExportModelCommand : Command<AssetsExportModelCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<KEY>")]
        [Description("Prefab ref (bundle#pathId), catalog GUID, or container path.")]
        public string Key { get; set; } = "";

        [CommandOption("--type <TYPE>")]
        [Description("Unity type of the object to export (default GameObject).")]
        public string Type { get; set; } = "GameObject";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var prefab = CliServices.LoadIndex(ws, install).Resolve(settings.Key, settings.Type);
        var results = new ModelExporter().Export(install, ws, prefab);
        foreach (var r in results)
            Console.WriteLine(r.Success ? $"  ok    {r.Name} -> {r.OutputPath}" : $"  FAIL  {r.Name}: {r.Error}");
        if (results.Count == 0) Console.WriteLine("  (the prefab has no meshes)");
        return results.All(r => r.Success) ? ExitCodes.Ok : ExitCodes.Partial;
    }
}
