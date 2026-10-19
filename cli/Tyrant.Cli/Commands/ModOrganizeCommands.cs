using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Cli.Commands;

// Organizing a mod's skins and deleting a whole mod: the same operations as the app's mod editor and Mods list.

public sealed class ModDeleteCommand : Command<ModDeleteCommand.Settings>
{
    public sealed class Settings : GameLooksSettings
    {
        [CommandOption("--keep-in-game")]
        [Description("Keep the installed copy in the game (by default it is removed too).")]
        public bool KeepInGame { get; set; }

        [CommandOption("--yes")]
        [Description("Delete it. Without this, only say what would go.")]
        public bool Yes { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        if (!settings.Yes)
        {
            if (!ModProject.Ids(ws).Contains(settings.Id, StringComparer.Ordinal))
                throw new TyrantException(TyrantErrorCode.ModNotFound, $"There is no mod '{settings.Id}' in this workspace ({ModProject.RootOf(ws)}).");
            Console.WriteLine($"This sends {Path.Combine(ModProject.RootOf(ws), settings.Id)} and its Blender projects to the Recycle Bin" +
                              (settings.KeepInGame ? "." : ", and removes it from the game if it is installed."));
            if (!settings.KeepInGame)
            {
                try
                {
                    GameLooksCli.SettleInstalled(ws, install, settings.Id, new LooksCheckOnly(), "deleting it with its copy in the game");
                }
                catch (TyrantException ex) when (ex.Code == TyrantErrorCode.GameLooksPending)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            Console.WriteLine($"Run 'tyrant mod delete {settings.Id} --yes{(settings.KeepInGame ? " --keep-in-game" : "")}' to do it.");
            return ExitCodes.Usage;
        }
        if (!settings.KeepInGame) GameLooksCli.SettleInstalled(ws, install, settings.Id, settings, "deleting it with its copy in the game");
        var removed = ModProject.Delete(ws, install, settings.Id, !settings.KeepInGame, new GameMods(CliServices.Launcher.IsRunning));
        Console.WriteLine($"Deleted '{settings.Id}'. In the Recycle Bin:");
        foreach (var path in removed.Recycled) Console.WriteLine($"  {path}");
        foreach (var line in removed.Kept) Console.WriteLine($"  stayed  {line}");
        Console.WriteLine("Its skin numbers stay reserved, so saved animals keep their skins if it comes back; 'tyrant mod clean-skins' frees them.");
        return ExitCodes.Ok;
    }
}

public sealed class ModGroupCommand : Command<ModGroupCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SKINS>")]
        [Description("The skins' ids (see 'tyrant mod show').")]
        public string[] Skins { get; set; } = [];

        [CommandOption("--group <NAME>")]
        [Description("The group to put them in, e.g. \"Jurassic Park\" (made if new). Groups are Tyrant's only; the game ignores them.")]
        public string? Group { get; set; }

        [CommandOption("--none")]
        [Description("Take them out of their group.")]
        public bool None { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (settings.None == (settings.Group is not null))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Give --group <NAME> to put the skins in a group, or --none to take them out.");
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        foreach (var id in settings.Skins) mod.Skin(id); // an unknown id fails before anything changes
        var moving = settings.Skins.ToHashSet(StringComparer.Ordinal);
        var group = settings.None ? null : settings.Group;
        mod.ArrangeSkins([
            .. mod.Manifest.Skins.Where(s => !moving.Contains(s.Id)).Select(s => new SkinPlace(s.Id, s.Group)),
            .. mod.Manifest.Skins.Where(s => moving.Contains(s.Id)).Select(s => new SkinPlace(s.Id, group)),
        ]);
        var saved = mod.Skin(settings.Skins[0]).Group;
        Console.WriteLine(saved is null ? $"{string.Join(", ", settings.Skins)}: no group." : $"{string.Join(", ", settings.Skins)} in group '{saved}'.");
        return ExitCodes.Ok;
    }
}

public sealed class ModMoveSkinCommand : Command<ModMoveSkinCommand.Settings>
{
    public sealed class Settings : ModSkinSettings
    {
        [CommandOption("--up")]
        [Description("One place up, within its group.")]
        public bool Up { get; set; }

        [CommandOption("--down")]
        [Description("One place down, within its group.")]
        public bool Down { get; set; }

        [CommandOption("--to <N>")]
        [Description("To this place within its group (1 = first).")]
        public int? To { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if ((settings.Up ? 1 : 0) + (settings.Down ? 1 : 0) + (settings.To is null ? 0 : 1) != 1)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Give one of --up, --down or --to <N>.");
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var skin = mod.Skin(settings.Skin);
        bool InSection(SkinEntry s) => string.Equals(s.Group, skin.Group, StringComparison.OrdinalIgnoreCase);
        var section = mod.Manifest.Skins.Where(InSection).ToList();
        var at = section.IndexOf(skin);
        var to = Math.Clamp(settings.To is int n ? n - 1 : at + (settings.Up ? -1 : 1), 0, section.Count - 1);
        section.RemoveAt(at);
        section.Insert(to, skin);
        var reordered = new Queue<SkinEntry>(section); // the section's places in the list, refilled in the new order
        mod.ArrangeSkins(mod.Manifest.Skins.Select(s => InSection(s) ? reordered.Dequeue() : s).Select(s => new SkinPlace(s.Id, s.Group)).ToList());
        Console.WriteLine($"'{skin.Name}' is now {to + 1} of {section.Count}{(skin.Group is null ? " ungrouped skins" : $" in '{skin.Group}'")}.");
        return ExitCodes.Ok;
    }
}

/// <summary>The delete dry-run: only says what the looks check would refuse.</summary>
internal sealed class LooksCheckOnly : IGameLooksChoice
{
    public bool BringInLooks => false;
    public bool DiscardGameLooks => false;
}
