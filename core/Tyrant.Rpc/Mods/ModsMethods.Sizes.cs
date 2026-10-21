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
/// A mod's size changes for one species (null: unchanged), the game's original, and the model length they give. File: the
/// sizes/ file they are in (or would go in); Check's messages about it start with "sizes/&lt;file&gt;:".
/// </summary>
public sealed record ModSizesDto(string Target, string File, double? Adult, double? Baby, double? Male, double? Female, ModOriginalSizeDto Original, double? FinalAdultLength);

public sealed record ModSizesParams(string Id, string Target);
public sealed record ModSetSizesParams(string Id, string Revision, string Target, double? Adult = null, double? Baby = null, double? Male = null, double? Female = null);
public sealed record ModSizeTargetsParams(string Id);
public sealed record ModSizeTargetsDto(IReadOnlyList<string> Targets);
public sealed record ModSizeRangesParams(string Id);

/// <summary>Nursery and Breeding are the mod's [min, max] (null: the game's); GameNursery and GameBreeding are the game's own.</summary>
public sealed record ModSizeRangesDto(IReadOnlyList<double>? Nursery, IReadOnlyList<double>? Breeding, IReadOnlyList<double> GameNursery, IReadOnlyList<double> GameBreeding);
public sealed record ModSetSizeRangesParams(string Id, string Revision, IReadOnlyList<double>? Nursery = null, IReadOnlyList<double>? Breeding = null);

/// <summary>mods.sizes and the size edits (undo and redo go through mods.restoreSnapshot).</summary>
public sealed partial class ModsMethods
{
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
        return new ModSizesDto(set?.Target ?? p.Target, mod.SizeFileOf(p.Target), set?.Adult, set?.Baby, set?.Male, set?.Female, original, length is { } l ? l * (set?.Adult ?? 1) : null);
    }

    [RpcMethod("mods.setSizes")]
    public ModDetail SetSizes(ModSetSizesParams p) =>
        Edit(p.Id, p.Revision, mod => mod.SetSizes(p.Target, new SizeSettings { Target = p.Target, Adult = p.Adult, Baby = p.Baby, Male = p.Male, Female = p.Female }),
            $"set the sizes of {p.Target}");

    [RpcMethod("mods.sizeTargets")]
    public ModSizeTargetsDto SizeTargets(ModSizeTargetsParams p)
    {
        var (ws, _) = session.Current();
        return new ModSizeTargetsDto(ModProject.Open(ws, p.Id).SizeTargets);
    }

    [RpcMethod("mods.sizeRanges")]
    public ModSizeRangesDto SizeRanges(ModSizeRangesParams p)
    {
        var (ws, _) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var facts = SpeciesSizes.Facts(ws, mod);
        var (incubation, breeding) = (facts?.Incubation ?? SpeciesSizes.DefaultIncubation, facts?.Breeding ?? SpeciesSizes.DefaultBreeding);
        var ranges = mod.Manifest.SizeRanges;
        return new ModSizeRangesDto(ranges?.Nursery, ranges?.Breeding, [incubation.Min, incubation.Max], [breeding.Min, breeding.Max]);
    }

    [RpcMethod("mods.setSizeRanges")]
    public ModDetail SetSizeRanges(ModSetSizeRangesParams p) =>
        Edit(p.Id, p.Revision, mod => mod.SetSizeRanges(new SizeRanges { Nursery = p.Nursery?.ToArray(), Breeding = p.Breeding?.ToArray() }), "set its genetic size ranges");
}
