using System.Globalization;
using Tyrant.Core.Data;

namespace Tyrant.Rpc.Data;

public static class DataQuery
{
    public static string[] Tokens(string? filter) =>
        (filter ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Rows where every filter word appears in the name or any field, sorted by one column.</summary>
    public static IReadOnlyList<DataTableRow> Apply(DataTable table, string? filter, string? sort, bool descending)
    {
        var tokens = Tokens(filter);
        IEnumerable<DataTableRow> rows = table.Rows;
        if (tokens.Length > 0) rows = rows.Where(row => tokens.All(token => Matches(row, token)));
        if (!string.IsNullOrEmpty(sort))
        {
            var comparer = Comparer<string>.Create(CompareValues);
            rows = descending ? rows.OrderByDescending(r => r.Get(sort), comparer) : rows.OrderBy(r => r.Get(sort), comparer);
        }
        return rows.ToList();
    }

    /// <summary>Numbers compare by value ("9" before "10"), other text case-insensitively; empty values sort last.</summary>
    public static int CompareValues(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        if (a.Length == 0 || b.Length == 0) return (a.Length == 0).CompareTo(b.Length == 0);
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return x.CompareTo(y);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(DataTableRow row, string token) =>
        row.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
        || row.Values.Values.Any(v => v.Contains(token, StringComparison.OrdinalIgnoreCase));
}
