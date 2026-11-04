using System.Text.Json;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Dumping;

/// <summary>manifest.json written by the in-game dumper (last file of a dump).</summary>
public sealed record DumpManifestFile(int SchemaVersion, string RequestId, string BuildGuid, string DumperVersion, string CreatedUtc,
    Dictionary<string, int> Counts, List<string> Languages, List<string> Errors)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Version FirstGameOnlyDumper = new(1, 1, 0);

    /// <summary>
    /// True for a dump made before the framework stayed out of dumps, which lists the skins and species of whatever mods were
    /// installed as the game's. A new Tyrant with an old dumper still in the game makes one too, so this goes by the dumper.
    /// </summary>
    public bool MayIncludeMods => !Version.TryParse(DumperVersion, out var made) || made < FirstGameOnlyDumper;

    /// <summary>Whether the workspace's dump may list mods as the game; false without one (Check already says it is missing).</summary>
    public static bool MayIncludeModsIn(Workspace ws) => TryRead(Path.Combine(ws.DataDir, "manifest.json"))?.MayIncludeMods == true;

    /// <summary>Null while the file is missing, partially written or unreadable.</summary>
    public static DumpManifestFile? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var manifest = JsonSerializer.Deserialize<DumpManifestFile>(stream, Json);
            return manifest is null ? null : manifest with
            {
                Counts = manifest.Counts ?? [], Languages = manifest.Languages ?? [], Errors = manifest.Errors ?? [],
                RequestId = manifest.RequestId ?? "",
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
