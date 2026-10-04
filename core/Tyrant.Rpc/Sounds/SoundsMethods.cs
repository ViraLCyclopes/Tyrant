using Tyrant.Core.Data;
using Tyrant.Core.Sounds;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Sounds;

/// <summary>A game sound: its picker group and name, the species that use it, and its length and kind when the event list has it.</summary>
public sealed record SoundDto(string Event, string Name, string Group, IReadOnlyList<string> Species, int? LengthMs, bool? OneShot);

public sealed record SoundsForSpeciesParams(string Species);

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
    public IReadOnlyList<SoundDto> ForSpecies(SoundsForSpeciesParams p) => Catalog().ForSpecies(p.Species).Select(Dto).ToList();

    [RpcMethod("sounds.search")]
    public SoundsSearchResult Search(SoundsSearchParams p)
    {
        var catalog = Catalog();
        return new SoundsSearchResult(catalog.Search(p.Text, Math.Clamp(p.Limit, 1, 2000)).Select(Dto).ToList(), catalog.HasEventList);
    }

    internal static SoundDto Dto(SoundInfo s) => new(s.Event, s.Name, s.Group, s.Species, s.LengthMs, s.OneShot);

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
