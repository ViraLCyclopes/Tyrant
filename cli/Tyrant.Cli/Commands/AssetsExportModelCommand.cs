using System.ComponentModel;
using Tyrant.Core.Blender;
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

        [CommandOption("--format <FORMAT>")]
        [Description("glb (default), fbx or both; FBX is converted by Blender.")]
        public string Format { get; set; } = "glb";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var format = ModelFormats.Parse(settings.Format);
        var converter = format == ModelFormat.Glb ? null : new BlenderService(CliServices.Blender).Converter(ws); // before anything is written
        var index = CliServices.LoadIndex(ws, install);
        var prefab = index.Resolve(settings.Key, settings.Type);
        var notes = new List<string>();
        var results = new ModelExporter().Export(install, ws, prefab, index, notes);
        var files = results.Where(r => r.Success).Select(r => r.OutputPath).ToList();
        if (converter is not null)
        {
            var (converted, convertNotes) = ModelFormats.Apply(converter, files, format);
            files = [.. converted];
            notes.AddRange(convertNotes);
        }
        foreach (var r in results.Where(r => !r.Success)) Console.WriteLine($"  FAIL  {r.Name}: {r.Error}");
        foreach (var file in files) Console.WriteLine($"  ok    {file}");
        if (results.Count == 0) Console.WriteLine("  (the prefab has no meshes)");
        foreach (var note in notes) Console.WriteLine($"  NOTE  {note}");
        return results.All(r => r.Success) ? ExitCodes.Ok : ExitCodes.Partial;
    }
}
