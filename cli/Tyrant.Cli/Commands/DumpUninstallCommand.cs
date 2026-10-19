using System.ComponentModel;
using Tyrant.Core.Dumping;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class DumpUninstallCommand : Command<DumpUninstallCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings, IGameLooksChoice
    {
        [CommandOption("--bring-in-looks")]
        [Description("First bring the looks made in the game into the workspace: into each workspace mod (the game's copy wins), and mods only in the game copied there.")]
        public bool BringInLooks { get; set; }

        [CommandOption("--discard-game-looks")]
        [Description("Let the looks made in the installed mods go.")]
        public bool DiscardGameLooks { get; set; }

        public override Spectre.Console.ValidationResult Validate() =>
            BringInLooks && DiscardGameLooks ? Spectre.Console.ValidationResult.Error("Use --bring-in-looks or --discard-game-looks, not both.") : base.Validate();
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        GameLooksCli.SettleAll(ws, install, settings, "uninstalling Tyrant from the game");
        var result = new ModLoaderInstaller(isGameRunning: CliServices.Launcher.IsRunning).Uninstall(install);
        Console.WriteLine(ModLoaderInstaller.UninstallSummary(result));
        return ExitCodes.Ok;
    }
}
