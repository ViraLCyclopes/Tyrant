using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
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

        [CommandOption("--with-pngs")]
        [Description("Also put the original PNGs in the zip (bigger; lets others edit them without quality loss).")]
        public bool WithPngs { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        ModCli.RebuildStaleModels(ws, install, mod, measureHeadRigs: true);
        var check = ModCli.Check(ws, install, mod);
        if (!check.Ok)
        {
            foreach (var error in check.Errors) Console.Error.WriteLine($"  ERROR  {error}");
            Console.Error.WriteLine($"'{settings.Id}' has problems, so it was not exported.");
            return ExitCodes.Error;
        }
        var cooker = ModCli.Cooker(ws, install, mod);
        var path = ModSharing.Export(mod, Path.GetFullPath(settings.Out ?? Path.Combine(ws.Dir, "exports", $"{mod.Id}-{mod.Manifest.Version}.zip")), FrameworkInfo.Version,
            dir => cooker.WritePack(mod.Manifest, mod.Dir, dir, new SyncProgress(p => Console.WriteLine(p.Message))), settings.WithPngs);
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
        var imported = ModSharing.ImportReported(ws, Path.GetFullPath(settings.Zip), settings.Replace);
        Console.WriteLine($"Imported '{imported.Mod.Id}' into the workspace (mods\\{imported.Mod.Id}). Install it with 'tyrant mod install {imported.Mod.Id}'.");
        return Report(ws, install, imported);
    }

    /// <summary>What import and copy-from-game print after the mod is in: extracted textures, missing PNGs, Check.</summary>
    internal static int Report(Tyrant.Core.Workspaces.Workspace ws, Tyrant.Core.Install.GameInstall install, ImportedMod imported)
    {
        if (imported.Extracted > 0)
            Console.WriteLine($"{imported.Extracted} textures were extracted from its cooked pack; they are slightly lower quality than the author's originals.");
        foreach (var warning in imported.Warnings) Console.WriteLine($"  WARN   {warning}");
        var check = ModCli.Check(ws, install, imported.Mod);
        foreach (var error in check.Errors) Console.WriteLine($"  ERROR  {error}");
        foreach (var warning in check.Warnings) Console.WriteLine($"  WARN   {warning}");
        return ExitCodes.Ok;
    }
}

/// <summary>tyrant mod copy-from-game: the Mods tab's Copy to workspace.</summary>
public sealed class ModCopyFromGameCommand : Command<ModCopyFromGameCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandOption("--replace")]
        [Description("Replace a mod with the same id already in the workspace.")]
        public bool Replace { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var imported = ModSharing.CopyFromGame(ws, install, settings.Id, settings.Replace);
        Console.WriteLine($"Copied '{imported.Mod.Id}' from the game into the workspace (mods\\{imported.Mod.Id}).");
        return ModImportCommand.Report(ws, install, imported);
    }
}
