using Spectre.Console;
using Tyrant.Cli.Commands;
using Tyrant.Core.Errors;
using Spectre.Console.Cli;

namespace Tyrant.Cli;

public static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int Error = 2;
    public const int Partial = 3;
}

public static class CliApp
{
    public static int Run(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("tyrant");
            config.ConfigureConsole(AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out) })); // help goes to the current output
            config.PropagateExceptions();
            config.AddBranch("install", install =>
            {
                install.SetDescription("Find the game install.");
                install.AddCommand<InstallDetectCommand>("detect")
                    .WithDescription("Find the Prehistoric Kingdom install and print its build fingerprint.");
            });
            config.AddBranch("workspace", workspace =>
            {
                workspace.SetDescription("Create a workspace and check what in it is out of date.");
                workspace.AddCommand<WorkspaceInitCommand>("init")
                    .WithDescription("Create a workspace folder (must be outside the game folder).");
                workspace.AddCommand<WorkspaceStatusCommand>("status")
                    .WithDescription("Show the workspace's game, build and output freshness.");
            });
            config.AddCommand<DecompileCommand>("decompile")
                .WithDescription("Decompile game assemblies into <workspace>/source.");
            config.AddBranch("assets", assets =>
            {
                assets.SetDescription("Index, search and export the game's textures and models.");
                assets.AddCommand<AssetsIndexCommand>("index")
                    .WithDescription("Index every object in the game's Addressables bundles.");
                assets.AddCommand<AssetsListCommand>("list")
                    .WithDescription("Search the asset index.");
                assets.AddCommand<AssetsDumpCommand>("dump")
                    .WithDescription("Print one object's fields as JSON.");
                assets.AddCommand<AssetsExportTexturesCommand>("export-textures")
                    .WithDescription("Export matching textures to <workspace>/assets/textures as PNG.");
                assets.AddCommand<AssetsExportModelCommand>("export-model")
                    .WithDescription("Export a prefab's meshes (with skeleton and blend shapes) to <workspace>/assets/models as .glb.");
            });
            config.AddBranch("sounds", sounds =>
            {
                sounds.SetDescription("The game's sounds, for sound replacements (needs the data dump).");
                sounds.AddCommand<SoundsListCommand>("list").WithDescription("List a species' sounds, or search every game sound.");
            });
            config.AddBranch("species", species =>
            {
                species.SetDescription("List species and export a species pack.");
                species.AddCommand<SpeciesListCommand>("list")
                    .WithDescription("List the animals the game ships (park and vivarium).");
                species.AddCommand<SpeciesStatsCommand>("stats")
                    .WithDescription("Show a species' purchase cost and basic stats from the game data (what a new species based on it inherits).");
                species.AddCommand<SpeciesTexturesCommand>("textures")
                    .WithDescription("List a species' skin textures (to replace them in a mod with 'tyrant mod replace').");
                species.AddCommand<SpeciesIkCommand>("ik")
                    .WithDescription("List a species' IK chains (Open in Blender builds them as IK controls).");
                species.AddCommand<SpeciesAnimationsCommand>("animations").WithDescription("List a species' animations in the game (for Blender and exports).");
                species.AddCommand<SpeciesExportAnimationsCommand>("export-animations").WithDescription("Export a species' animations as FBX (default) or glb files.");
                species.AddCommand<SpeciesRigInfoCommand>("rig-info")
                    .WithDescription("List the bones a species' animations move and the bones its growth positions or scales (for rig edits).");
                species.AddCommand<SpeciesPackCommand>("pack")
                    .WithDescription("Export one species' models, textures and targets.json to <workspace>/assets/species/<key>.");
            });
            config.AddBranch("dump", dump =>
            {
                dump.SetDescription("Install the dumper mod and dump the game's databases (starts the game).");
                dump.AddCommand<DumpInstallCommand>("install")
                    .WithDescription("Install MelonLoader and the dumper mod into the game folder (reversible).");
                dump.AddCommand<DumpRunCommand>("run")
                    .WithDescription("Start the game once and dump every game database into <workspace>/data.");
                dump.AddCommand<DumpUninstallCommand>("uninstall")
                    .WithDescription("Remove exactly what 'tyrant dump install' added.");
            });
            config.AddBranch("game", game =>
            {
                game.SetDescription("Tyrant in the game: MelonLoader and Tyrant's framework (the Workspace tab's 'Tyrant in the game').");
                game.AddCommand<GameStatusCommand>("status").WithDescription("Show the framework version in the game and in this Tyrant.");
                game.AddCommand<DumpInstallCommand>("install").WithDescription("Install MelonLoader (if needed) and Tyrant's framework and dumper into the game.");
                game.AddCommand<DumpInstallCommand>("update").WithDescription("Update Tyrant's framework and dumper in the game to this Tyrant's.");
                game.AddCommand<DumpUninstallCommand>("uninstall").WithDescription("Remove Tyrant (and MelonLoader, if Tyrant installed it) from the game.");
                game.AddCommand<GamePackageFrameworkCommand>("package-framework").WithDescription("Save Tyrant's framework as a zip for players without Tyrant.");
            });

            config.AddBranch("tpack", tpack =>
            {
                tpack.SetDescription("Cooked texture packs (textures.tpack).");
                tpack.AddCommand<TpackExtractCommand>("extract").WithDescription("Write a texture pack's textures as PNGs into a folder.");
            });
            config.AddBranch("blender", blender =>
            {
                blender.SetDescription("Open models in Blender with Tyrant's add-on and send them back (the Workspace tab's Blender card, Open in Blender).");
                blender.AddCommand<BlenderStatusCommand>("status").WithDescription("Show the Blender Tyrant uses and its add-on.");
                blender.AddCommand<BlenderSetPathCommand>("set-path").WithDescription("Use this blender.exe (or --clear to find Blender again).");
                blender.AddCommand<BlenderInstallAddonCommand>("install-addon").WithDescription("Install or update Tyrant's add-on in Blender.");
                blender.AddCommand<BlenderOpenCommand>("open").WithDescription("Open a species, a skin or a mod's model in Blender.");
                blender.AddCommand<BlenderDestinationsCommand>("destinations").WithDescription("List where a Blender project can be sent (JSON, for the add-on).");
                blender.AddCommand<BlenderIkDataCommand>("ik-data").WithDescription("Write the game's IK chains into an older Blender project (JSON, for the add-on).");
                blender.AddCommand<BlenderAnimationsCommand>("animations").WithDescription("Write animations next to a Blender project (JSON, for the add-on).");
                blender.AddCommand<BlenderAnimationListCommand>("animation-list").WithDescription("Write the species' animation list into an older Blender project (JSON, for the add-on).");
                blender.AddCommand<BlenderSendCommand>("send").WithDescription("Add a model exported from a Blender project to its mod (JSON, for the add-on).");
            });

            config.AddBranch("mod", mod =>
            {
                mod.SetDescription("Make, check and install mods: texture, model and sound replacements and new skins.");
                mod.AddCommand<ModNewCommand>("new").WithDescription("Create a mod in the workspace (mods/<id>).");
                mod.AddCommand<ModReplaceCommand>("replace").WithDescription("Replace a game texture with your PNG in a mod.");
                mod.AddCommand<ModReplaceModelCommand>("replace-model").WithDescription("Replace a species' model (or give a skin its own) with your .glb or .fbx.");
                mod.AddCommand<ModRemoveModelCommand>("remove-model").WithDescription("Stop replacing a species' model, or make a skin use the species model again.");
                mod.AddCommand<ModPrefabCommand>("prefab").WithDescription("List, set or reset a species' prefab settings (component fields, e.g. the swimming waterline).");
                mod.AddCommand<ModSizeCommand>("size").WithDescription("Show, set or clear a species' sizes (adult, newborn share, male and female).");
                mod.AddCommand<ModSizeRangesCommand>("size-ranges").WithDescription("Show, set or clear the genetic size ranges of a mod's nursery and breeding.");
                mod.AddCommand<ModReplaceSoundCommand>("replace-sound").WithDescription("Replace a game sound (for everyone, one species or one skin) with your audio files.");
                mod.AddCommand<ModReplaceSoundsCommand>("replace-sounds").WithDescription("Replace a folder of sounds at once, matched to the game's sounds by file name.");
                mod.AddCommand<ModSetSoundCommand>("set-sound").WithDescription("Change a sound replacement's volume, age pitch or who hears it.");
                mod.AddCommand<ModRemoveSoundCommand>("remove-sound").WithDescription("Stop replacing a game sound.");
                mod.AddCommand<ModRigCommand>("rig").WithDescription("Show or clear a species' or a skin's rig edit (made in Blender).");
                mod.AddCommand<ModRebuildModelsCommand>("rebuild-models").WithDescription("Rebuild a mod's models and their levels of detail.");
                mod.AddCommand<ModCheckCommand>("check").WithDescription("Check a mod's files and targets before installing it.");
                mod.AddCommand<ModRestoreCutoutsCommand>("restore-cutouts").WithDescription("Copy the see-through parts (feathers, hair) of the vanilla textures back into colour PNGs that lost them.");
                mod.AddCommand<ModExportCommand>("export").WithDescription("Export a mod as a zip players unzip into the game folder.");
                mod.AddCommand<ModImportCommand>("import").WithDescription("Add a mod from a zip to the workspace.");
                mod.AddCommand<ModInstallCommand>("install").WithDescription("Install a mod into the game (and Tyrant's framework, if needed).");
                mod.AddCommand<ModCopyFromGameCommand>("copy-from-game").WithDescription("Copy a mod that is only in the game folder into the workspace (its PNGs extracted from its pack).");
                mod.AddCommand<ModCookCommand>("cook").WithDescription("Cook a mod's textures into textures.tpack in its workspace folder, without installing.");
                mod.AddCommand<ModRemoveCommand>("remove").WithDescription("Remove a mod from the game.");
                mod.AddCommand<ModLooksCommand>("looks").WithDescription("List the looks made in the game (material folders the in-game editor changed), or bring them into the workspace.");
                mod.AddCommand<ModDeleteCommand>("delete").WithDescription("Delete a mod from the workspace (to the Recycle Bin), and from the game unless --keep-in-game.");
                mod.AddCommand<ModEnableCommand>("enable").WithDescription("Turn an installed mod on.");
                mod.AddCommand<ModDisableCommand>("disable").WithDescription("Turn an installed mod off.");
                mod.AddCommand<ModAddSkinCommand>("add-skin").WithDescription("Add a new skin to a species, starting from a vanilla skin's textures.");
                mod.AddCommand<ModAddSpeciesCommand>("add-species").WithDescription("Experimental: add a distinct species using a vanilla regular animal as its template.");
                mod.AddCommand<ModRemoveSpeciesCommand>("remove-species").WithDescription("Remove a custom species with its skins, models, sounds and (unless --keep-files) files.");
                mod.AddCommand<ModAddStartingSkinCommand>("add-starting-skin").WithDescription("Give a custom species an editable starting skin (copied from a game skin, or your PNGs).");
                mod.AddCommand<ModSetSpeciesSkinCommand>("set-species-skin").WithDescription("Set a custom species' starting skin, its name and its swatch PNG.");
                mod.AddCommand<ModSetStartingSkinCommand>("set-starting-skin").WithDescription("Make one of a custom species' skins its starting (default) skin; the old one stays as an ordinary skin.");
                mod.AddCommand<ModSkinPortraitCommand>("set-skin-portrait").WithDescription("Set or clear one of a skin's in-game portraits.");
                mod.AddCommand<ModSpeciesPortraitCommand>("set-species-portrait").WithDescription("Set or clear one of a custom species' starting skin portraits.");
                mod.AddCommand<ModSpeciesPhotoCommand>("set-species-photo").WithDescription("Set or clear a custom species' Paleopedia photo (also the Nursery species panel picture).");
                mod.AddCommand<ModCleanSkinsCommand>("clean-skins").WithDescription("List or forget skin numbers left by removed mods.");
                mod.AddCommand<ModListCommand>("list").WithDescription("List your mods and what is installed in the game.");
                mod.AddCommand<ModShowCommand>("show").WithDescription("Show a mod's details, replacements and skins.");
                mod.AddCommand<ModSetCommand>("set").WithDescription("Change a mod's name, version, author or description.");
                mod.AddCommand<ModRenameSkinCommand>("rename-skin").WithDescription("Change a skin's shown name (its id stays).");
                mod.AddCommand<ModRemoveSkinCommand>("remove-skin").WithDescription("Remove a skin from a mod, optionally with its files.");
                mod.AddCommand<ModGroupCommand>("group").WithDescription("Put skins in a group in the mod editor (Tyrant only), or take them out.");
                mod.AddCommand<ModMoveSkinCommand>("move-skin").WithDescription("Move a skin up, down or to a place within its group.");
                mod.AddCommand<ModColorsCommand>("colors").WithDescription("Show, set (from a JSON file) or clear a skin's colours.");
                mod.AddCommand<ModMaterialsCommand>("materials").WithDescription("Show, set (from a JSON file), clear or import images for a skin's or species' material settings.");
                mod.AddCommand<ModSkinFileCommand>("skin-file").WithDescription("Use your PNG for one slot of a skin, or the base skin's texture.");
                mod.AddCommand<ModThumbnailCommand>("thumbnail").WithDescription("Set a skin's swatch PNG, or let the game make one.");
                mod.AddCommand<ModUnreplaceCommand>("unreplace").WithDescription("Stop replacing a game texture.");
                mod.AddCommand<ModSetCompressionCommand>("set-compression").WithDescription("Choose how Tyrant compresses the mod's textures (auto or bc7), or one texture's.");
            });
            config.AddBranch("data", data =>
            {
                data.SetDescription("Browse and export the dumped game data.");
                data.AddCommand<DataTypesCommand>("types").WithDescription("List dumped data types and object counts.");
                data.AddCommand<DataShowCommand>("show").WithDescription("List objects of a type, or print one object's JSON.");
                data.AddCommand<DataExportCommand>("export").WithDescription("Export all objects of a type as CSV or JSON.");
            });
            config.AddBranch("update", update =>
            {
                update.SetDescription("Check for a newer Tyrant (the app installs it: Help → Check for updates).");
                update.AddCommand<UpdateCheckCommand>("check").WithDescription("Show this version and the latest on GitHub.");
            });

            config.AddCommand<RpcCommand>("rpc")
                .WithDescription("Serve the Tyrant app over JSON-RPC on stdin/stdout (sidecar mode).");
        });

        try
        {
            return app.Run(args);
        }
        catch (TyrantException ex)
        {
            CliServices.PrintError(ex);
            return ExitCodes.Error;
        }
        catch (CommandAppException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // File-system and bad-path problems are user-facing; anything else is a bug and keeps its stack trace.
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitCodes.Error;
        }
    }
}
