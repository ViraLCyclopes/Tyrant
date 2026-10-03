using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Assets;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Cli.Commands;

public class ModSettings : WorkspaceSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("The mod id (its folder name), e.g. red-spot-carcharo.")]
    public string Id { get; set; } = "";
}

internal static class ModCli
{
    public static AssetIndex? TryIndex(Workspace ws)
    {
        try
        {
            return AssetIndex.Load(AssetIndex.PathIn(ws));
        }
        catch (TyrantException)
        {
            return null;
        }
    }

    /// <summary>Prints a check; returns false when it has errors.</summary>
    public static bool Print(ModCheckResult result)
    {
        foreach (var error in result.Errors) Console.WriteLine($"  ERROR  {error}");
        foreach (var warning in result.Warnings) Console.WriteLine($"  WARN   {warning}");
        if (result.Errors.Count == 0 && result.Warnings.Count == 0) Console.WriteLine("  OK     no problems found");
        return result.Ok;
    }

    public static string Label(ModInstallState state) => state switch
    {
        ModInstallState.Installed => "installed",
        ModInstallState.Changed => "changed since install",
        _ => "not installed",
    };
}

public sealed class ModNewCommand : Command<ModNewCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; set; }

        [CommandOption("--author <AUTHOR>")]
        public string? Author { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Create(ws, settings.Id, settings.Name, settings.Author);
        Console.WriteLine($"Created mod '{mod.Id}' in {mod.Dir}");
        return ExitCodes.Ok;
    }
}

public sealed class ModReplaceCommand : Command<ModReplaceCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<TEXTURE>")]
        [Description("The game texture: its name, Addressables path, GUID or ref.")]
        public string Texture { get; set; } = "";

        [CommandArgument(2, "[PNG]")]
        [Description("Your PNG (default: the texture's exported PNG under assets/textures).")]
        public string? Png { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var entry = mod.Replace(ws, CliServices.LoadIndex(ws, install), settings.Texture, settings.Png);
        Console.WriteLine($"  {entry.Texture} <- {entry.File}");
        return ExitCodes.Ok;
    }
}

public sealed class ModCheckCommand : Command<ModSettings>
{
    public override int Execute(CommandContext context, ModSettings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        return ModCli.Print(ModChecker.ForGame(install).Check(mod, ModCli.TryIndex(ws))) ? ExitCodes.Ok : ExitCodes.Error;
    }
}

public sealed class ModInstallCommand : Command<ModInstallCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandOption("--loader-zip <PATH>")]
        [Description("Use an already-downloaded MelonLoader.x64.zip (0.7.3) if MelonLoader has to be installed.")]
        public string? LoaderZip { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        if (!ModCli.Print(ModChecker.ForGame(install).Check(mod, ModCli.TryIndex(ws))))
        {
            Console.WriteLine("Fix the errors above, then install again.");
            return ExitCodes.Error;
        }
        var launcher = new SteamLauncher();
        if (!ModLoaderInstaller.HasFramework(install))
        {
            var zip = settings.LoaderZip;
            if (zip is null && ModLoaderInstaller.GetState(install) == InstallState.NotInstalled)
            {
                Console.WriteLine($"Downloading {ModLoaderInstaller.LoaderName} {ModLoaderInstaller.Version} ...");
                using var http = new HttpClient();
                zip = ModLoaderInstaller.DownloadAsync(http, Path.Combine(ws.CacheDir, "downloads"), CancellationToken.None).GetAwaiter().GetResult();
            }
            new ModLoaderInstaller(isGameRunning: launcher.IsRunning).Install(install, zip, Path.Combine(AppContext.BaseDirectory, "dumper"));
            Console.WriteLine("Installed Tyrant's framework into the game.");
        }
        new GameMods(launcher.IsRunning).Install(install, mod);
        Console.WriteLine($"Installed '{mod.Id}' into {Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id)}. Start the game to see it; undo with 'tyrant mod remove {mod.Id}'.");
        return ExitCodes.Ok;
    }
}

public sealed class ModRemoveCommand : Command<ModSettings>
{
    public override int Execute(CommandContext context, ModSettings settings)
    {
        var (_, install) = CliServices.OpenWorkspace(settings);
        new GameMods(new SteamLauncher().IsRunning).Remove(install, settings.Id);
        Console.WriteLine($"Removed '{settings.Id}' from the game.");
        return ExitCodes.Ok;
    }
}

public sealed class ModEnableCommand : Command<ModSettings>
{
    public override int Execute(CommandContext context, ModSettings settings)
    {
        var (_, install) = CliServices.OpenWorkspace(settings);
        new GameMods().SetEnabled(install, settings.Id, true);
        Console.WriteLine($"'{settings.Id}' is on (from the next game start).");
        return ExitCodes.Ok;
    }
}

public sealed class ModDisableCommand : Command<ModSettings>
{
    public override int Execute(CommandContext context, ModSettings settings)
    {
        var (_, install) = CliServices.OpenWorkspace(settings);
        new GameMods().SetEnabled(install, settings.Id, false);
        Console.WriteLine($"'{settings.Id}' is off (from the next game start).");
        return ExitCodes.Ok;
    }
}

public sealed class ModListCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mods = new GameMods();
        var installed = mods.List(install).ToDictionary(m => m.Id);
        Console.WriteLine(ModLoaderInstaller.HasFramework(install) ? "Tyrant framework: installed" : "Tyrant framework: not installed (installing a mod installs it)");
        foreach (var id in ModProject.Ids(ws))
        {
            try
            {
                var mod = ModProject.Open(ws, id);
                var state = mods.StateOf(install, mod);
                var onOff = installed.TryGetValue(id, out var inGame) ? (inGame.Enabled ? " (on)" : " (off)") : "";
                Console.WriteLine($"  {id,-28} {mod.Manifest.Version,-8} {ModCli.Label(state)}{onOff}  {mod.Manifest.Replace.Count} replacement(s)");
            }
            catch (TyrantException ex)
            {
                Console.WriteLine($"  {id,-28} cannot be read: {ex.Message}");
            }
            installed.Remove(id);
        }
        foreach (var other in installed.Values)
            Console.WriteLine($"  {other.Id,-28} {other.Version,-8} in the game only{(other.Enabled ? " (on)" : " (off)")}{(other.Error is null ? "" : "  " + other.Error)}");
        return ExitCodes.Ok;
    }
}
