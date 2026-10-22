using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Mods;
using Tyrant.Core.Rigging;

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

        [CommandOption("--keep-rig")]
        [Description("Keep the destination's rig edit whatever the model's bones say (as before).")]
        public bool KeepRig { get; set; }

        [CommandOption("--use-file-rig")]
        [Description("Use the model's skeleton even when it is the game's (removes the rig edit).")]
        public bool UseFileRig { get; set; }

        [CommandOption("--dry-run")]
        [Description("Print the rig edit the model brings and save nothing.")]
        public bool DryRun { get; set; }

        public override ValidationResult Validate() =>
            KeepRig && UseFileRig ? ValidationResult.Error("--keep-rig and --use-file-rig cannot be used together: choose one.") : base.Validate();
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var (index, species) = (CliServices.LoadIndex(ws, install), ModCli.RequireSpeciesFor(ws, install, mod));
        var (file, target) = (Path.GetFullPath(settings.File), settings.Skin is null ? settings.Target : null);
        Func<Tyrant.Core.Blender.IModelConverter> converter = () => new Tyrant.Core.Blender.BlenderService(CliServices.Blender).Converter(ws);
        var choice = settings.KeepRig ? ModelRigChoice.KeepCurrent : settings.UseFileRig ? ModelRigChoice.UseFile : ModelRigChoice.Auto;
        if (choice == ModelRigChoice.KeepCurrent)
        {
            if (settings.DryRun) Console.WriteLine("Rig edit: the current one is kept (--keep-rig)");
        }
        else
        {
            var preview = mod.PreviewModelRig(ws, install, index, species, CliServices.AssetReader, file, target, null, settings.Skin, converter);
            PrintRig(preview, file, choice);
            foreach (var w in preview.Warnings) Console.WriteLine($"  WARN   {w}");
            foreach (var e in preview.Errors) Console.WriteLine($"  ERROR  {e}");
            if (preview.Errors.Count > 0 && choice == ModelRigChoice.Auto)
            {
                Console.WriteLine("Nothing was saved: fix the rig edit (or use --keep-rig to leave the destination's as it is).");
                return ExitCodes.Error;
            }
        }
        if (settings.DryRun) return ExitCodes.Ok;
        var report = mod.ReplaceModel(install, index, species, CliServices.AssetReader, file, target, null, settings.Skin, converter,
            rigChoice: choice, ws: ws);
        mod.RefreshHeadRigs(ws, install, index, species, CliServices.AssetReader);
        Console.WriteLine($"Added the model to '{settings.Id}': {string.Join(", ", report.Lods.Select((l, i) => $"LOD {i} {l.Vertices:N0} vertices"))}.");
        foreach (var warning in report.Warnings) Console.WriteLine($"  WARN   {warning}");
        return ExitCodes.Ok;
    }

    /// <summary>Where the rig edit comes from, its bones, and (when the model has the game's skeleton) that the current one is kept.</summary>
    private static void PrintRig(ModelRigPreview preview, string file, ModelRigChoice choice)
    {
        var reading = preview.Reading;
        var count = $"{reading.Rig.Count} bone{(reading.Rig.Count == 1 ? "" : "s")}";
        Console.WriteLine("Rig edit: " + (reading.Source switch
        {
            RigSource.Saved => "same as the current one",
            RigSource.GameSkeleton => "the model has the game's skeleton",
            _ when !preview.Changes && preview.Current is not null => "same as the current one",
            RigSource.RigFile => $"from {Path.GetFileName(ModelRigReader.RigFilePathFor(file))} ({count})",
            _ => $"from the model's bones ({count})",
        }));
        foreach (var (bone, offset) in reading.Rig.OrderBy(p => p.Key, StringComparer.Ordinal)) Console.WriteLine("  " + RigText.Describe(bone, offset));
        if (choice == ModelRigChoice.Auto && preview.Question == ModelRigQuestion.KeepOrRemove)
            Console.WriteLine("Kept the current rig edit (the model has the game's skeleton); --use-file-rig removes it.");
        else if (choice == ModelRigChoice.Auto && preview.Question == ModelRigQuestion.UseOrKeep)
            Console.WriteLine("Replaced the current rig edit with the model's; --keep-rig keeps the current one.");
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
        var species = ModCli.RequireSpeciesFor(ws, install, mod);
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
