using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

public sealed record ModSpeciesTexturesParams(string Species);

/// <summary>Slot: "adult colour", "baby normal"…; SharedWith: other species using the same texture (a replacement changes them too).</summary>
public sealed record SpeciesTextureDto(string Texture, string Slot, IReadOnlyList<string> Skins, IReadOnlyList<string> SharedWith);

/// <summary>SpeciesId: the dump's id for the species asked for (by id or asset key); null when it is not in the dump.</summary>
public sealed record ModSpeciesTexturesResult(string? SpeciesId, IReadOnlyList<SpeciesTextureDto> Textures);

/// <summary>mods.* for texture replacements: the mod editor's Texture replacements → + Add.</summary>
public sealed partial class ModsMethods
{
    [RpcMethod("mods.speciesTextures")]
    public ModSpeciesTexturesResult SpeciesTextures(ModSpeciesTexturesParams p)
    {
        var (ws, _) = session.Current();
        var species = TrySpecies(ws) ?? throw new TyrantException(TyrantErrorCode.DataMissing,
            "Listing a species' textures needs the game's data: on the Workspace tab click Run data dump (or run 'tyrant dump run').", FixAction.RefreshWorkspace);
        var index = TryIndex(ws) ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing,
            "Listing a species' textures needs the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index').", FixAction.ReindexAssets);
        var textures = Tyrant.Core.Mods.SpeciesTextures.For(species, index, p.Species);
        var id = species.FirstOrDefault(s => string.Equals(s.SpeciesId, p.Species, StringComparison.OrdinalIgnoreCase))?.SpeciesId
            ?? species.FirstOrDefault(s => Normal(s.SpeciesId) == Normal(p.Species))?.SpeciesId;
        return new ModSpeciesTexturesResult(id, textures.Select(t => new SpeciesTextureDto(t.Texture, t.Slot, t.Skins, t.SharedWith)).ToList());
    }

    private static string Normal(string id) => new(id.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    [RpcMethod("mods.setCompression")]
    public ModDetail SetCompression(ModSetCompressionParams p)
    {
        TextureCompression? mode = p.Mode is null ? null
            : TextureCompressions.Parse(p.Mode) ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{p.Mode}' is not a texture compression; use auto or bc7.");
        return Edit(p.Id, p.Revision, mod => mod.SetCompression(p.File, mode),
            p.File is null ? $"compresses its textures as {p.Mode}" : $"set {p.File}'s compression to {p.Mode ?? "the mod's"}");
    }

    /// <summary>Every texture Tyrant cooks for the mod, with the format and in-game size it will get.</summary>
    [RpcMethod("mods.textures")]
    public ModTexturesResult Textures(ModIdParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var estimates = Cooker(ws, install, mod).Estimate(mod.Manifest, mod.Dir);
        var rows = estimates.Select(e => new ModTextureDto(e.File, KindName(e.Kind), TextureCompressions.Name(e.Mode), e.Override,
            UnityTextureFormats.Name(e.Format), e.Width, e.Height, e.Bytes)).ToList();
        return new ModTexturesResult(TextureCompressions.Name(mod.Manifest.Textures.Compression), rows, rows.Sum(r => r.Bytes));
    }

    private static string KindName(SlotKind kind) => kind == SlotKind.Normal ? "normal" : kind == SlotKind.Data ? "mask" : "colour";

    private static ModTextureCooker Cooker(Workspace ws, GameInstall install, ModProject mod) =>
        // The index and dump are only needed to tell what a replacement fills; skin slots carry their kind (and parsing them is slow).
        mod.Manifest.Replace.Count > 0 ? ModTextureCooker.For(ws, TryIndex(ws)?.Assets, TrySpeciesFor(ws, install, mod)) : ModTextureCooker.For(ws, null, null);

    /// <summary>Cooking progress as job progress between two fractions: "Cooking textures 3/20...". Synchronous, so reports stay in order and end with the job.</summary>
    private static IProgress<(int Done, int Total)> Cooking(IProgress<JobProgress> progress, double from, double to) =>
        new CookingProgress(p => progress.Report(new JobProgress(from + (to - from) * p.Done / Math.Max(1, p.Total), $"Cooking textures {p.Done}/{p.Total}...")));

    private sealed class CookingProgress(Action<(int Done, int Total)> report) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => report(value);
    }
}
