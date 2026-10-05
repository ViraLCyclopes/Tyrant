using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

/// <summary>A species' skin textures, as the mod editor's Texture replacements → + Add lists them (names to use with 'tyrant mod replace').</summary>
public sealed class SpeciesTexturesCommand : Command<SpeciesTexturesCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<SPECIES>")]
        [Description("The species id, e.g. \"Allosaurus Anax\" (needs the data dump and the asset index).")]
        public string Species { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var textures = SpeciesTextures.For(ModCli.RequireSpecies(ws), CliServices.LoadIndex(ws, install), settings.Species);
        if (textures.Count == 0)
        {
            Console.WriteLine($"No skin textures found for '{settings.Species}' (check the species id with 'tyrant species list').");
            return ExitCodes.Error;
        }
        foreach (var t in textures)
        {
            var shared = t.SharedWith.Count == 0 ? "" : $"  (also used by {string.Join(", ", t.SharedWith.Take(4))}{(t.SharedWith.Count > 4 ? $" and {t.SharedWith.Count - 4} more" : "")})";
            Console.WriteLine($"  {t.Texture,-40} {t.Slot,-14} {string.Join(", ", t.Skins)}{shared}");
        }
        Console.WriteLine($"{textures.Count} texture(s). Replace one with 'tyrant mod replace <mod> <texture> <png>'.");
        return ExitCodes.Ok;
    }
}
