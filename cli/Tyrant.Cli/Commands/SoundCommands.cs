using System.ComponentModel;
using System.Globalization;
using Spectre.Console.Cli;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Core.Sounds;
using Tyrant.Framework.Core;

namespace Tyrant.Cli.Commands;

// Sound replacements: the same operations as the app's Sounds lists (species panel, All sounds) and the mod editor's Sounds section.

public sealed class SoundsListCommand : Command<SoundsListCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--species <ID>")]
        [Description("Only this species' sounds (its own and the ones it shares), e.g. Carcharodontosaurus.")]
        public string? Species { get; set; }

        [CommandOption("--search <TEXT>")]
        [Description("Only sounds whose name or path contains this text.")]
        public string? Search { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var catalog = SoundCatalog.Load(ws);
        var sounds = settings.Species is { } species
            ? catalog.ForSpecies(species).Where(s => settings.Search is null
                || s.Event.Contains(settings.Search, StringComparison.OrdinalIgnoreCase) || s.Name.Contains(settings.Search, StringComparison.OrdinalIgnoreCase)).ToList()
            : catalog.Search(settings.Search, int.MaxValue);
        foreach (var group in sounds.GroupBy(s => s.Group))
        {
            Console.WriteLine(group.Key);
            foreach (var s in group)
            {
                var others = s.Species.Where(x => !string.Equals(x, settings.Species, StringComparison.OrdinalIgnoreCase)).ToList();
                var shared = others.Count == 0 ? "" : $"  (also {string.Join(", ", others.Take(4))}{(others.Count > 4 ? $" and {others.Count - 4} more" : "")})";
                var length = s.LengthMs is { } ms && ms > 0 ? $"  {ms / 1000.0:0.0}s" : "";
                Console.WriteLine($"  {s.Name,-28} {s.Event}{length}{shared}");
            }
        }
        Console.WriteLine($"{sounds.Count} sound(s).");
        if (!catalog.HasEventList && settings.Species is null)
            Console.WriteLine("Only the species' sounds are listed: run 'tyrant dump run' once more (or Run data dump on the Workspace tab) to list every game sound.");
        return ExitCodes.Ok;
    }
}

public class ModSoundSettings : ModSettings
{
    [CommandArgument(1, "<EVENT>")]
    [Description("The game sound, e.g. event:/AnimalFamily Master/…/TheroLarge_VoxBroadcast (see 'tyrant sounds list').")]
    public string Event { get; set; } = "";

    [CommandOption("--species <ID>")]
    [Description("The replacement only this species hears (with set-sound and remove-sound: the one to change).")]
    public string? Species { get; set; }

    [CommandOption("--skin <KEY>")]
    [Description("The replacement only this skin hears, e.g. my-mod/scarred or Carcharodontosaurus/Alt 1.")]
    public string? Skin { get; set; }
}

public sealed class ModReplaceSoundCommand : Command<ModReplaceSoundCommand.Settings>
{
    public sealed class Settings : ModSoundSettings
    {
        [CommandArgument(2, "<FILES>")]
        [Description("One or more audio files (WAV, OGG, MP3 or FLAC); with several, one is picked at random each time.")]
        public string[] Files { get; set; } = [];

        [CommandOption("--volume <0-2>")]
        [Description("1 plays the file as it is.")]
        public double? Volume { get; set; }

        [CommandOption("--age-pitch <0-1>")]
        [Description("How much higher babies sound (1, the default, follows the animal's age; 0 never changes).")]
        public double? AgePitch { get; set; }

        [CommandOption("--chance <0-1>")]
        [Description("How often a one-off sound plays when the game starts it (0.3 = about one time in three). Without it, it follows the game's own chances.")]
        public double? Chance { get; set; }

        [CommandOption("--like-game")]
        [Description("Drop an earlier own chance: it plays when the game's own sound does.")]
        public bool LikeGame { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var entry = mod.ReplaceSound(settings.Event, settings.Files.Select(Path.GetFullPath).ToList(), settings.Species, settings.Skin);
        if (settings.Volume is not null || settings.AgePitch is not null || settings.Chance is not null || settings.LikeGame)
            mod.SetSound(entry.Event, entry.Species, entry.Skin, settings.Volume, settings.AgePitch, null, null, null, settings.Chance, settings.LikeGame);
        Console.WriteLine($"'{settings.Id}' replaces {SoundCatalog.NameOf(entry.Event).ToLowerInvariant()} {ModProject.ScopeText(entry.Species, entry.Skin)}: {string.Join(", ", entry.Files)}.");
        return ExitCodes.Ok;
    }
}

/// <summary>A sound pack's folder: its files matched to the game's sounds by name (takes grouped), all replaced at once.</summary>
public sealed class ModReplaceSoundsCommand : Command<ModReplaceSoundsCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<FOLDER>")]
        [Description("A folder of audio files named like the game's sounds, e.g. AlloAnax_VoxAngry_01.wav (a prefix and a take number are fine).")]
        public string Folder { get; set; } = "";

        [CommandOption("--species <ID>")]
        [Description("Match that species' own sounds; only it hears the replacements.")]
        public string? Species { get; set; }

