using System.Text.Json;

namespace PK.Core.Dumping;

/// <summary>manifest.json written by the in-game dumper (last file of a dump).</summary>
public sealed record DumpManifestFile(int SchemaVersion, string RequestId, string BuildGuid, string DumperVersion, string CreatedUtc,
    Dictionary<string, int> Counts, List<string> Languages, List<string> Errors)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
