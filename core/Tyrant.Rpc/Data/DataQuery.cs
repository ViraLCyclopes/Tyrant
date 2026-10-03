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
            var ordered = rows.OrderBy(r => r.Get(sort).Length == 0); // empty values last in both directions
            rows = descending ? ordered.ThenByDescending(r => r.Get(sort), comparer) : ordered.ThenBy(r => r.Get(sort), comparer);
        }
        return rows.ToList();
    }

    /// <summary>
    /// A total order: finite numbers first, by value ("9" before "10"); then other text, case-insensitively; empty values last.
    /// </summary>
    public static int CompareValues(string? a, string? b)
    {
        var (rankA, numberA) = Rank(a ?? "");
        var (rankB, numberB) = Rank(b ?? "");
        if (rankA != rankB) return rankA.CompareTo(rankB);
        return rankA switch
        {
            0 => numberA.CompareTo(numberB),
            1 => string.Compare(a, b, StringComparison.OrdinalIgnoreCase),
            _ => 0,
        };
    }

    private static (int Rank, double Number) Rank(string value) =>
        value.Length == 0 ? (2, 0)
        : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? (0, number)
        : (1, 0);

    private static bool Matches(DataTableRow row, string token) =>
        row.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
        || row.Values.Values.Any(v => v.Contains(token, StringComparison.OrdinalIgnoreCase));
}
