using System.Text;
using System.Text.Json;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Data;

public sealed record DataType(string FullName, string ShortName, int Count);

public sealed record LanguageInfo(string Code, string Name, int TermCount);

public sealed record LocalizationTable(string Code, string Name, IReadOnlyDictionary<string, string> Terms);

/// <summary>Read-only access to a dump in &lt;workspace&gt;/data.</summary>
public sealed class DataStore
{
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
            ?? throw new TyrantException(TyrantErrorCode.DataMissing, "No game data yet. On the Home tab, click Install Tyrant in game once, then Run data dump (or 'tyrant dump install', then 'tyrant dump run').", FixAction.RefreshWorkspace);
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
            0 => throw new TyrantException(TyrantErrorCode.AssetNotFound, $"No data type '{query}'. The Data tab lists them (or 'tyrant data types')."),
            _ => throw new TyrantException(TyrantErrorCode.AssetAmbiguous, $"'{query}' matches several types: {string.Join(", ", byShort.Select(t => t.FullName))}."),
        };
    }

    public IReadOnlyList<string> ObjectNames(DataType type) =>
        Directory.GetFiles(TypeDir(type), "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();

    public JsonElement Load(DataType type, string name)
    {
        var file = Directory.GetFiles(TypeDir(type), "*.json")
            .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new TyrantException(TyrantErrorCode.AssetNotFound, $"No {type.ShortName} named '{name}'. The Data tab lists them (or 'tyrant data show {type.ShortName}').");
        return Parse(file);
    }

    public IEnumerable<(string Name, JsonElement Root)> LoadAll(DataType type) =>
        Directory.GetFiles(TypeDir(type), "*.json").Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => (Path.GetFileNameWithoutExtension(f), Parse(f)));

    /// <summary>Indented JSON that keeps non-ASCII text (é, 日本) and markup readable instead of escape sequences; for files and display only.</summary>
    public static readonly JsonSerializerOptions ReadableJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>UTF-8 with a byte-order mark, so Excel opens exported CSV files with the right characters.</summary>
    public static readonly System.Text.Encoding CsvEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string ToCsv(IEnumerable<(string Name, JsonElement Root)> objects) => DataTable.From(objects).ToCsv();

    /// <summary>Every dumped language (data/localization/&lt;code&gt;.json), ordered by code.</summary>
    public IReadOnlyList<LocalizationTable> LoadLocalization()
    {
        var dir = Path.Combine(_dir, "localization");
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase).Select(ReadLanguage).ToList();
    }

    /// <summary>Writes every object of a type as CSV (UTF-8 with BOM, for Excel) or JSON; returns the object count.</summary>
    public int Export(DataType type, string format, string path)
    {
        if (format is not ("csv" or "json"))
            throw new ArgumentException($"Unknown export format '{format}'; use csv or json.", nameof(format));
        var objects = LoadAll(type).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (format == "csv")
            File.WriteAllText(path, ToCsv(objects), CsvEncoding);
        else
            File.WriteAllText(path, JsonSerializer.Serialize(objects.Select(o => o.Root), ReadableJson));
        return objects.Count;
    }

    private static LocalizationTable ReadLanguage(string file)
    {
        var root = Parse(file);
        var terms = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("terms", out var list) && list.ValueKind == JsonValueKind.Object)
            foreach (var term in list.EnumerateObject())
                terms[term.Name] = term.Value.ValueKind == JsonValueKind.String ? term.Value.GetString() ?? "" : term.Value.GetRawText();
        var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : Path.GetFileNameWithoutExtension(file);
        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : code;
        return new LocalizationTable(code, name, terms);
    }

    private string TypeDir(DataType type) => Path.Combine(_dir, "objects", type.FullName);

    private static JsonElement Parse(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.Clone();
    }
}
