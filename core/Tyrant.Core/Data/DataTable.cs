using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tyrant.Core.Data;

public sealed record DataTableRow(string Name, IReadOnlyDictionary<string, string> Values)
{
    public string Get(string column) =>
        column == DataTable.NameColumn ? Name : Values.TryGetValue(column, out var value) ? value : "";
}

/// <summary>Objects flattened into rows: nested objects become dotted columns, arrays compact JSON, references the target's name.</summary>
public sealed class DataTable
{
    public const string NameColumn = "$name";
    private const int MaxFlattenDepth = 4;
    private static readonly JsonSerializerOptions CompactReadableJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private DataTable(IReadOnlyList<string> columns, IReadOnlyList<DataTableRow> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    /// <summary>Every column in first-seen order, starting with <see cref="NameColumn"/>.</summary>
    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<DataTableRow> Rows { get; }

    public static DataTable From(IEnumerable<(string Name, JsonElement Root)> objects)
    {
        var columns = new List<string> { NameColumn };
        var seen = new HashSet<string>(StringComparer.Ordinal) { NameColumn };
        var rows = new List<DataTableRow>();
        foreach (var (name, root) in objects)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.ValueKind == JsonValueKind.Object)
                foreach (var property in root.EnumerateObject())
                    if (property.Name is not ("$type" or "$name" or "$id"))
                        Flatten(property.Name, property.Value, values, 1);
            foreach (var key in values.Keys)
                if (seen.Add(key)) columns.Add(key);
            rows.Add(new DataTableRow(name, values));
        }
        return new DataTable(columns, rows);
    }

    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Columns.Select(Escape))).Append("\r\n");
        foreach (var row in Rows)
            sb.Append(string.Join(",", Columns.Select(c => Escape(row.Get(c))))).Append("\r\n");
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
}
