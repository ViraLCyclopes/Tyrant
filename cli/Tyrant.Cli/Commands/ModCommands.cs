using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Assets;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
using Tyrant.Core.Workspaces;

namespace Tyrant.Cli.Commands;

public class ModSettings : WorkspaceSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("The mod id (its folder name), e.g. red-spot-carcharo.")]
    public string Id { get; set; } = "";
}

/// <summary>Reports on the calling thread, in order (System.Progress posts to the thread pool without a synchronization context).</summary>
internal sealed class SyncProgress(Action<(int Done, int Total)> report) : IProgress<(int Done, int Total)>
{
    public void Report((int Done, int Total) value) => report(value);
}

internal static class ModCli
{
    public static IReadOnlyList<SpeciesSkins>? TrySpecies(Workspace ws)
    {
        try
        {
            return SpeciesSkinsReader.Load(ws);
        }
        catch (TyrantException)
        {
            return null;
        }
    }

    /// <summary>The data dump's species plus the custom ones the mod's skins can be for (<see cref="ModProject.SpeciesFor"/>); null without the dump.</summary>
    public static IReadOnlyList<SpeciesSkins>? TrySpeciesFor(Workspace ws, GameInstall install, ModProject mod) =>
        TrySpecies(ws) is { } dump ? mod.SpeciesFor(ws, install, dump) : null;

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

    public static IReadOnlyList<SpeciesSkins> RequireSpecies(Workspace ws) => TrySpecies(ws) ?? throw new TyrantException(TyrantErrorCode.DataMissing,
        "Model replacements need the game's data: on the Workspace tab click Run data dump, or run 'tyrant dump run'.", FixAction.RefreshWorkspace);

    /// <summary><see cref="RequireSpecies"/> plus the custom species the mod's skins can be for (<see cref="ModProject.SpeciesFor"/>).</summary>
    public static IReadOnlyList<SpeciesSkins> RequireSpeciesFor(Workspace ws, GameInstall install, ModProject mod) => mod.SpeciesFor(ws, install, RequireSpecies(ws));

    /// <summary>Before Check and Install: models whose .glb changed are built again (needs the index and the data dump).</summary>
    /// <param name="measureHeadRigs">Install and Export measure species rig edits' head look again when out of date; Check does not write.</param>
    public static void RebuildStaleModels(Workspace ws, GameInstall install, ModProject mod, bool measureHeadRigs = false)
    {
        if (!mod.ModelEntries().Any() && !mod.Manifest.Models.Any(m => m.Rig is { Count: > 0 } || m.HeadRig is not null)) return;
        if (TryIndex(ws) is not { } index || TrySpeciesFor(ws, install, mod) is not { } species) return;
        foreach (var file in mod.RebuildStaleModels(install, index, species, CliServices.AssetReader))
            Console.WriteLine($"  rebuilt  {file} (it changed since it was built)");
        if (measureHeadRigs && mod.RefreshHeadRigs(ws, install, index, species, CliServices.AssetReader))
            Console.WriteLine("  re-measured the head look of its species-wide rig edits");
    }

    /// <summary>Prints a check; returns false when it has errors.</summary>
    /// <summary>Check as 'tyrant mod check' and the app's Mods tab do it: the asset index and, for skins and sounds, the data dump when present.</summary>
    public static ModCheckResult Check(Workspace ws, GameInstall install, ModProject mod)
    {
        var m = mod.Manifest;
        var index = TryIndex(ws);
        var species = TrySpecies(ws);
        var hasPrefabs = mod.PrefabTargets().Count > 0;
        var others = m.Skins.Count > 0 ? OtherModSpecies.Load(ws, new GameMods().List(install), mod.Id) : null;
        Func<string, IReadOnlyList<PrefabFieldInfo>?>? prefabFields = !hasPrefabs || index is null || species is null ? null : target =>
        {
            try
            {
                return PrefabFields.Read(ws, install, index, ModProject.WithSpecies(species, m), CliServices.AssetReader, target);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return null;
            }
        };
        return ModChecker.ForGame(install).Check(mod, index, m.Species.Count > 0 || m.Skins.Count > 0 || m.Sounds.Count > 0 || hasPrefabs ? species : null,
            m.Sounds.Count > 0 ? TrySounds(ws) : null,
            RigInfoProvider(ws, install, species is null ? null : ModProject.WithSpecies(species, m, others?.Species)), // a skin's rig edit may be on a custom species
            id => SpeciesDatabase.Template(ws, id), prefabFields, others: others);
    }

