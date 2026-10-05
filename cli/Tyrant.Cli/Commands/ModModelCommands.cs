using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

// Model replacements: the same operations as the app's Replace model in a mod and the mod editor's model page.

public sealed class ModReplaceModelCommand : Command<ModReplaceModelCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<TARGET>")]
        [Description("The species id, e.g. Carcharodontosaurus (with --skin, the skin's species is used).")]
        public string Target { get; set; } = "";

        [CommandArgument(2, "<FILE>")]
        [Description("Your model: a .glb from Blender, or an .fbx (converted by Blender).")]
        public string File { get; set; } = "";

        [CommandOption("--skin <SKIN>")]
        [Description("Give this skin its own model instead of replacing the species' model.")]
        public string? Skin { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var report = mod.ReplaceModel(install, CliServices.LoadIndex(ws, install), ModCli.RequireSpecies(ws), CliServices.AssetReader,
            Path.GetFullPath(settings.File), settings.Skin is null ? settings.Target : null, null, settings.Skin,
            () => new Tyrant.Core.Blender.BlenderService(CliServices.Blender).Converter(ws));
        Console.WriteLine($"Added the model to '{settings.Id}': {string.Join(", ", report.Lods.Select((l, i) => $"LOD {i} {l.Vertices:N0} vertices"))}.");
        foreach (var warning in report.Warnings) Console.WriteLine($"  WARN   {warning}");
        return ExitCodes.Ok;
    }
}

public sealed class ModRemoveModelCommand : Command<ModRemoveModelCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<TARGET>")]
        [Description("The species id whose model the mod replaces.")]
        public string Target { get; set; } = "";

        [CommandOption("--skin <SKIN>")]
        [Description("Make this skin use the species model again.")]
        public string? Skin { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        ModProject.Open(ws, settings.Id).RemoveModel(settings.Target, settings.Skin);
        Console.WriteLine(settings.Skin is null ? $"'{settings.Id}' no longer replaces {settings.Target}'s model." : $"'{settings.Skin}' uses the species model again.");
        return ExitCodes.Ok;
    }
}

public sealed class ModRebuildModelsCommand : Command<ModSettings>
{
    public override int Execute(CommandContext context, ModSettings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var index = CliServices.LoadIndex(ws, install);
        var species = ModCli.RequireSpecies(ws);
        var failed = false;
        foreach (var (speciesId, file) in mod.ModelEntries())
        {
            var report = ModelBuilder.Build(mod.Dir, file, CliServices.AssetReader.ReadPrefabModel(install, ModProject.ResolveModelTarget(index, species, speciesId, null).Prefab), null, mod.RigOfFile(file));
            failed |= report.Errors.Count > 0;
            Console.WriteLine($"  {speciesId}: {(report.Errors.Count == 0 ? $"{report.Lods.Count} LOD(s)" : string.Join(" ", report.Errors))}");
        }
        return failed ? ExitCodes.Error : ExitCodes.Ok;
    }
}
