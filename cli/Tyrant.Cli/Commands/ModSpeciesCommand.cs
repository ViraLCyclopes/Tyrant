using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Errors;
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
        [CommandOption("--starting-skin <SKIN>")]
        [Description("The template skin the species starts with, by name or number (default: the first).")]
        public string? StartingSkin { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var species = SpeciesSkinsReader.Load(ws);
        var template = species.FirstOrDefault(s => string.Equals(s.SpeciesId, settings.Template, StringComparison.OrdinalIgnoreCase));
        var entry = mod.AddSpecies(species, settings.SpeciesId, settings.Template,
            settings.Name, settings.Genus, settings.Epithet, settings.Cost, templateStats: SpeciesDatabase.Template(ws, template?.SpeciesId ?? settings.Template),
            startingSkin: settings.StartingSkin is null ? 0 : StartingSkinOption.Resolve(species, settings.Template, settings.StartingSkin));
        Console.WriteLine($"Added {entry.Name}: {entry.Key(mod.Id)} (template {entry.Base}).");
        Console.WriteLine("Experimental species support: test spawning and save/reload in a separate park before using it in your main park.");
        return ExitCodes.Ok;
    }
}

internal static class StartingSkinOption
{
    /// <summary>A template skin by number or name (case-insensitive), as --starting-skin takes it.</summary>
    public static int Resolve(IReadOnlyList<SpeciesSkins> vanilla, string template, string skin)
    {
        var row = vanilla.FirstOrDefault(s => string.Equals(s.SpeciesId, template, StringComparison.OrdinalIgnoreCase))
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{template}' is not in the game data dump.");
        if (int.TryParse(skin, out var number)) return number; // AddSpecies / SetSpeciesStartingSkin report a missing number
        return row.Skins.FirstOrDefault(s => string.Equals(s.Name, skin, StringComparison.OrdinalIgnoreCase))?.Index
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"{row.SpeciesId} has no skin '{skin}'. Its skins: {string.Join(", ", row.Skins.Select(s => $"{s.Index} {s.Name}"))}.");
    }
}

public sealed class ModSetSpeciesSkinCommand : Command<ModSetSpeciesSkinCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        public string SpeciesId { get; set; } = "";
        [CommandOption("--starting-skin <SKIN>")]
        [Description("The template skin to start with, by name or number.")]
        public string? StartingSkin { get; set; }
        [CommandOption("--name <NAME>")]
        [Description("The starting skin's name in game (\"\" = the template skin's name).")]
        public string? Name { get; set; }
        [CommandOption("--swatch <PNG>")]
        [Description("A PNG for the starting skin's swatch in the skin picker.")]
        public string? Swatch { get; set; }
        [CommandOption("--no-swatch")]
        [Description("Go back to the template skin's swatch.")]
        public bool NoSwatch { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var entry = mod.Manifest.Species.FirstOrDefault(s => s.Id == settings.SpeciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"No species '{settings.SpeciesId}' in mod '{mod.Id}'.");
        if (settings.Swatch is not null && settings.NoSwatch) throw new TyrantException(TyrantErrorCode.ModInvalid, "Use --swatch or --no-swatch, not both.");
        if (settings.StartingSkin is not null || settings.Name is not null)
        {
            var vanilla = SpeciesSkinsReader.Load(ws);
            var skin = settings.StartingSkin is null ? entry.StartingSkin : StartingSkinOption.Resolve(vanilla, entry.Base, settings.StartingSkin);
            mod.SetSpeciesStartingSkin(vanilla, entry.Id, skin, settings.Name ?? entry.StartingSkinName);
        }
        if (settings.Swatch is not null) mod.SetSpeciesSwatch(entry.Id, settings.Swatch);
        if (settings.NoSwatch) mod.SetSpeciesSwatch(entry.Id, null);
        entry = mod.Manifest.Species.First(s => s.Id == settings.SpeciesId);
        Console.WriteLine($"{entry.Key(mod.Id)}: starting skin {entry.StartingSkin}{(entry.StartingSkinName is null ? "" : $" \"{entry.StartingSkinName}\"")}{(entry.Swatch is null ? "" : $", swatch {entry.Swatch}")}.");
        return ExitCodes.Ok;
    }
}

public sealed class ModSpeciesPortraitCommand : Command<ModSpeciesPortraitCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        public string SpeciesId { get; set; } = "";

        [CommandOption("--slot <SLOT>")]
        [Description("adultMale, adultMaleAlbino, adultMaleMelanistic, adultMaleLeucistic, the same for adultFemale, infant, infantAlbino, infantMelanistic, infantLeucistic.")]
        public string Slot { get; set; } = "";

        [CommandOption("--png <FILE>")]
        [Description("The portrait PNG (the game's are 128x64).")]
        public string? Png { get; set; }

        [CommandOption("--clear")]
        [Description("Show the base skin's portrait again.")]
        public bool Clear { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.Clear == (settings.Png is not null))
        {
            Console.Error.WriteLine("Give --png or --clear (not both).");
            return ExitCodes.Error;
        }
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var portraits = ModProject.Open(ws, settings.Id).SetSpeciesPortraits(settings.SpeciesId,
            new Dictionary<string, PortraitSource?> { [settings.Slot] = settings.Clear ? null : new PortraitSource(Png: settings.Png) });
        Console.WriteLine(portraits.TryGetValue(settings.Slot, out var file) ? $"  {settings.Slot} <- {file}" : $"'{settings.SpeciesId}' {settings.Slot} uses the game's portrait.");
        return ExitCodes.Ok;
    }
}