    /// <summary>
    /// Rig edits' species skeletons for Check (null without the asset index or the data dump). <paramref name="species"/>: where
    /// species are looked up (default: the data dump; give <see cref="TrySpeciesFor"/>'s for a mod's custom species).
    /// </summary>
    public static Func<string, Tyrant.Core.Rigging.RigInfo?>? RigInfoProvider(Workspace ws, GameInstall install, IReadOnlyList<SpeciesSkins>? species = null)
    {
        if (TryIndex(ws) is not { } index || (species ?? TrySpecies(ws)) is not { } lookup) return null;
        return id =>
        {
            try
            {
                return Tyrant.Core.Rigging.RigInfoService.For(ws, install, index, lookup, CliServices.AssetReader, id);
            }
            catch (TyrantException)
            {
                return null;
            }
        };
    }

    public static Tyrant.Core.Sounds.SoundCatalog? TrySounds(Workspace ws)
    {
        try
        {
            return Tyrant.Core.Sounds.SoundCatalog.Load(ws);
        }
        catch (TyrantException)
        {
            return null;
        }
    }

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
        ModCli.RebuildStaleModels(ws, install, mod);
        return ModCli.Print(ModCli.Check(ws, install, mod)) ? ExitCodes.Ok : ExitCodes.Error;
    }
}

public sealed class ModRestoreCutoutsCommand : Command<ModSettings>
{
    public override int Execute(CommandContext context, ModSettings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var index = CliServices.LoadIndex(ws, install); // errors when there is no index: the game textures would be unknown
        var result = new CutoutRestorer(Cutouts.GamePixels(install, new BundleAssetReader())).Restore(mod, index, ModCli.TrySpeciesFor(ws, install, mod));
        foreach (var file in result.Restored) Console.WriteLine($"  restored  {Path.Combine(mod.Dir, file.Replace('/', Path.DirectorySeparatorChar))}");
        foreach (var problem in result.Problems) Console.WriteLine($"  WARN      {problem}");
        if (result.Restored.Count == 0 && result.Problems.Count == 0) Console.WriteLine("No colour PNGs need their see-through parts restored.");
        if (result.Restored.Count > 0) Console.WriteLine($"Install again to update the game: 'tyrant mod install {mod.Id}'.");
        return result.Problems.Count == 0 ? ExitCodes.Ok : ExitCodes.Partial;
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
        ModCli.RebuildStaleModels(ws, install, mod, measureHeadRigs: true);
        if (!ModCli.Print(ModCli.Check(ws, install, mod)))
        {
            Console.WriteLine("Fix the errors above, then install again.");
            return ExitCodes.Error;
        }
        var launcher = CliServices.Launcher;
        var componentDir = Path.Combine(AppContext.BaseDirectory, "dumper");
        if (ModLoaderInstaller.FrameworkStatus(install, componentDir) != FrameworkState.Current)
        {
            var zip = settings.LoaderZip;
            if (zip is null && ModLoaderInstaller.GetState(install) == InstallState.NotInstalled)
            {
                Console.WriteLine($"Downloading {ModLoaderInstaller.LoaderName} {ModLoaderInstaller.Version} ...");
                using var http = new HttpClient();
                zip = ModLoaderInstaller.DownloadAsync(http, Path.Combine(ws.CacheDir, "downloads"), CancellationToken.None).GetAwaiter().GetResult();
            }
            new ModLoaderInstaller(isGameRunning: launcher.IsRunning).Install(install, zip, componentDir);
            Console.WriteLine("Installed or updated Tyrant's framework in the game.");
        }
        var cooker = ModTextureCooker.For(ws, ModCli.TryIndex(ws)?.Assets, ModCli.TrySpeciesFor(ws, install, mod));
        new GameMods(launcher.IsRunning).Install(install, mod, staging => cooker.WritePack(mod.Manifest, staging, staging,
            new SyncProgress(p => Console.WriteLine($"Cooking textures {p.Done}/{p.Total}..."))));
        Console.WriteLine($"Installed '{mod.Id}' into {Path.Combine(ModLoaderInstaller.ModsDir(install), mod.Id)}. Start the game to see it; undo with 'tyrant mod remove {mod.Id}'.");
        return ExitCodes.Ok;
    }
}

