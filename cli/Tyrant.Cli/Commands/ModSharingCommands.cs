using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Cli.Commands;

// Sharing mods: the same as the mod editor's Mod → Export for sharing… and the Mods tab's Add mod from zip….

public sealed class ModExportCommand : Command<ModExportCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandOption("--out <FILE>")]
        [Description("Where to write the zip (default: <workspace>\\exports\\<id>-<version>.zip).")]
        public string? Out { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        ModCli.RebuildStaleModels(ws, install, mod);
        var check = ModCli.Check(ws, install, mod);
        if (!check.Ok)
        {
            foreach (var error in check.Errors) Console.Error.WriteLine($"  ERROR  {error}");
            Console.Error.WriteLine($"'{settings.Id}' has problems, so it was not exported.");
            return ExitCodes.Error;
        }
        var path = ModSharing.Export(mod, Path.GetFullPath(settings.Out ?? Path.Combine(ws.Dir, "exports", $"{mod.Id}-{mod.Manifest.Version}.zip")), FrameworkInfo.Version);
        foreach (var warning in check.Warnings) Console.WriteLine($"  WARN   {warning}");
        Console.WriteLine($"Exported '{mod.Id}' to {path}. Players unzip it into the game folder.");
        return ExitCodes.Ok;
    }
}

public sealed class ModImportCommand : Command<ModImportCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<ZIP>")]
        [Description("A mod zip (from 'tyrant mod export' or a zip of a mod folder).")]
        public string Zip { get; set; } = "";

        [CommandOption("--replace")]
        [Description("Replace a mod with the same id already in the workspace.")]
        public bool Replace { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModSharing.Import(ws, Path.GetFullPath(settings.Zip), settings.Replace);
        Console.WriteLine($"Imported '{mod.Id}' into the workspace (mods\\{mod.Id}). Install it with 'tyrant mod install {mod.Id}'.");
        var check = ModCli.Check(ws, install, mod);
        foreach (var error in check.Errors) Console.WriteLine($"  ERROR  {error}");
        foreach (var warning in check.Warnings) Console.WriteLine($"  WARN   {warning}");
        return ExitCodes.Ok;
    }
}
