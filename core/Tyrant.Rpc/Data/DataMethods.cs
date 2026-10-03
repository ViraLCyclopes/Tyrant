using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Data;

/// <summary>data.* — read-only browsing of the dump; tables are built once per type and dump, then queried server-side.</summary>
public sealed class DataMethods
{
    public const int DefaultColumnCount = 12;
    public const int MaxPageSize = 500;
    private const int MaxCompare = 20;

    private readonly StudioSession _session;
    private readonly object _lock = new();
    private readonly Dictionary<string, DataTable> _tables = new(StringComparer.Ordinal);
    private IReadOnlyList<LocalizationTable>? _languages;
    private string _cacheKey = "";

    /// <summary>Builds one type's table; replaceable so tests can interleave a slow build with a new dump.</summary>
    internal Func<DataStore, DataType, DataTable> BuildTable { get; set; } = (store, type) => DataTable.From(store.LoadAll(type));

    public DataMethods(StudioSession session)
    {
        _session = session;
        session.DataChanged += Clear;
    }

    [RpcMethod("data.types")]
    public DataTypesResult Types()
    {
        var (store, _, _, key) = Open();
        return new DataTypesResult(store.Manifest.CreatedUtc, store.Manifest.BuildGuid,
            store.Types().Select(t => new DataTypeInfo(t.FullName, t.ShortName, t.Count)).ToList(), store.Manifest.Errors);
    }

    [RpcMethod("data.query")]
    public DataQueryResult Query(DataQueryParams p)
    {
        var (store, _, _, key) = Open();
        var type = store.FindType(p.Type);
        var table = TableOf(store, type, key);
        var known = table.Columns.ToHashSet(StringComparer.Ordinal);
        var columns = p.Columns is null
            ? table.Columns.Take(DefaultColumnCount).ToList()
            : new[] { DataTable.NameColumn }.Concat(p.Columns.Where(c => c != DataTable.NameColumn && known.Contains(c))).Distinct().ToList();
        var sort = p.Sort is not null && known.Contains(p.Sort) ? p.Sort : null;
        var rows = DataQuery.Apply(table, p.Filter, sort, p.Descending);
        var (page, pageSize) = Paging(p.Page, p.PageSize);
        var pageRows = rows.Skip(page * pageSize).Take(pageSize).Select(r => new DataRow(r.Name, columns.Select(r.Get).ToList())).ToList();
        return new DataQueryResult(type.FullName, table.Columns, columns, pageRows, rows.Count, page, pageSize);
    }

    [RpcMethod("data.objects")]
    public DataObjectsResult Objects(DataObjectsParams p)
    {
        var (store, _, _, key) = Open();
        var names = store.ObjectNames(store.FindType(p.Type));
        var tokens = DataQuery.Tokens(p.Filter);
        return new DataObjectsResult(tokens.Length == 0
            ? names
            : names.Where(n => tokens.All(t => n.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList());
    }

    [RpcMethod("data.object")]
    public DataObjectResult Object(DataObjectParams p)
    {
        var (store, _, _, key) = Open();
        var type = store.FindType(p.Type);
        return new DataObjectResult(type.FullName, p.Name, store.Load(type, p.Name));
    }

    [RpcMethod("data.compare")]
    public DataCompareResult Compare(DataCompareParams p)
    {
        if (p.Names.Count is < 2 or > MaxCompare)
            throw new ArgumentException($"Pick between 2 and {MaxCompare} objects to compare.");
        var (store, _, _, key) = Open();
        var type = store.FindType(p.Type);
        var table = DataTable.From(p.Names.Select(n => (n, store.Load(type, n))));
        var fields = table.Columns.Skip(1)
            .Where(f => !p.OnlyDifferences || table.Rows.Select(r => r.Get(f)).Distinct(StringComparer.Ordinal).Count() > 1)
            .ToList();
        var values = fields.Select(f => (IReadOnlyList<string>)table.Rows.Select(r => r.Get(f)).ToList()).ToList();
        return new DataCompareResult(table.Rows.Select(r => r.Name).ToList(), fields, values);
    }

    [RpcMethod("data.languages")]
    public LanguagesResult Languages()
    {
        var (store, _, _, key) = Open();
        return new LanguagesResult(LanguagesOf(store, key).Select(l => new LanguageInfo(l.Code, l.Name, l.Terms.Count)).ToList());
    }

    [RpcMethod("data.localization")]
    public LocalizationQueryResult Localization(LocalizationQueryParams p)
    {
        var (store, _, _, key) = Open();
        var all = LanguagesOf(store, key);
        var selected = p.Languages is null ? all : all.Where(l => p.Languages.Contains(l.Code, StringComparer.OrdinalIgnoreCase)).ToList();
        var tokens = DataQuery.Tokens(p.Filter);
        var rows = all.SelectMany(l => l.Terms.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(term => new LocalizationRow(term, selected.Select(l => l.Terms.TryGetValue(term, out var v) ? v : "").ToList()))
            .Where(r => tokens.All(t => r.Term.Contains(t, StringComparison.OrdinalIgnoreCase) || r.Values.Any(v => v.Contains(t, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        var (page, pageSize) = Paging(p.Page, p.PageSize);
        return new LocalizationQueryResult(selected.Select(l => l.Code).ToList(), rows.Skip(page * pageSize).Take(pageSize).ToList(), rows.Count, page, pageSize);
    }

    [RpcMethod("data.export")]
    public DataExportResult Export(DataExportParams p)
    {
        var (store, ws, install, _) = Open();
        var type = store.FindType(p.Type);
        var format = p.Format.ToLowerInvariant();
        var path = Path.GetFullPath(p.Path ?? Path.Combine(ws.Dir, "exports", $"{type.ShortName}.{format}"));
        if (install.ContainsPath(path))
            throw new TyrantException(TyrantErrorCode.OutputInGameFolder, $"Refusing to write '{path}' inside the game folder.");
        return new DataExportResult(path, store.Export(type, format, path));
    }

    private static (int Page, int PageSize) Paging(int page, int pageSize) => (Math.Max(0, page), Math.Clamp(pageSize, 1, MaxPageSize));

    /// <summary>Opens the dump; the returned key identifies it, so results built from an older dump are never cached.</summary>
    private (DataStore Store, Workspace Workspace, GameInstall Install, string Key) Open()
    {
        var (ws, install) = _session.Current();
        var store = DataStore.Open(ws);
        var key = $"{ws.DataDir}|{store.Manifest.RequestId}|{store.Manifest.CreatedUtc}";
        lock (_lock)
        {
            if (key != _cacheKey)
            {
                _tables.Clear();
                _languages = null;
                _cacheKey = key;
            }
        }
        return (store, ws, install, key);
    }

    private DataTable TableOf(DataStore store, DataType type, string key)
    {
        lock (_lock)
            if (key == _cacheKey && _tables.TryGetValue(type.FullName, out var cached)) return cached;
        var table = BuildTable(store, type); // outside the lock: a big type takes a moment to read
        lock (_lock)
            if (key == _cacheKey) _tables[type.FullName] = table; // a newer dump arrived meanwhile: don't cache the old one
        return table;
    }

    private IReadOnlyList<LocalizationTable> LanguagesOf(DataStore store, string key)
    {
        lock (_lock)
            if (key == _cacheKey && _languages is not null) return _languages;
        var languages = store.LoadLocalization();
        lock (_lock)
            if (key == _cacheKey) _languages = languages;
        return languages;
    }

    private void Clear()
    {
        lock (_lock)
        {
            _tables.Clear();
            _languages = null;
            _cacheKey = "";
        }
    }
}
