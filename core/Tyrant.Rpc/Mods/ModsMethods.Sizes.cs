using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

public sealed record ModSkinSizeDto(double Male, double Female);

/// <summary>A species' size in the game data: the model's length, the newborn's share of the adult size and each skin's sex multipliers.</summary>
public sealed record ModOriginalSizeDto(double? AdultLength, double BabyShare, IReadOnlyList<ModSkinSizeDto> Skins);

/// <summary>
/// A mod's size changes for one species (null: the game's), the game's original, and the model length they give. File: the
/// sizes/ file they are in (or would go in); Check's messages about it start with "sizes/&lt;file&gt;:". RecordEdits: a mod
/// species' old record edits that change its size outside Size (mods.removeSizeEdits removes them). Nursery, Breeding and
/// Limit: the species' own genetic size ranges and size limit, each [min, max] (null: the game's); GameNursery, GameBreeding
/// and GameLimit are the game's own (the same for every species, a custom one's being its template's).
/// </summary>
public sealed record ModSizesDto(string Target, string File, double? Adult, double? Baby, double? Male, double? Female, ModOriginalSizeDto Original,
    double? FinalAdultLength, IReadOnlyList<string> RecordEdits, IReadOnlyList<double>? Nursery, IReadOnlyList<double>? Breeding, IReadOnlyList<double>? Limit,
    IReadOnlyList<double> GameNursery, IReadOnlyList<double> GameBreeding, IReadOnlyList<double> GameLimit);

public sealed record ModSizesParams(string Id, string Target);
public sealed record ModRemoveSizeEditsParams(string Id, string Revision, string Target);

/// <summary>Replaces the species' whole set: a key left out (null) is the game's.</summary>
public sealed record ModSetSizesParams(string Id, string Revision, string Target, double? Adult = null, double? Baby = null, double? Male = null, double? Female = null,
    IReadOnlyList<double>? Nursery = null, IReadOnlyList<double>? Breeding = null, IReadOnlyList<double>? Limit = null);
public sealed record ModSizeTargetsParams(string Id);
public sealed record ModSizeTargetsDto(IReadOnlyList<string> Targets);
/// <summary>mods.sizes and the size edits (undo and redo go through mods.restoreSnapshot).</summary>
public sealed partial class ModsMethods
{
    /// <summary>The game's size limit, 0.05 to 2 (as doubles: the game's floats rounded).</summary>
    private static double[] GameLimit => [Math.Round(SizeMath.FinalMin, 6), Math.Round(SizeMath.FinalMax, 6)];

    [RpcMethod("mods.sizes")]
    public ModSizesDto Sizes(ModSizesParams p)
    {
        var (ws, _) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var set = mod.Sizes(p.Target);
        OriginalSize? found = null;
        try
        {
            found = SpeciesSizes.Original(DataStore.Open(ws), p.Target, mod);
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound)
        {
        }
        var length = found?.LengthHeightWidth is { Length: > 0 } lhw ? (double?)lhw[0] : null;
        var c0 = found is null ? 0f : SizeMath.Evaluate(found.Curve, 0f);
        var c1 = found is null ? 0f : SizeMath.Evaluate(found.Curve, 1f);
        var original = new ModOriginalSizeDto(length, c1 == 0f ? 1 : c0 / (double)c1, (found?.Skins ?? []).Select(s => new ModSkinSizeDto(s.Male, s.Female)).ToList());
        var (incubation, breeding) = SpeciesSizes.GameRanges(ws);
        return new ModSizesDto(set?.Target ?? p.Target, mod.SizeFileOf(p.Target), set?.Adult, set?.Baby, set?.Male, set?.Female, original, length is { } l ? l * (set?.Adult ?? 1) : null,
            mod.SizeRecordEdits(p.Target), set?.Nursery, set?.Breeding, set?.Limit, [incubation.Min, incubation.Max], [breeding.Min, breeding.Max], GameLimit);
    }

    [RpcMethod("mods.setSizes")]
    public ModDetail SetSizes(ModSetSizesParams p) =>
        Edit(p.Id, p.Revision, mod => mod.SetSizes(p.Target, new SizeSettings
        {
            Target = p.Target, Adult = p.Adult, Baby = p.Baby, Male = p.Male, Female = p.Female,
            Nursery = p.Nursery?.ToArray(), Breeding = p.Breeding?.ToArray(), Limit = p.Limit?.ToArray(),
        }), $"set the sizes of {p.Target}");

    /// <summary>Removes a mod species' old record edits of its size (Check's "Set sizes in Size instead").</summary>
    [RpcMethod("mods.removeSizeEdits")]
    public ModDetail RemoveSizeEdits(ModRemoveSizeEditsParams p) =>
        Edit(p.Id, p.Revision, mod => mod.RemoveSizeRecordEdits(p.Target), $"removed the old size edits of {p.Target}");

    [RpcMethod("mods.sizeTargets")]
    public ModSizeTargetsDto SizeTargets(ModSizeTargetsParams p)
    {
        var (ws, _) = session.Current();
        return new ModSizeTargetsDto(ModProject.Open(ws, p.Id).SizeTargets);
    }
}
