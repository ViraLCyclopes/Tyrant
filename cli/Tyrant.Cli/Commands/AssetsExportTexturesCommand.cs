using System.ComponentModel;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class AssetsExportTexturesCommand : Command<AssetsExportTexturesCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--filter <TEXT>")]
        [Description("Only textures whose name, path, GUID or bundle contains this text.")]
        public string? Filter { get; set; }

        [CommandOption("--limit <N>")]
        [Description("Export at most this many textures.")]
        public int? Limit { get; set; }

        public override ValidationResult Validate() => Limit < 1 ? ValidationResult.Error("--limit must be at least 1.") : base.Validate();
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var textures = CliServices.LoadIndex(ws, install).Query("Texture2D", settings.Filter).ToList();
        if (settings.Limit is { } limit) textures = textures.Take(Math.Max(limit, 0)).ToList();
        if (textures.Count == 0)
            throw new TyrantException(TyrantErrorCode.AssetNotFound,
                settings.Filter is null ? "The asset index has no textures." : $"No textures match '{settings.Filter}'.");

        var results = new TextureExporter().ExportMany(install, ws, textures, new ConsoleProgress(), CancellationToken.None);
        var failed = results.Where(r => !r.Success).ToList();
        Console.WriteLine($"Exported {results.Count - failed.Count} textures to {Path.Combine(ws.AssetsDir, "textures")}; {failed.Count} failed.");
        foreach (var f in failed.Take(10)) Console.WriteLine($"  FAIL  {f.Asset.Ref}: {f.Error}");
        if (failed.Count > 10) Console.WriteLine($"  ... and {failed.Count - 10} more");
        return failed.Count == 0 ? ExitCodes.Ok : ExitCodes.Partial;
    }
}
