using System.ComponentModel;
using Tyrant.Core.Blender;
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

        [CommandOption("--format <FORMAT>")]
        [Description("glb (default), fbx or both: the pack keeps its .glb files and FBX (converted by Blender) is added.")]
        public string Format { get; set; } = "glb";

        [CommandOption("--animations <WHICH>")]
        [Description("none (default) or all: the species' animations too (in the pack's format), in the pack's animations folder.")]
        public string Animations { get; set; } = "none";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var format = ModelFormats.Parse(settings.Format);
        var converter = format == ModelFormat.Glb ? null : new BlenderService(CliServices.Blender).Converter(ws); // before anything is written
        var index = CliServices.LoadIndex(ws, install);
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(index), settings.Name);

        var result = new SpeciesPackExporter().Export(install, ws, index, species, new ConsoleProgress(), CancellationToken.None);
        var notes = result.Notes.ToList();
        if (converter is not null)
            notes.AddRange(ModelFormats.Apply(converter, [.. result.Models.Where(m => m.Success).Select(m => m.OutputPath)], format, keepGlb: true).Notes);

        if (string.Equals(settings.Animations, "all", StringComparison.OrdinalIgnoreCase))
        {
            var dumped = ModCli.RequireSpecies(ws);
            var id = Tyrant.Core.Rigging.RigInfoService.SpeciesIdOf(species.Key, dumped, SpeciesCatalog.FromIndex(index));
            var ids = Tyrant.Core.Animation.AnimationService.List(ws, install, index, dumped, CliServices.AssetReader, id).Select(a => a.Id).ToList();
            var animations = Tyrant.Core.Animation.AnimationExporter.Run(install, ws, index, dumped, CliServices.AssetReader, id, ids,
                Path.Combine(result.Directory, "animations"), false, format, converter, new ConsoleProgress(), CancellationToken.None);
            notes.AddRange(animations.Notes);
            Console.WriteLine($"  animations: {animations.Files.Count} file(s)");
        }
        else if (!string.Equals(settings.Animations, "none", StringComparison.OrdinalIgnoreCase))
            throw new Tyrant.Core.Errors.TyrantException(Tyrant.Core.Errors.TyrantErrorCode.ModInvalid, $"--animations takes none or all (got '{settings.Animations}').");

        var modelFailures = result.Models.Where(m => !m.Success).ToList();
        var textureFailures = result.Textures.Where(t => !t.Success).ToList();
        Console.WriteLine($"{species.DisplayName} -> {result.Directory}");
        Console.WriteLine($"  models:   {result.Models.Count - modelFailures.Count} exported, {modelFailures.Count} failed");
        Console.WriteLine($"  textures: {result.Textures.Count - textureFailures.Count} exported, {textureFailures.Count} failed");
        Console.WriteLine($"  targets:  {result.TargetsPath}");
        foreach (var m in modelFailures) Console.WriteLine($"  FAIL  model {m.Name}: {m.Error}");
        foreach (var t in textureFailures.Take(10)) Console.WriteLine($"  FAIL  texture {t.Asset.Ref}: {t.Error}");
        foreach (var note in notes) Console.WriteLine($"  NOTE  {note}");
        return modelFailures.Count == 0 && textureFailures.Count == 0 ? ExitCodes.Ok : ExitCodes.Partial;
    }
}
