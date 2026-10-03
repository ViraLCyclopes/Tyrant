using System.Text.Json;
using Tyrant.Core.Data;

namespace Tyrant.Rpc.Data;

public sealed record DataTypeInfo(string FullName, string ShortName, int Count);

public sealed record DataTypesResult(string CreatedUtc, string BuildGuid, IReadOnlyList<DataTypeInfo> Types, IReadOnlyList<string> Errors);

public sealed record DataQueryParams(string Type, string? Filter = null, string? Sort = null, bool Descending = false, int Page = 0,
    int PageSize = 100, IReadOnlyList<string>? Columns = null);

/// <summary>One table row; <see cref="Values"/> line up with <see cref="DataQueryResult.Columns"/>.</summary>
public sealed record DataRow(string Name, IReadOnlyList<string> Values);

public sealed record DataQueryResult(string Type, IReadOnlyList<string> AllColumns, IReadOnlyList<string> Columns, IReadOnlyList<DataRow> Rows,
    int Total, int Page, int PageSize);

public sealed record DataObjectsParams(string Type, string? Filter = null);

public sealed record DataObjectsResult(IReadOnlyList<string> Names);

public sealed record DataObjectParams(string Type, string Name);

public sealed record DataObjectResult(string Type, string Name, JsonElement Json);

public sealed record DataCompareParams(string Type, IReadOnlyList<string> Names, bool OnlyDifferences = false);

/// <summary><see cref="Values"/>[field][object] lines up with <see cref="Fields"/> and <see cref="Names"/>.</summary>
public sealed record DataCompareResult(IReadOnlyList<string> Names, IReadOnlyList<string> Fields, IReadOnlyList<IReadOnlyList<string>> Values);

public sealed record LanguagesResult(IReadOnlyList<LanguageInfo> Languages);

public sealed record LocalizationQueryParams(string? Filter = null, IReadOnlyList<string>? Languages = null, int Page = 0, int PageSize = 100);

public sealed record LocalizationRow(string Term, IReadOnlyList<string> Values);

public sealed record LocalizationQueryResult(IReadOnlyList<string> Languages, IReadOnlyList<LocalizationRow> Rows, int Total, int Page, int PageSize);

public sealed record DataExportParams(string Type, string Format = "csv", string? Path = null);

public sealed record DataExportResult(string Path, int Count);
