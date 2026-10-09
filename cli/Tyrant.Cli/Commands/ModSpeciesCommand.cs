using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

public sealed class ModAddSpeciesCommand : Command<ModAddSpeciesCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        public string SpeciesId { get; set; } = "";
        [CommandArgument(2, "<TEMPLATE>")]
        public string Template { get; set; } = "";
        [CommandOption("--name <NAME>")]
        [Description("The new animal's display name.")]
        public string Name { get; set; } = "";
        [CommandOption("--genus <GENUS>")]
        public string Genus { get; set; } = "";
        [CommandOption("--epithet <EPITHET>")]
        public string Epithet { get; set; } = "";
        [CommandOption("--cost <COST>")]
        public int? Cost { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var species = SpeciesSkinsReader.Load(ws);
        var template = species.FirstOrDefault(s => string.Equals(s.SpeciesId, settings.Template, StringComparison.OrdinalIgnoreCase));
        var entry = mod.AddSpecies(species, settings.SpeciesId, settings.Template,
            settings.Name, settings.Genus, settings.Epithet, settings.Cost, templateStats: SpeciesDatabase.Template(ws, template?.SpeciesId ?? settings.Template));
        Console.WriteLine($"Added {entry.Name}: {entry.Key(mod.Id)} (template {entry.Base}).");
        Console.WriteLine("Experimental species support: test spawning and save/reload in a separate park before using it in your main park.");
        return ExitCodes.Ok;
    }
}
