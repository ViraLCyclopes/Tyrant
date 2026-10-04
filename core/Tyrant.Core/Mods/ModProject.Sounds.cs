using System.Security.Cryptography;
using System.Text;
using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Sound replacements: the game's sound for everyone, or only for one species or one skin (mod.json "sounds").</summary>
public sealed partial class ModProject
{
    public const string SoundsFolder = "sounds";

    /// <summary>
    /// Replaces a game sound with one or more audio files (several = one picked at random each time). The files are copied into
    /// sounds/ under content-hashed names (files already in the mod keep theirs, so the editor adds and removes files by sending the
    /// new list); an entry for the same sound and scope is replaced, keeping its volume and age pitch.
    /// </summary>
    public SoundReplacement ReplaceSound(string eventPath, IReadOnlyList<string> sourceFiles, string? species, string? skin)
    {
        var path = EventPathOf(eventPath);
        (species, skin) = ScopeOf(species, skin);
        if (sourceFiles.Count == 0) throw new TyrantException(TyrantErrorCode.ModInvalid, "Pick at least one audio file (WAV, OGG, MP3 or FLAC).");

        var formats = new List<string>();
        foreach (var source in sourceFiles)
        {
            if (!File.Exists(source)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{source}' does not exist.");
            formats.Add(AudioFormat.Sniff(Head(source))
                ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{source}' is not an audio file (WAV, OGG, MP3 or FLAC)."));
        }

        Directory.CreateDirectory(Path.Combine(Dir, SoundsFolder));
        var files = new List<string>();
        for (var i = 0; i < sourceFiles.Count; i++)
        {
            var source = sourceFiles[i];
            if (ModPaths.IsInside(Path.GetFullPath(source), Dir))
            {
                var inside = Path.GetRelativePath(Dir, Path.GetFullPath(source)).Replace(Path.DirectorySeparatorChar, '/');
                if (!files.Contains(inside, StringComparer.OrdinalIgnoreCase)) files.Add(inside);
                continue;
            }
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))[..8].ToLowerInvariant();
            var file = $"{SoundsFolder}/{Slug(Path.GetFileNameWithoutExtension(source))}-{hash}.{formats[i]}";
            var destination = Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(destination)) File.Copy(source, destination);
            if (!files.Contains(file, StringComparer.OrdinalIgnoreCase)) files.Add(file);
        }

        var old = FindSound(path, species, skin);
        var entry = new SoundReplacement { Event = path, Species = species, Skin = skin, Volume = old?.Volume ?? 1.0, AgePitch = old?.AgePitch ?? 1.0 };
        entry.Files.AddRange(files);
        var at = old is null ? Manifest.Sounds.Count : Manifest.Sounds.IndexOf(old);
        if (old is not null) Manifest.Sounds.Remove(old);
        Manifest.Sounds.Insert(at, entry);
        Save();
        return entry;
    }

    /// <summary>Removes a sound replacement; its files stay in sounds/ (the editor's undo puts the entry back).</summary>
    public void RemoveSound(string eventPath, string? species, string? skin)
    {
        var entry = Sound(eventPath, species, skin);
        Manifest.Sounds.Remove(entry);
        Save();
    }

    /// <summary>
    /// Changes a sound replacement's volume (0–2), age pitch (0–1) or who hears it: <paramref name="forEveryone"/>, another
    /// species or another skin moves the entry there (refused when that scope already replaces the sound).
    /// </summary>
    public void SetSound(string eventPath, string? species, string? skin, double? volume, double? agePitch, string? newSpecies, string? newSkin, bool? forEveryone)
    {
        var entry = Sound(eventPath, species, skin);
        if (volume is { } v && (double.IsNaN(v) || v < 0 || v > 2))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Volume goes from 0 to 2 (1 is the file as it is).");
        if (agePitch is { } a && (double.IsNaN(a) || a < 0 || a > 1))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Age pitch goes from 0 (babies sound like adults) to 1 (babies play higher).");

        var moving = forEveryone == true || !string.IsNullOrWhiteSpace(newSpecies) || !string.IsNullOrWhiteSpace(newSkin);
        string? targetSpecies = entry.Species, targetSkin = entry.Skin;
        if (moving)
        {
            if (forEveryone == true && (!string.IsNullOrWhiteSpace(newSpecies) || !string.IsNullOrWhiteSpace(newSkin)))
                throw new TyrantException(TyrantErrorCode.ModInvalid, "Choose one: for everyone, one species, or one skin.");
            (targetSpecies, targetSkin) = forEveryone == true ? (null, null) : ScopeOf(newSpecies, newSkin);
            if (FindSound(entry.Event, targetSpecies, targetSkin) is { } taken && taken != entry)
                throw new TyrantException(TyrantErrorCode.ModInvalid,
                    $"'{Id}' already replaces {SoundCatalogName(entry.Event)} {ScopeText(targetSpecies, targetSkin)}; remove that one first.");
        }

        if (volume is { } newVolume) entry.Volume = newVolume;
        if (agePitch is { } newAgePitch) entry.AgePitch = newAgePitch;
        entry.Species = targetSpecies;
        entry.Skin = targetSkin;
        Save();
    }

    /// <summary>"for everyone", "for Carcharodontosaurus" or "for skin carch-voice/scarred".</summary>
    public static string ScopeText(string? species, string? skin) =>
        skin is not null ? $"for skin {skin}" : species is not null ? $"for {species}" : "for everyone";

    public SoundReplacement Sound(string eventPath, string? species, string? skin) =>
        FindSound(eventPath, Blank(species), Blank(skin))
        ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{Id}' does not replace {eventPath} {ScopeText(Blank(species), Blank(skin))}.");

    private SoundReplacement? FindSound(string eventPath, string? species, string? skin) =>
        Manifest.Sounds.FirstOrDefault(s => string.Equals(s.Event, eventPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Species, species, StringComparison.Ordinal) && string.Equals(s.Skin, skin, StringComparison.Ordinal));

    private static string EventPathOf(string eventPath)
    {
        var path = eventPath?.Trim() ?? "";
        if (!path.StartsWith("event:/", StringComparison.OrdinalIgnoreCase) || path.Length <= "event:/".Length)
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{eventPath}' is not a game sound; sounds look like event:/Folder/Name (copy one from a species' Sounds list).");
        return path;
    }

    private static (string? Species, string? Skin) ScopeOf(string? species, string? skin)
    {
        species = Blank(species);
        skin = Blank(skin);
        if (species is not null && skin is not null)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "A sound replacement is for one species or one skin, not both (a skin already belongs to its species).");
        return (species, skin);
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string SoundCatalogName(string eventPath) => Sounds.SoundCatalog.NameOf(eventPath).ToLowerInvariant();

    private static byte[] Head(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[16];
        var read = stream.Read(head, 0, head.Length);
        return head[..read];
    }

    /// <summary>"Big Roar!" → "big-roar".</summary>
    private static string Slug(string name)
    {
        var slug = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        var text = slug.ToString().Trim('-');
        return text.Length == 0 ? "sound" : text.Length > 40 ? text[..40].TrimEnd('-') : text;
    }
}
