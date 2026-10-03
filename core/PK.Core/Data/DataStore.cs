using System.Text;
using System.Text.Json;
using PK.Core.Dumping;
using PK.Core.Errors;
using PK.Core.Workspaces;

namespace PK.Core.Data;

public sealed record DataType(string FullName, string ShortName, int Count);

/// <summary>Read-only access to a dump in &lt;workspace&gt;/data.</summary>
public sealed class DataStore
{
    private const int MaxFlattenDepth = 4;
    private readonly string _dir;

    private DataStore(string dir, DumpManifestFile manifest)
    {
        _dir = dir;
        Manifest = manifest;
    }

    public DumpManifestFile Manifest { get; }

    public static DataStore Open(Workspace ws) => OpenDirectory(ws.DataDir);

    public static DataStore OpenDirectory(string dataDir)
    {
        var manifest = DumpManifestFile.TryRead(Path.Combine(dataDir, "manifest.json"))
            ?? throw new PkException(PkErrorCode.DataMissing, "No game data yet. Run 'pk dump install' once, then 'pk dump run'.", FixAction.RefreshWorkspace);
        return new DataStore(dataDir, manifest);
    }

    public IReadOnlyList<DataType> Types()
    {
        var objects = Path.Combine(_dir, "objects");
        if (!Directory.Exists(objects)) return [];
        return Directory.GetDirectories(objects)
            .Select(d => Path.GetFileName(d))
            .Select(name => new DataType(name, name[(name.LastIndexOf('.') + 1)..], Directory.GetFiles(Path.Combine(objects, name), "*.json").Length))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    public DataType FindType(string query)
    {
        var types = Types();
        var exact = types.FirstOrDefault(t => string.Equals(t.FullName, query, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var byShort = types.Where(t => string.Equals(t.ShortName, query, StringComparison.OrdinalIgnoreCase)).ToList();
        return byShort.Count switch
        {
            1 => byShort[0],
            0 => throw new PkException(PkErrorCode.AssetNotFound, $"No data type '{query}'. Use 'pk data types' to list them."),
            _ => throw new PkException(PkErrorCode.AssetAmbiguous, $"'{query}' matches several types: {string.Join(", ", byShort.Select(t => t.FullName))}."),
        };
    }

    public IReadOnlyList<string> ObjectNames(DataType type) =>
        Directory.GetFiles(TypeDir(type), "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();

    public JsonElement Load(DataType type, string name)
    {
        var file = Directory.GetFiles(TypeDir(type), "*.json")
            .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new PkException(PkErrorCode.AssetNotFound, $"No {type.ShortName} named '{name}'. Use 'pk data show {type.ShortName}' to list them.");
        return Parse(file);
    }

    public IEnumerable<(string Name, JsonElement Root)> LoadAll(DataType type) =>
        Directory.GetFiles(TypeDir(type), "*.json").Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => (Path.GetFileNameWithoutExtension(f), Parse(f)));

    /// <summary>One row per object; nested objects become dotted columns, arrays compact JSON, references the target name.</summary>
    /// <summary>Indented JSON that keeps non-ASCII text (é, 日本) and markup readable instead of escape sequences; for files and display only.</summary>
    public static readonly JsonSerializerOptions ReadableJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions CompactReadableJson = new() { Encoder = ReadableJson.Encoder };

    /// <summary>UTF-8 with a byte-order mark, so Excel opens exported CSV files with the right characters.</summary>
    public static readonly System.Text.Encoding CsvEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string ToCsv(IEnumerable<(string Name, JsonElement Root)> objects)
    {
        var columns = new List<string> { "$name" };
        var rows = new List<Dictionary<string, string>>();
        foreach (var (name, root) in objects)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal) { ["$name"] = name };
            if (root.ValueKind == JsonValueKind.Object)
                foreach (var property in root.EnumerateObject())
                    if (property.Name is not ("$type" or "$name" or "$id"))
                        Flatten(property.Name, property.Value, row, 1);
            foreach (var key in row.Keys)
                if (!columns.Contains(key)) columns.Add(key);
            rows.Add(row);
        }

        var sb = new StringBuilder();
        sb.Append(string.Join(",", columns.Select(Escape))).Append("\r\n");
        foreach (var row in rows)
            sb.Append(string.Join(",", columns.Select(c => Escape(row.TryGetValue(c, out var v) ? v : "")))).Append("\r\n");
        return sb.ToString();
    }

    private static void Flatten(string prefix, JsonElement value, Dictionary<string, string> row, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object when value.TryGetProperty("$ref", out var reference):
                row[prefix] = reference.TryGetProperty("name", out var refName) ? refName.GetString() ?? "" : "";
                break;
            case JsonValueKind.Object when depth < MaxFlattenDepth:
                foreach (var property in value.EnumerateObject())
                    if (property.Name is not ("$type" or "$id"))
                        Flatten($"{prefix}.{property.Name}", property.Value, row, depth + 1);
                break;
            case JsonValueKind.Object:
            case JsonValueKind.Array:
                row[prefix] = JsonSerializer.Serialize(value, CompactReadableJson);
                break;
            case JsonValueKind.String:
                row[prefix] = value.GetString() ?? "";
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                row[prefix] = "";
                break;
            default:
                row[prefix] = value.GetRawText();
                break;
        }
    }

    private static string Escape(string text) =>
        text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

    private string TypeDir(DataType type) => Path.Combine(_dir, "objects", type.FullName);

    private static JsonElement Parse(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.Clone();
    }
}
