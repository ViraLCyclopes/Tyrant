using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

public sealed class ModRemoveSpeciesCommand : Command<ModRemoveSpeciesCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        [Description("The species' id in the mod (see 'tyrant mod show').")]
        public string SpeciesId { get; set; } = "";

        [CommandOption("--keep-files")]
        [Description("Leave its PNGs, models, sounds, language files, icon and Blender projects in place (default: they go to the Recycle Bin).")]
        public bool KeepFiles { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var plan = mod.SpeciesRemovalOf(ws, settings.SpeciesId);
        var deleted = mod.RemoveSpecies(settings.SpeciesId, deleteFiles: !settings.KeepFiles);
        Console.WriteLine($"Removed species {plan.Key} ({plan.Name}) and {plan.Skins.Count} skin(s).{(plan.Models + plan.Sounds > 0 ? $" Also {plan.Models} model(s) and {plan.Sounds} sound(s)." : "")}");
        Console.WriteLine("Saved parks with this species can no longer load its animals.");
        foreach (var user in plan.UsedBy)
            Console.WriteLine($"  {user.ModId} still has {user.Skins} skin(s), {user.Models} model(s), {user.Sounds} sound(s) for it; the game skips them.");
        foreach (var file in deleted.Recycled) Console.WriteLine($"  to the Recycle Bin  {file}");
        foreach (var file in deleted.Kept) Console.WriteLine($"  stayed  {file}");
        return ExitCodes.Ok;
    }
}
