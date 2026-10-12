using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Textures;

namespace Tyrant.Cli.Commands;

/// <summary>tyrant tpack extract: the Mods tab's Extract textures… — any textures.tpack to PNGs; no workspace needed.</summary>
public sealed class TpackExtractCommand : Command<TpackExtractCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("A textures.tpack (from a mod folder or a mod zip).")]
        public string File { get; set; } = "";

        [CommandOption("--out <FOLDER>")]
        [Description("Where to write the PNGs (at their paths in the mod, e.g. skins\\blue\\male_D.png).")]
        public string Out { get; set; } = "";

        public override Spectre.Console.ValidationResult Validate() =>
            string.IsNullOrWhiteSpace(Out) ? Spectre.Console.ValidationResult.Error("Give the folder to write the PNGs to with --out <FOLDER>.") : base.Validate();
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var outDir = Path.GetFullPath(settings.Out);
        var result = TexturePackExtractor.ExtractTo(Path.GetFullPath(settings.File), outDir);
        Console.WriteLine($"Extracted {result.Written.Count} textures into {outDir}.");
        foreach (var skipped in result.Skipped) Console.WriteLine($"  SKIPPED {skipped}");
        return ExitCodes.Ok;
    }
}
