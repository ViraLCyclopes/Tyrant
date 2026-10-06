using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Sounds;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Sounds;

/// <summary>A game sound: its picker group and name, the species that use it, and its length and kind when the event list has it.</summary>
/// <summary>PerAnimal false: menu sounds and the like, which only a replacement for everyone changes.</summary>
public sealed record SoundDto(string Event, string Name, string Group, IReadOnlyList<string> Species, int? LengthMs, bool? OneShot, bool PerAnimal);

public sealed record SoundsForSpeciesParams(string Species);

/// <summary>SpeciesId: the dump's id for the species asked for (by id or asset key); null and no sounds when the dump does not have it.</summary>
public sealed record SoundsForSpeciesResult(string? SpeciesId, IReadOnlyList<SoundDto> Sounds, bool HasEventList);

/// <summary>Species: match against that species' own sounds (the ones a species can have its own of); none = every sound.</summary>
public sealed record SoundsMatchFolderParams(string Folder, string? Species = null);

/// <summary>Files sharing a name (takes) and the game sounds that name fits: one, or several to pick from.</summary>
public sealed record SoundFolderGroupDto(string Name, IReadOnlyList<string> Files, IReadOnlyList<SoundDto> Sounds);

/// <summary>Unmatched: files that fit no game sound, or are not audio.</summary>
public sealed record SoundsMatchFolderResult(IReadOnlyList<SoundFolderGroupDto> Groups, IReadOnlyList<string> Unmatched);

public sealed record SoundsSearchParams(string? Text = null, int Limit = 500);

/// <summary>HasEventList false: the dump predates the FMOD event list, so only the species' sounds are known (run the dump again).</summary>
public sealed record SoundsSearchResult(IReadOnlyList<SoundDto> Sounds, bool HasEventList);

/// <summary>sounds.* — the game's sounds from the data dump, for the sound pickers.</summary>
public sealed class SoundsMethods
{
    private readonly StudioSession _session;
    private readonly object _lock = new();
    private SoundCatalog? _catalog;
    private string _key = "";

    public SoundsMethods(StudioSession session)
    {
        _session = session;
        session.DataChanged += () => { lock (_lock) _catalog = null; };
    }

    [RpcMethod("sounds.forSpecies")]
    public SoundsForSpeciesResult ForSpecies(SoundsForSpeciesParams p)
    {
        var catalog = Catalog();
        var id = catalog.SpeciesIdFor(p.Species);
        return new SoundsForSpeciesResult(id, id is null ? [] : catalog.ForSpecies(id).Select(Dto).ToList(), catalog.HasEventList);
    }

    [RpcMethod("sounds.search")]
    public SoundsSearchResult Search(SoundsSearchParams p)
    {
        var catalog = Catalog();
        return new SoundsSearchResult(catalog.Search(p.Text, Math.Clamp(p.Limit, 1, 2000)).Select(Dto).ToList(), catalog.HasEventList);
    }

    /// <summary>A sound pack's folder matched to the game's sounds by file name, for Replace from folder.</summary>
    [RpcMethod("sounds.matchFolder")]
    public SoundsMatchFolderResult MatchFolder(SoundsMatchFolderParams p)
    {
        if (string.IsNullOrWhiteSpace(p.Folder) || !Directory.Exists(p.Folder))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"The folder '{p.Folder}' does not exist.");
        var catalog = Catalog();
        IReadOnlyList<SoundInfo> sounds;
        if (string.IsNullOrWhiteSpace(p.Species)) sounds = catalog.Search(null, int.MaxValue);
        else
        {
            var id = catalog.SpeciesIdFor(p.Species)
                ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{p.Species}' has no sounds in the game data.");
            sounds = catalog.ForSpecies(id).Where(s => s.PerAnimal).ToList();
        }
        var byEvent = sounds.GroupBy(s => s.Event, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var match = SoundFolderMatcher.Match(Directory.GetFiles(p.Folder), sounds);
        return new SoundsMatchFolderResult(
            match.Groups.Select(g => new SoundFolderGroupDto(g.Name, g.Files, g.Events.Select(e => Dto(byEvent[e])).ToList())).ToList(),
            match.Unmatched);
    }

    internal static SoundDto Dto(SoundInfo s) => new(s.Event, s.Name, s.Group, s.Species, s.LengthMs, s.OneShot, s.PerAnimal);

    /// <summary>Built once per workspace and dump (reading every species' data takes a moment).</summary>
    private SoundCatalog Catalog()
    {
        var (ws, _) = _session.Current();
        var key = $"{ws.Dir}|{DumpStamp(ws)}";
        lock (_lock)
        {
            if (_catalog is not null && _key == key) return _catalog;
            _catalog = SoundCatalog.Load(ws);
            _key = key;
            return _catalog;
        }
    }

    private static string DumpStamp(Workspace ws)
    {
        var manifest = Path.Combine(ws.DataDir, "manifest.json");
        var events = Path.Combine(ws.DataDir, "audio", "events.json");
        return $"{(File.Exists(manifest) ? File.GetLastWriteTimeUtc(manifest).Ticks : 0)}|{(File.Exists(events) ? File.GetLastWriteTimeUtc(events).Ticks : 0)}";
    }
}
