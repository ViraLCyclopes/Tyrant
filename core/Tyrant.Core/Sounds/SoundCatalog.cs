using System.Text;
using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Sounds;

/// <summary>
/// A game sound (an FMOD event): its picker group and name, the species that use it, and what the event list knows. PerAnimal:
/// an animal plays it (its audio databases, breathing, skin and hit sounds), so a species or skin can have its own; menu sounds
/// (Nursery, Paleopedia) and the rest can only be replaced for everyone.
/// </summary>
public sealed record SoundInfo(string Event, string Name, string Group, IReadOnlyList<string> Species, int? LengthMs, bool? OneShot, bool PerAnimal);

/// <summary>
/// The game's sounds from the data dump: each species' audio databases and event fields (AnimalData), and every event the
/// dumper listed from FMOD (data/audio/events.json, when the dump has it).
/// </summary>
public sealed class SoundCatalog
{
    private const string DatabaseType = "PrehistoricKingdom.AnimalAudioDatabase";
    private static readonly string[] AnimalTypes = ["PrehistoricKingdom.AnimalData", "PrehistoricKingdom.VivariumAnimalData"];
    private const string EventPrefix = "event:/";

    /// <summary>AnimalData fields the game plays from the animal itself (the others play in menus or without the animal).</summary>
    private static readonly string[] AnimalFields = ["breathingCore", "bodyCore", "animalHitEventAudio"];

