using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Spectre.Console.Cli;
using Tyrant.Core.Animation;
using Tyrant.Core.Errors;
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

/// <summary>A species' animations as files: FBX (default) or glb, one per animation or all in one, with the model.</summary>
public sealed class SpeciesExportAnimationsCommand : Command<SpeciesExportAnimationsCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Species id or name, or a unique part of it (see 'tyrant species list').")]
        public string Name { get; set; } = "";

        [CommandArgument(1, "[ID]")]
        [Description("Animation ids (see 'tyrant species animations --json'), e.g. Carch|LocWalk.")]
        public string[] Ids { get; set; } = [];

        [CommandOption("--all")]
        [Description("Every animation of the species.")]
        public bool All { get; set; }

        [CommandOption("--format <FORMAT>")]
        [Description("fbx (default; made by your Blender), glb, or both.")]
        public string Format { get; set; } = "fbx";

        [CommandOption("--single-file")]
        [Description("All the animations in one file (one take each) instead of one file per animation.")]
        public bool SingleFile { get; set; }

        [CommandOption("--out <DIR>")]
        [Description("Where to write them (default: the workspace's assets/animations/<species>).")]
        public string? Out { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.Ids.Length == 0 && !settings.All)
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                "Name the animations to export, or use --all (app: Species tab > Animations...; list them with 'tyrant species animations <species>').");
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var format = Tyrant.Core.Blender.ModelFormats.Parse(settings.Format);
        var converter = format == Tyrant.Core.Blender.ModelFormat.Glb ? null : new Tyrant.Core.Blender.BlenderService(CliServices.Blender).Converter(ws);
        var index = CliServices.LoadIndex(ws, install);
        var species = ModCli.RequireSpecies(ws);
        var id = RigInfoService.SpeciesIdOf(settings.Name, species, SpeciesCatalog.FromIndex(index));
        var ids = settings.All ? AnimationService.List(ws, install, index, species, CliServices.AssetReader, id).Select(a => a.Id).ToList() : settings.Ids.ToList();
        if (settings.All && ids.Count > AnimationExporter.ManyAnimations)
            Console.WriteLine($"Exporting all {ids.Count} animations of {id}: this takes a while.");
        var outDir = Path.GetFullPath(settings.Out ?? Path.Combine(ws.AssetsDir, "animations", Tyrant.Core.Assets.TextureExporter.Sanitize(id)));
        var result = AnimationExporter.Run(install, ws, index, species, CliServices.AssetReader, id, ids, outDir, settings.SingleFile, format, converter,
            new ConsoleProgress(), CancellationToken.None);
        Console.WriteLine($"{id}: {result.Files.Count} file(s) in {outDir}");
        foreach (var file in result.Files) Console.WriteLine($"  {Path.GetFileName(file)}");
        foreach (var note in result.Notes) Console.WriteLine($"  NOTE  {note}");
        return result.Files.Count > 0 ? ExitCodes.Ok : ExitCodes.Error;
    }
}
