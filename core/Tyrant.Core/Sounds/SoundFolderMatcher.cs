using System.Text.RegularExpressions;

namespace Tyrant.Core.Sounds;

/// <summary>Files that share a name (apart from a take number) and the game sounds that name fits (one, several, or none).</summary>
public sealed record SoundFolderGroup(string Name, IReadOnlyList<string> Files, IReadOnlyList<string> Events);

/// <summary>Groups that fit at least one game sound, and the files that fit none (or are not audio).</summary>
public sealed record SoundFolderMatch(IReadOnlyList<SoundFolderGroup> Groups, IReadOnlyList<string> Unmatched);

/// <summary>
/// A folder of sounds matched to the game's sounds by name, as sound packs name them: "AlloAnax_VoxAngry_03.wav" is take 3
/// of the game's TheroMed_VoxAngry (each name's own prefix, a take number, case, spaces and underscores are left out).
/// </summary>
public static partial class SoundFolderMatcher
{
    private static readonly string[] AudioExtensions = [".wav", ".ogg", ".mp3", ".flac"];

    public static SoundFolderMatch Match(IReadOnlyList<string> files, IReadOnlyList<SoundInfo> sounds)
    {
        var groups = new List<SoundFolderGroup>();
        var unmatched = new List<string>();
        var sorted = files.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
        unmatched.AddRange(sorted.Where(f => !AudioExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)));
        foreach (var group in sorted.Except(unmatched).GroupBy(f => WithoutTake(Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase))
        {
            var events = EventsFor(group.Key, sounds);
            if (events.Count == 0) unmatched.AddRange(group);
            else groups.Add(new SoundFolderGroup(group.Key, group.ToList(), events));
        }
        return new SoundFolderMatch(groups, unmatched.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>The game sounds a name fits: its whole name first; else either name without its prefix.</summary>
    private static List<string> EventsFor(string name, IReadOnlyList<SoundInfo> sounds)
    {
        var full = Key(name);
        var bare = Key(WithoutPrefix(name));
        var exact = sounds.Where(s => Key(Last(s.Event)) == full).Select(s => s.Event).Distinct().ToList();
        if (exact.Count > 0) return exact;
        return sounds.Where(s =>
        {
            var gameBare = Key(WithoutPrefix(Last(s.Event)));
            return gameBare == full || gameBare == bare || Key(Last(s.Event)) == bare;
        }).Select(s => s.Event).Distinct().ToList();
    }

    /// <summary>"AlloAnax_VoxAngry_03" → "AlloAnax_VoxAngry" (a take number at the end, with or without _ - or a space).</summary>
    private static string WithoutTake(string name)
    {
        var m = Take().Match(name);
        return m.Success && m.Groups[1].Length > 0 ? m.Groups[1].Value : name;
    }

    /// <summary>"AlloAnax_VoxAngry" → "VoxAngry"; a name without a prefix stays as it is.</summary>
    private static string WithoutPrefix(string name)
    {
        var i = name.IndexOf('_');
        return i > 0 && i < name.Length - 1 ? name[(i + 1)..] : name;
    }

    private static string Last(string eventPath) => eventPath[(eventPath.LastIndexOf('/') + 1)..];

    private static string Key(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [GeneratedRegex(@"^(.+?)[ _\-]?\d+$")]
    private static partial Regex Take();
}
