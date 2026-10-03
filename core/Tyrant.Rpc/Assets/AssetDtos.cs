using System.Text.Json;

namespace Tyrant.Rpc.Assets;

/// <summary>One asset as listed in the browser; <see cref="Ref"/> ("bundle#pathId") identifies it in every other call.</summary>
public sealed record AssetRow(string Ref, string Bundle, long PathId, string Type, string Name, string? ContainerPath, string? Guid, string? Script);

public sealed record AssetCount(string Name, int Count);

public sealed record AssetsSummary(int Assets, int Bundles, IReadOnlyList<AssetCount> Groups, IReadOnlyList<AssetCount> Types,
    IReadOnlyList<string> Warnings, int Failures, int MissingBundles, bool Stale);

public sealed record AssetBundlesParams(string Group);

public sealed record AssetBundlesResult(IReadOnlyList<AssetCount> Bundles);

public sealed record AssetListParams(string? Filter = null, string? Type = null, string? Group = null, string? Bundle = null, int Page = 0,
    int PageSize = 200);

public sealed record AssetListResult(IReadOnlyList<AssetRow> Rows, int Total, int Page, int PageSize);

public sealed record AssetRefParams(string Ref);

/// <summary>A reference from the asset's fields: <see cref="Ref"/> when it points into the index, otherwise <see cref="External"/> names the target.</summary>
public sealed record AssetReferenceRow(string Field, string? Ref, string? Type, string? Name, string? External);

public sealed record AssetDetails(AssetRow Asset, long ByteSize, IReadOnlyList<AssetReferenceRow> References, JsonElement Fields);

public sealed record SpeciesRow(string Key, string DisplayName, bool Vivarium, string Group, string PrefabRef, int Textures);

public sealed record SpeciesListResult(IReadOnlyList<SpeciesRow> Species);