public sealed class ModRemoveCommand : Command<ModSettings>
{
    public override int Execute(CommandContext context, ModSettings settings)
    {
        var (_, install) = CliServices.OpenWorkspace(settings);
        new GameMods(CliServices.Launcher.IsRunning).Remove(install, settings.Id);
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
                Console.WriteLine($"  {id,-28} {mod.Manifest.Version,-8} {ModCli.Label(state)}{onOff}  {mod.Manifest.Replace.Count} replacement(s), {mod.Manifest.Skins.Count} skin(s)");
                foreach (var clash in inGame?.Clashes ?? []) Console.WriteLine($"      NOTE   {clash}");
            }
            catch (TyrantException ex)
            {
                Console.WriteLine($"  {id,-28} cannot be read: {ex.Message}");
            }
            installed.Remove(id);
        }
        foreach (var other in installed.Values)
        {
            Console.WriteLine($"  {other.Id,-28} {other.Version,-8} in the game only{(other.Enabled ? " (on)" : " (off)")}{(other.Error is null ? "" : "  " + other.Error)}");
            foreach (var clash in other.Clashes) Console.WriteLine($"      NOTE   {clash}");
        }
        return ExitCodes.Ok;
    }
}

public sealed class ModAddSkinCommand : Command<ModAddSkinCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<SPECIES>")]
        [Description("The species id as the game data names it, e.g. Carcharodontosaurus, or a custom species' tyrant.<mod>.<species>.")]
        public string Species { get; set; } = "";

        [CommandOption("--name <NAME>")]
        [Description("The skin's name in the Nursery.")]
        public string Name { get; set; } = "";

        [CommandOption("--base <SKIN>")]
        [Description("The vanilla skin to start from: its name or number (default: the first).")]
        public string? Base { get; set; }

        [CommandOption("--male")]
        public bool Male { get; set; }

        [CommandOption("--female")]
        public bool Female { get; set; }

        [CommandOption("--maps")]
        [Description("Also export the base normal, extra, pattern and fur maps to edit (the babies' colour map always comes).")]
        public bool Maps { get; set; }

        [CommandOption("--group <NAME>")]
        [Description("Put it in this group in the mod editor, e.g. \"Jurassic Park\" (Tyrant only).")]
        public string? Group { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Name)) throw new TyrantException(TyrantErrorCode.ModInvalid, "Give the skin a name with --name.");
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var species = SpeciesSkinsReader.Load(ws);
        var bothWhenNone = !settings.Male && !settings.Female;
        var entry = mod.AddSkin(ws, install, CliServices.LoadIndex(ws, install), new BundleAssetReader(), species, settings.Species, settings.Name,
            settings.Base, new SkinTemplateOptions(settings.Male || bothWhenNone, settings.Female || bothWhenNone, settings.Maps), settings.Group,
            OtherModSpecies.Load(ws, new GameMods().List(install), mod.Id).Species);
        Console.WriteLine($"Added skin '{entry.Name}' ({entry.Key(mod.Id)}) based on '{entry.Base}':");
        foreach (var (sex, files) in new[] { ("male", entry.Male), ("female", entry.Female) })
            if (files is not null)
                foreach (var (slot, file) in files) Console.WriteLine($"  {sex,-6} {slot,-8} {Path.Combine(mod.Dir, file.Replace('/', Path.DirectorySeparatorChar))}");
        Console.WriteLine($"Edit those PNGs, then 'tyrant mod check {mod.Id}' and 'tyrant mod install {mod.Id}'.");
        return ExitCodes.Ok;
    }
}

public sealed class ModCleanSkinsCommand : Command<ModCleanSkinsCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--forget <KEY>")]
        [Description("A skin key (mod id/skin id) to forget; repeat for more. Without it, the command lists what can be cleaned up.")]
        public string[] Forget { get; set; } = [];
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (_, install) = CliServices.OpenWorkspace(settings);
        var slots = new SkinSlots(CliServices.Launcher.IsRunning);
        if (settings.Forget.Length > 0)
        {
            Console.WriteLine($"Forgot {slots.Forget(install, settings.Forget)} skin number(s). Saved animals that wore them show their species' skin 0 until a new skin takes the number.");
            return ExitCodes.Ok;
        }
        var orphans = slots.Orphans(install);
        if (orphans.Count == 0) Console.WriteLine("No skin numbers to clean up: every added skin's mod is installed.");
        foreach (var orphan in orphans) Console.WriteLine($"  {orphan.Species,-24} #{orphan.Number,-3} {orphan.Key}");
        return ExitCodes.Ok;
    }
}