    /// <summary>Folder word → group, checked from the deepest folder up; within a folder the first word that matches wins.</summary>
    private static readonly Dictionary<string, string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Vox"] = "Calls", ["Growls"] = "Growls", ["Footsteps"] = "Footsteps",
        ["Breath"] = "Breathing", ["Breathing"] = "Breathing", ["Core"] = "Breathing",
        ["Nourishment"] = "Eating & drinking", ["Food"] = "Eating & drinking", ["Drink"] = "Eating & drinking",
        ["Body"] = "Body", ["Combat"] = "Combat", ["User Interface"] = "Interface", ["Music"] = "Music", ["Ambience"] = "Ambience",
    };

    private readonly Dictionary<string, SoundInfo> _sounds;
    private readonly Dictionary<string, List<SoundInfo>> _bySpecies;

    private SoundCatalog(Dictionary<string, SoundInfo> sounds, Dictionary<string, List<SoundInfo>> bySpecies, bool hasEventList)
    {
        _sounds = sounds;
        _bySpecies = bySpecies;
        HasEventList = hasEventList;
    }

    /// <summary>True when the dump has the FMOD event list (a dump run with Tyrant 0.4 or later).</summary>
    public bool HasEventList { get; }

    public static SoundCatalog Load(Workspace ws)
    {
        try
        {
            return Read(ws.DataDir);
        }
        catch (TyrantException ex) when (ex.Code == TyrantErrorCode.DataMissing)
        {
            throw new TyrantException(TyrantErrorCode.DataMissing,
                "The sound lists need the game's data: on the Workspace tab click Run data dump (or run 'tyrant dump run'), then try again.",
                FixAction.RefreshWorkspace, ex);
        }
    }

    public static SoundCatalog Read(string dataDir)
    {
        var store = DataStore.OpenDirectory(dataDir);
        var types = store.Types();

        var databases = new Dictionary<long, List<string>>();
        var databasesByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var perAnimal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (types.FirstOrDefault(t => t.FullName == DatabaseType) is { } databaseType)
            foreach (var (name, root) in store.LoadAll(databaseType))
            {
                var events = DatabaseEvents(root);
                known.UnionWith(events);
                perAnimal.UnionWith(events);
                if (root.TryGetProperty("$id", out var id) && id.TryGetInt64(out var key)) databases[key] = events;
                databasesByName[name] = events;
            }

        var speciesEvents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types.Where(t => AnimalTypes.Contains(t.FullName)))
            foreach (var (_, root) in store.LoadAll(type))
            {
                if (!root.TryGetProperty("speciesID", out var idElement) || idElement.ValueKind != JsonValueKind.String
                    || idElement.GetString() is not { Length: > 0 } speciesId) continue;
                var events = new List<string>();
                if (root.TryGetProperty("AudioDatabases", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var reference in list.EnumerateArray())
                        if (Referenced(reference, databases, databasesByName) is { } databaseEvents) events.AddRange(databaseEvents);
                foreach (var field in root.EnumerateObject())
                    if (IsEvent(field.Value))
                    {
                        events.Add(field.Value.GetString()!);
                        if (AnimalFields.Contains(field.Name, StringComparer.OrdinalIgnoreCase)) perAnimal.Add(field.Value.GetString()!);
                    }
                var distinct = events.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                known.UnionWith(distinct);
                speciesEvents[speciesId] = distinct;
            }

        var sharers = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (species, events) in speciesEvents)
            foreach (var path in events)
            {
                if (!sharers.TryGetValue(path, out var set)) sharers[path] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(species);
            }

        var listed = ReadEventList(dataDir);
        if (listed != null) known.UnionWith(listed.Keys);

        var sounds = new Dictionary<string, SoundInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in known)
        {
            (int Length, bool OneShot)? details = listed != null && listed.TryGetValue(path, out var d) ? d : null;
            sounds[path] = new SoundInfo(path, NameOf(path), GroupOf(path),
                sharers.TryGetValue(path, out var set) ? set.ToList() : [], details?.Length, details?.OneShot, perAnimal.Contains(path));
        }
        var bySpecies = speciesEvents.ToDictionary(kv => kv.Key, kv => Sorted(kv.Value.Select(p => sounds[p])).ToList(), StringComparer.OrdinalIgnoreCase);
        return new SoundCatalog(sounds, bySpecies, listed != null);
    }

    /// <summary>The species' sounds (its audio databases and its own event fields), by group then name; empty when unknown.</summary>
    public IReadOnlyList<SoundInfo> ForSpecies(string speciesId) =>
        _bySpecies.TryGetValue(speciesId, out var sounds) ? sounds : [];

    /// <summary>The dump's species id for an id in any case or an asset key ("carcharodontosaurus"); null when unknown.</summary>
    public string? SpeciesIdFor(string idOrKey)
    {
        var key = Key(idOrKey);
        return _bySpecies.Keys.FirstOrDefault(id => string.Equals(id, idOrKey, StringComparison.OrdinalIgnoreCase))
            ?? _bySpecies.Keys.FirstOrDefault(id => Key(id) == key);
    }

    private static string Key(string id) => new(id.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    /// <summary>Every known sound whose path or name contains the text (all when empty), by group then name.</summary>
    public IReadOnlyList<SoundInfo> Search(string? text, int limit = 500)
    {
        var query = text?.Trim() ?? "";
        var matches = _sounds.Values.Where(s => query.Length == 0
            || s.Event.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        return Sorted(matches).Take(Math.Max(0, limit)).ToList();
    }

    public SoundInfo? Find(string eventPath) => _sounds.TryGetValue(eventPath, out var sound) ? sound : null;

    /// <summary>The picker group: the deepest folder with a known word (Vox → Calls, Footsteps, Breath/Core → Breathing, …), else Other.</summary>
    public static string GroupOf(string path)
    {
        var folders = Segments(path);
        for (var i = folders.Length - 2; i >= 0; i--)
        {
            var folder = WithoutNote(folders[i]);
            if (Groups.TryGetValue(folder, out var whole)) return whole;
            foreach (var word in folder.Split(['_', ' '], StringSplitOptions.RemoveEmptyEntries))
                if (Groups.TryGetValue(word, out var group)) return group;
        }
        return "Other";
    }

    /// <summary>
    /// A readable name from the last segment: the family prefix before the first '_' and a "Vox" prefix dropped, camel case and
    /// '_' split into lowercase words, first letter capitalised ("TheroLarge_VoxSocialCall" → "Social call").
    /// </summary>
    public static string NameOf(string path)
    {
        var segments = Segments(path);
        var name = segments.Length == 0 ? path : segments[^1];
        var underscore = name.IndexOf('_');
        if (underscore >= 0 && underscore < name.Length - 1) name = name[(underscore + 1)..];
        if (name.Length > 3 && name.StartsWith("Vox", StringComparison.Ordinal) && (char.IsUpper(name[3]) || name[3] == '_')) name = name[3..];
        var words = Words(name);
        if (words.Count == 0) return name;
        var text = string.Join(" ", words).ToLowerInvariant();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string[] Segments(string path) =>
        (path.StartsWith(EventPrefix, StringComparison.OrdinalIgnoreCase) ? path[EventPrefix.Length..] : path)
        .Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>"Body (-28 LUFS)" → "Body".</summary>
    private static string WithoutNote(string folder)
    {
        var open = folder.IndexOf('(');
        return (open >= 0 ? folder[..open] : folder).Trim();
    }

    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '_' or ' ' or '-')
            {
                Flush();
                continue;
            }
            if (char.IsUpper(c) && current.Length > 0)
            {
                var previous = current[^1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower)) Flush();
            }
            current.Append(c);
        }
        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0) words.Add(current.ToString());
            current.Clear();
        }
    }

    private static IEnumerable<SoundInfo> Sorted(IEnumerable<SoundInfo> sounds) =>
        sounds.OrderBy(s => s.Group, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Event, StringComparer.OrdinalIgnoreCase);

    private static bool IsEvent(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length > EventPrefix.Length
        && text.StartsWith(EventPrefix, StringComparison.OrdinalIgnoreCase);

    private static List<string> DatabaseEvents(JsonElement database)
    {
        var events = new List<string>();
        if (!database.TryGetProperty("AudioEventContainers", out var containers) || containers.ValueKind != JsonValueKind.Array) return events;
        foreach (var container in containers.EnumerateArray())
        {
            if (container.ValueKind != JsonValueKind.Object || !container.TryGetProperty("AudioEvents", out var list)
                || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in list.EnumerateArray())
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("AudioEvent", out var path) && IsEvent(path))
                    events.Add(path.GetString()!);
        }
        return events.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string>? Referenced(JsonElement reference, Dictionary<long, List<string>> byId, Dictionary<string, List<string>> byName)
    {
        if (reference.ValueKind != JsonValueKind.Object || !reference.TryGetProperty("$ref", out var target) || target.ValueKind != JsonValueKind.Object)
            return null;
        if (target.TryGetProperty("id", out var id) && id.TryGetInt64(out var key) && byId.TryGetValue(key, out var events)) return events;
        return target.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            && byName.TryGetValue(name.GetString()!, out var named) ? named : null;
    }

    /// <summary>data/audio/events.json by path; null when it is missing or damaged (the lists then come from the databases).</summary>
    private static Dictionary<string, (int Length, bool OneShot)>? ReadEventList(string dataDir)
    {
        var file = Path.Combine(dataDir, "audio", "events.json") /* the dumper's AudioEventList */;
        if (!File.Exists(file)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            if (!document.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array) return null;
            var result = new Dictionary<string, (int, bool)>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in events.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("path", out var path) || !IsEvent(path)) continue;
                var length = e.TryGetProperty("lengthMs", out var l) && l.TryGetInt32(out var ms) ? ms : 0;
                var oneShot = e.TryGetProperty("oneShot", out var o) && o.ValueKind == JsonValueKind.True;
                result[path.GetString()!] = (length, oneShot);
            }
            return result;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
