using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Mods;
using Tyrant.Core.Rigging;

namespace Tyrant.Cli.Commands;

// Rig edits are made in Blender (Start / Apply rig edit, then Send); here they are shown and cleared, as on the app's model and skin pages.

public sealed class ModRigCommand : Command<ModRigCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES>")]
        [Description("The species id, e.g. Carcharodontosaurus (with --skin, the skin's species is used).")]
        public string Species { get; set; } = "";

        [CommandOption("--skin <SKIN>")]
        [Description("The skin's own rig edit instead of the species'.")]
        public string? Skin { get; set; }

        [CommandOption("--clear")]
        [Description("Remove the rig edit (a model made for it is rebuilt for the game's skeleton).")]
        public bool Clear { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var skin = settings.Skin is null ? null : mod.Skin(settings.Skin);
        var species = skin?.Species ?? settings.Species;
        var what = skin is null ? species : $"{species} (skin {skin.Id})";
        var rig = mod.RigOf(species, skin?.Id);
        if (rig is null || rig.Count == 0)
        {
            Console.WriteLine($"No rig edit on {what} in '{mod.Id}'.");
            return ExitCodes.Ok;
        }
        var hasModel = skin is not null ? skin.Model is not null
            : mod.Manifest.Models.Any(m => string.Equals(m.Target, species, StringComparison.Ordinal) && m.File.Length > 0);
        if (settings.Clear)
        {
            var report = mod.SetRig(install, CliServices.LoadIndex(ws, install), ModCli.RequireSpecies(ws), CliServices.AssetReader, species, skin?.Id, null);
            Console.WriteLine($"Cleared the rig edit of {what}.");
            if (report is not null)
                Console.WriteLine("  WARN   The model was made for the edited skeleton; send it again from Blender (or add it again) so it fits the game's skeleton.");
            return ExitCodes.Ok;
        }
        Console.WriteLine($"Rig edit of {what}:");
        foreach (var (bone, offset) in rig.OrderBy(p => p.Key, StringComparer.Ordinal)) Console.WriteLine("  " + RigText.Describe(bone, offset));
        var where = skin is null ? $"Rig edit of {species}" : $"Skin '{skin.Id}'";
        if (ModCli.RigInfoProvider(ws, install)?.Invoke(species) is { } info)
        {
            var (errors, warnings) = RigRules.Problems(rig, info, hasModel, where);
            foreach (var e in errors) Console.WriteLine($"  ERROR  {e}");
            foreach (var w in warnings) Console.WriteLine($"  WARN   {w}");
        }
        else if (!hasModel) Console.WriteLine($"  WARN   {where}: the rig edit has no model of its own: the game's mesh stretches with the moved bones.");
        return ExitCodes.Ok;
    }
}
