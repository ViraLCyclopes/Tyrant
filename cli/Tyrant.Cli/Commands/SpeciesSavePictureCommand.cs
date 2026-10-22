using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

/// <summary>
/// A game species' own Nursery or Paleopedia picture as a PNG (the species page's Save template picture...): the game's
/// picture, to design a custom species' picture over. It is never written into a mod.
/// </summary>
public sealed class SpeciesSavePictureCommand : Command<SpeciesSavePictureCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<SPECIES>")]
        [Description("The game species id, e.g. \"Allosaurus Anax\" (a custom species' template; needs the data dump and the asset index).")]
        public string Species { get; set; } = "";

        [CommandOption("--nursery")]
        [Description("Save its Nursery picture (512x256, transparent background).")]
        public bool Nursery { get; set; }

        [CommandOption("--paleopedia")]
        [Description("Save its Paleopedia picture (a full 16:9 scene, 2048x1152).")]
        public bool Paleopedia { get; set; }

        [CommandOption("--out <FILE>")]
        [Description("The PNG to write (outside your mods: it is the game's picture, for reference only).")]
        public string Out { get; set; } = "";

        public override Spectre.Console.ValidationResult Validate() =>
            Nursery == Paleopedia ? Spectre.Console.ValidationResult.Error("Use --nursery or --paleopedia (one of them).")
            : Out.Length == 0 ? Spectre.Console.ValidationResult.Error("Name the PNG to write with --out <FILE>.")
            : base.Validate();
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        Tyrant.Core.Data.DataStore.Open(ws); // a missing dump is reported before the index is read
        var index = CliServices.LoadIndex(ws, install);
        var kind = settings.Nursery ? TemplatePictureKind.Nursery : TemplatePictureKind.Paleopedia;
        var saved = TemplatePictures.Save(ws, install, CliServices.AssetReader, settings.Species, kind, settings.Out, index);
        Console.WriteLine($"Saved {saved.Species}'s {TemplatePictures.Label(kind)} ({saved.Texture}, {saved.Width}x{saved.Height}) to {saved.Path}");
        Console.WriteLine("It is the game's picture, for designing yours over: don't share it or put it in a mod.");
        return ExitCodes.Ok;
    }
}
