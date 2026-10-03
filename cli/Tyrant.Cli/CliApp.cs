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
            config.PropagateExceptions();
            config.AddBranch("install", install =>
            {
                install.AddCommand<InstallDetectCommand>("detect")
                    .WithDescription("Find the Prehistoric Kingdom install and print its build fingerprint.");
            });
            config.AddBranch("workspace", workspace =>
            {
                workspace.AddCommand<WorkspaceInitCommand>("init")
                    .WithDescription("Create a workspace folder (must be outside the game folder).");
                workspace.AddCommand<WorkspaceStatusCommand>("status")
                    .WithDescription("Show the workspace's game, build and output freshness.");
            });
            config.AddCommand<DecompileCommand>("decompile")
                .WithDescription("Decompile game assemblies into <workspace>/source.");
            config.AddBranch("assets", assets =>
            {
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
            config.AddBranch("species", species =>
            {
                species.AddCommand<SpeciesListCommand>("list")
                    .WithDescription("List the animals the game ships (park and vivarium).");
                species.AddCommand<SpeciesPackCommand>("pack")
                    .WithDescription("Export one species' models, textures and targets.json to <workspace>/assets/species/<key>.");
            });
            config.AddBranch("dump", dump =>
            {
                dump.AddCommand<DumpInstallCommand>("install")
                    .WithDescription("Install MelonLoader and the dumper mod into the game folder (reversible).");
                dump.AddCommand<DumpRunCommand>("run")
                    .WithDescription("Start the game once and dump every game database into <workspace>/data.");
                dump.AddCommand<DumpUninstallCommand>("uninstall")
                    .WithDescription("Remove exactly what 'tyrant dump install' added.");
            });
            config.AddBranch("mod", mod =>
            {
                mod.AddCommand<ModNewCommand>("new").WithDescription("Create a mod in the workspace (mods/<id>).");
                mod.AddCommand<ModReplaceCommand>("replace").WithDescription("Replace a game texture with your PNG in a mod.");
                mod.AddCommand<ModCheckCommand>("check").WithDescription("Check a mod's files and targets before installing it.");
                mod.AddCommand<ModRestoreCutoutsCommand>("restore-cutouts").WithDescription("Copy the see-through parts (feathers, hair) of the vanilla textures back into colour PNGs that lost them.");
                mod.AddCommand<ModInstallCommand>("install").WithDescription("Install a mod into the game (and Tyrant's framework, if needed).");
                mod.AddCommand<ModRemoveCommand>("remove").WithDescription("Remove a mod from the game.");
                mod.AddCommand<ModEnableCommand>("enable").WithDescription("Turn an installed mod on.");
                mod.AddCommand<ModDisableCommand>("disable").WithDescription("Turn an installed mod off.");
                mod.AddCommand<ModAddSkinCommand>("add-skin").WithDescription("Add a new skin to a species, starting from a vanilla skin's textures.");
                mod.AddCommand<ModCleanSkinsCommand>("clean-skins").WithDescription("List or forget skin numbers left by removed mods.");
                mod.AddCommand<ModListCommand>("list").WithDescription("List your mods and what is installed in the game.");
            });
            config.AddBranch("data", data =>
            {
                data.AddCommand<DataTypesCommand>("types").WithDescription("List dumped data types and object counts.");
                data.AddCommand<DataShowCommand>("show").WithDescription("List objects of a type, or print one object's JSON.");
                data.AddCommand<DataExportCommand>("export").WithDescription("Export all objects of a type as CSV or JSON.");
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