        [CommandOption("--for-everyone")]
        [Description("Match every game sound; everyone hears the replacements.")]
        public bool ForEveryone { get; set; }

        [CommandOption("--dry-run")]
        [Description("Show what matches; change nothing.")]
        public bool DryRun { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Species) == !settings.ForEveryone)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Choose one: --species <ID> (only that species hears them) or --for-everyone.");
        var folder = Path.GetFullPath(settings.Folder);
        if (!Directory.Exists(folder)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"The folder '{folder}' does not exist.");
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var catalog = SoundCatalog.Load(ws);
        IReadOnlyList<SoundInfo> sounds = catalog.Search(null, int.MaxValue);
        string? species = null;
        if (!settings.ForEveryone)
        {
            species = catalog.SpeciesIdFor(settings.Species!)
                ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{settings.Species}' has no sounds in the game data (see 'tyrant sounds list').");
            sounds = catalog.ForSpecies(species).Where(s => s.PerAnimal).ToList();
        }
        var match = SoundFolderMatcher.Match(Directory.GetFiles(folder), sounds);
        var chosen = new List<SoundFiles>();
        foreach (var group in match.Groups)
        {
            var names = string.Join(" or ", group.Events.Select(SoundCatalog.NameOf));
            Console.WriteLine($"  {group.Name} ({group.Files.Count} file{(group.Files.Count == 1 ? "" : "s")}) -> {names}");
            if (group.Events.Count == 1) chosen.Add(new SoundFiles(group.Events[0], group.Files));
            else Console.WriteLine($"    fits several sounds; replace it with 'tyrant mod replace-sound' and the one you mean: {string.Join(", ", group.Events)}");
        }
        if (match.Unmatched.Count > 0) Console.WriteLine($"Not matched: {string.Join(", ", match.Unmatched.Select(Path.GetFileName))}");
        if (chosen.Count == 0) throw new TyrantException(TyrantErrorCode.ModInvalid, "No file matched one game sound; nothing was replaced.");
        if (settings.DryRun)
        {
            Console.WriteLine($"{chosen.Count} sound(s) would be replaced {ModProject.ScopeText(species, null)} (dry run: nothing changed).");
            return ExitCodes.Ok;
        }
        ModProject.Open(ws, settings.Id).ReplaceSounds(chosen, species, null);
        Console.WriteLine($"'{settings.Id}' replaces {chosen.Count} sound(s) {ModProject.ScopeText(species, null)}.");
        return ExitCodes.Ok;
    }
}

public sealed class ModSetSoundCommand : Command<ModSetSoundCommand.Settings>
{
    public sealed class Settings : ModSoundSettings
    {
        [CommandOption("--volume <0-2>")]
        [Description("1 plays the file as it is.")]
        public double? Volume { get; set; }

        [CommandOption("--age-pitch <0-1>")]
        [Description("How much higher babies sound (0 never changes).")]
        public double? AgePitch { get; set; }

        [CommandOption("--to-species <ID>")]
        [Description("Move the replacement: only this species hears it.")]
        public string? ToSpecies { get; set; }

        [CommandOption("--to-skin <KEY>")]
        [Description("Move the replacement: only this skin hears it.")]
        public string? ToSkin { get; set; }

        [CommandOption("--chance <0-1>")]
        [Description("Its own chance for a one-off sound (0.3 = about one time in three).")]
        public double? Chance { get; set; }

        [CommandOption("--like-game")]
        [Description("Drop its own chance: it plays when the game's own sound does.")]
        public bool LikeGame { get; set; }

        [CommandOption("--for-everyone")]
        [Description("Move the replacement: every animal (or the whole game) hears it.")]
        public bool ForEveryone { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        mod.SetSound(settings.Event, settings.Species, settings.Skin, settings.Volume, settings.AgePitch, settings.ToSpecies, settings.ToSkin,
            settings.ForEveryone ? true : null, settings.Chance, settings.LikeGame);
        Console.WriteLine($"Changed '{settings.Id}''s replacement of {SoundCatalog.NameOf(settings.Event).ToLowerInvariant()}.");
        return ExitCodes.Ok;
    }
}

public sealed class ModRemoveSoundCommand : Command<ModSoundSettings>
{
    public override int Execute(CommandContext context, ModSoundSettings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        ModProject.Open(ws, settings.Id).RemoveSound(settings.Event, settings.Species, settings.Skin);
        Console.WriteLine($"'{settings.Id}' no longer replaces {settings.Event} {ModProject.ScopeText(settings.Species, settings.Skin)} (its files stay in sounds/).");
        return ExitCodes.Ok;
    }
}

internal static class SoundCli
{
    /// <summary>One line for 'tyrant mod show'.</summary>
    public static string Line(SoundReplacement s) =>
        $"  {SoundCatalog.NameOf(s.Event),-24} {ModProject.ScopeText(s.Species, s.Skin)}  {s.Files.Count} file(s)  volume {s.Volume.ToString("0.##", CultureInfo.InvariantCulture)}"
        + $"  age pitch {s.AgePitch.ToString("0.##", CultureInfo.InvariantCulture)}"
        + $"  {(s.Chance is double chance ? $"chance {Math.Round(chance * 100).ToString(CultureInfo.InvariantCulture)}%" : "like the game")}  {s.Event}";
}
