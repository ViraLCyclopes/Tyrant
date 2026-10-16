using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

/// <summary>The starting skin options shared by add-species and add-starting-skin.</summary>
public interface IStartingSkinOptions
{
    string? StartFrom { get; }
    string? StartPng { get; }
    string? StartPngFemale { get; }
    string? StartName { get; }
}

internal static class StartingSkinOptions
{
    /// <summary>--start-png (+ --start-png-female), else a copy of --start-from SPECIES[/SKIN] (default: the template and its skin).</summary>
    public static StartingSkinSource Source(IReadOnlyList<SpeciesSkins> vanilla, IStartingSkinOptions o, string template, int templateSkin)
    {
        if (o.StartPng is not null && o.StartFrom is not null)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Copy a skin or use your own PNGs, not both.");
        if (o.StartPng is null && o.StartPngFemale is not null)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "--start-png-female needs --start-png too.");
        if (o.StartPng is not null) return new OwnPngs(o.StartPng, o.StartPngFemale);
        if (o.StartFrom is null) return new CopySkin(template, templateSkin);
        var slash = o.StartFrom.IndexOf('/');
        var species = slash < 0 ? o.StartFrom : o.StartFrom[..slash];
        var skin = slash < 0 ? 0 : StartingSkinOption.Resolve(vanilla, species, o.StartFrom[(slash + 1)..]);
        var row = vanilla.FirstOrDefault(s => string.Equals(s.SpeciesId, species, StringComparison.OrdinalIgnoreCase));
        return new CopySkin(row?.SpeciesId ?? species, skin);
    }
}

public sealed class ModAddStartingSkinCommand : Command<ModAddStartingSkinCommand.Settings>
{
    public sealed class Settings : ModSettings, IStartingSkinOptions
    {
        [CommandArgument(1, "<SPECIES-ID>")]
        [Description("The species' id in the mod (see 'tyrant mod show').")]
        public string SpeciesId { get; set; } = "";
        [CommandOption("--start-from <SPECIES>")]
        [Description("Copy this base-game species' skin, as SPECIES or SPECIES/SKIN (number or name); default: the template's starting skin.")]
        public string? StartFrom { get; set; }
        [CommandOption("--start-png <PNG>")]
        [Description("Your own diffuse PNG instead (the other maps come from the template skin).")]
        public string? StartPng { get; set; }
        [CommandOption("--start-png-female <PNG>")]
        [Description("The female's own diffuse PNG (default: the --start-png one).")]
        public string? StartPngFemale { get; set; }
        [CommandOption("--start-name <NAME>")]
        [Description("The starting skin's name in game (default: the copied skin's name, or the species name).")]
        public string? StartName { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var entry = mod.Manifest.Species.FirstOrDefault(s => s.Id == settings.SpeciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"No species '{settings.SpeciesId}' in mod '{mod.Id}'.");
        var vanilla = SpeciesSkinsReader.Load(ws);
        var skin = mod.AddStartingSkin(install, CliServices.LoadIndex(ws, install), CliServices.AssetReader, vanilla, entry.Id,
            StartingSkinOptions.Source(vanilla, settings, entry.Base, entry.StartingSkin), settings.StartName);
        Console.WriteLine($"Starting skin: {skin.Id} ({skin.Name}).");
        return ExitCodes.Ok;
    }
}
