using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
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

/// <summary>tyrant mod looks: the material folders the in-game editor (or the workspace) changed since install, and bringing them in.</summary>
public sealed class ModLooksCommand : Command<ModLooksCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandOption("--bring-in")]
        [Description("Replace the workspace's material folders with the game's copy (every folder changed in the game, or the --folder ones).")]
        public bool BringIn { get; set; }

        [CommandOption("--folder <FOLDER>")]
        [Description("A material folder to bring in, as listed (e.g. materials/species/Carcharodontosaurus); repeat for more.")]
        public string[] Folders { get; set; } = [];

        public override Spectre.Console.ValidationResult Validate() =>
            Folders.Length > 0 && !BringIn ? Spectre.Console.ValidationResult.Error("--folder needs --bring-in.") : base.Validate();
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var changes = GameLooks.Changes(install, mod);
        if (settings.BringIn)
        {
            var folders = settings.Folders.Length > 0 ? settings.Folders : changes.Where(c => c.GameChanged).Select(c => c.Folder).ToArray();
            if (folders.Length == 0)
            {
                Console.WriteLine($"No looks in the game differ from '{mod.Id}' in the workspace; nothing to bring in.");
                return ExitCodes.Ok;
            }
            var brought = GameLooks.BringIn(install, mod, folders);
            foreach (var folder in folders) Console.WriteLine($"  brought in  {folder}");
            Console.WriteLine($"Brought {brought} look folder(s) from the game into '{mod.Id}' (mods\\{mod.Id}).");
            return ExitCodes.Ok;
        }
        if (changes.Count == 0)
        {
            Console.WriteLine($"The looks of '{mod.Id}' in the game are as installed.");
            return ExitCodes.Ok;
        }
        var width = changes.Max(c => c.Folder.Length);
        foreach (var change in changes)
            Console.WriteLine($"  {change.Folder.PadRight(width)}  changed {(change.GameChanged && change.ProjectChanged ? "in the game and in the project" : change.GameChanged ? "in the game" : "in the project")}");
        if (changes.Any(c => c.GameChanged))
            Console.WriteLine($"Bring the game's into the workspace with 'tyrant mod looks {mod.Id} --bring-in' (add --folder <FOLDER> for some only); installing or removing the mod would throw them away.");
        return ExitCodes.Ok;
    }
}

/// <summary>--bring-in-looks / --discard-game-looks, on every command that throws away a copy in the game.</summary>
internal interface IGameLooksChoice
{
    bool BringInLooks { get; }
    bool DiscardGameLooks { get; }
}

/// <summary>
/// Install, remove, delete and uninstall: refused while an installed copy has looks made in the game (or Tyrant cannot tell),
/// unless they are brought in (--bring-in-looks) or let go (--discard-game-looks).
/// </summary>
internal static class GameLooksCli
{
    public static void Settle(GameInstall install, ModProject mod, IGameLooksChoice choice, string action)
    {
        if (choice.DiscardGameLooks) return;
        List<LooksChange> pending;
        try
        {
            pending = GameLooks.Changes(install, mod).Where(c => c.GameChanged).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TyrantException(TyrantErrorCode.GameLooksPending,
                $"Tyrant could not compare '{mod.Id}' in the game with the workspace, so it cannot tell whether {action} would throw away looks made there ({ex.Message}). " +
                "Close the game or the program holding the file and try again, or add --discard-game-looks.", FixAction.None, ex);
        }
        if (pending.Count == 0) return;
        if (!choice.BringInLooks)
            throw new TyrantException(TyrantErrorCode.GameLooksPending,
                $"'{mod.Id}' in the game has looks made there ({GameLooks.Describe(pending)}) that {action} would throw away. " +
                $"Add --bring-in-looks to bring them into the workspace first (or run 'tyrant mod looks {mod.Id} --bring-in'), or --discard-game-looks to let them go.");
        GameLooks.BringIn(install, mod, pending.Select(c => c.Folder).ToList());
        Console.WriteLine($"Brought the looks made in the game into '{mod.Id}' in the workspace: {GameLooks.Describe(pending)}.");
    }

    /// <summary>
    /// One installed mod by id, in the workspace or only in the game. A workspace copy that cannot be read is refused when the
    /// game's copy has material folders (Tyrant cannot tell what the game changed), unless --discard-game-looks.
    /// </summary>
    public static void SettleInstalled(Workspace ws, GameInstall install, string id, IGameLooksChoice choice, string action)
    {
        if (choice.DiscardGameLooks) return;
        if (!ModProject.Ids(ws).Contains(id, StringComparer.Ordinal))
        {
            SettleGameOnly(ws, install, id, choice, action);
            return;
        }
        ModProject mod;
        try
        {
            mod = ModProject.Open(ws, id);
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.ModInvalid or TyrantErrorCode.ModNotFound)
        {
            var installed = Path.Combine(ModLoaderInstaller.ModsDir(install), id);
            if (!Directory.Exists(installed) || !GameLooks.HasMaterials(installed)) return; // no looks in the game to lose
            throw new TyrantException(TyrantErrorCode.GameLooksPending,
                $"'{id}' in the workspace cannot be read, so Tyrant cannot tell whether {action} would throw away looks made in the game ({ex.Message}). " +
                "Fix the workspace copy, or add --discard-game-looks to go ahead anyway.", FixAction.None, ex);
        }
        Settle(install, mod, choice, action);
    }

    /// <summary>A mod only in the game (such as Vanilla Looks) with material folders, so looks made there: --bring-in-looks copies it into the workspace first.</summary>
    public static void SettleGameOnly(Workspace ws, GameInstall install, string id, IGameLooksChoice choice, string action)
    {
        var installed = Path.Combine(ModLoaderInstaller.ModsDir(install), id);
        if (!ModId.IsValid(id) || !Directory.Exists(installed) || !GameLooks.HasMaterials(installed) || choice.DiscardGameLooks) return;
        if (!choice.BringInLooks)
            throw new TyrantException(TyrantErrorCode.GameLooksPending,
                $"'{id}' is only in the game and holds looks made there; {action} would throw them away. " +
                $"Add --bring-in-looks to copy it into the workspace first (as 'tyrant mod copy-from-game {id}'), or --discard-game-looks to let them go.");
        ModSharing.CopyFromGame(ws, install, id, replace: false);
        Console.WriteLine($"Copied '{id}' from the game into the workspace (mods\\{id}).");
    }

    /// <summary>Every installed mod (uninstall): all refusals in one message, so one run names everything at stake.</summary>
    public static void SettleAll(Workspace ws, GameInstall install, IGameLooksChoice choice, string action)
    {
        if (choice.DiscardGameLooks) return;
        var problems = new List<string>();
        foreach (var mod in new GameMods().List(install))
        {
            try
            {
                SettleInstalled(ws, install, mod.Id, choice, action);
            }
            catch (TyrantException ex) when (ex.Code == TyrantErrorCode.GameLooksPending)
            {
                problems.Add(ex.Message);
            }
        }
        if (problems.Count > 0) throw new TyrantException(TyrantErrorCode.GameLooksPending, string.Join(" ", problems));
    }
}
