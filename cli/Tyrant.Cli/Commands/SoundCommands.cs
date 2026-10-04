using System.ComponentModel;
using System.Globalization;
using Spectre.Console.Cli;
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
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var entry = mod.ReplaceSound(settings.Event, settings.Files.Select(Path.GetFullPath).ToList(), settings.Species, settings.Skin);
        if (settings.Volume is not null || settings.AgePitch is not null)
            mod.SetSound(entry.Event, entry.Species, entry.Skin, settings.Volume, settings.AgePitch, null, null, null);
        Console.WriteLine($"'{settings.Id}' replaces {SoundCatalog.NameOf(entry.Event).ToLowerInvariant()} {ModProject.ScopeText(entry.Species, entry.Skin)}: {string.Join(", ", entry.Files)}.");
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

        [CommandOption("--for-everyone")]
        [Description("Move the replacement: every animal (or the whole game) hears it.")]
        public bool ForEveryone { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        mod.SetSound(settings.Event, settings.Species, settings.Skin, settings.Volume, settings.AgePitch, settings.ToSpecies, settings.ToSkin,
            settings.ForEveryone ? true : null);
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
        + $"  age pitch {s.AgePitch.ToString("0.##", CultureInfo.InvariantCulture)}  {s.Event}";
}
