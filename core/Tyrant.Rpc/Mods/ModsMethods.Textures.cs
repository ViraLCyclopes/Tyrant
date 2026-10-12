using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Mods;
using Tyrant.Core.Textures;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using Tyrant.Rpc.Jobs;
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
        var cooker = Cooker(ws, install, mod);
        var estimates = cooker.Estimate(mod.Manifest, mod.Dir);
        var rows = estimates.Select(e => new ModTextureDto(e.File, KindName(e.Kind), TextureCompressions.Name(e.Mode), e.Override,
            UnityTextureFormats.Name(e.Format), e.Width, e.Height, e.Bytes)).ToList();
        var pack = new FileInfo(Path.Combine(mod.Dir, TexturePack.FileName));
        return new ModTexturesResult(TextureCompressions.Name(mod.Manifest.Textures.Compression), rows, rows.Sum(r => r.Bytes),
            cooker.PackState(mod.Manifest, mod.Dir), pack.Exists ? pack.Length : 0);
    }

    /// <summary>Cook textures: the mod's textures.tpack in its workspace folder, without installing (install writes it too).</summary>
    [RpcMethod("mods.cookTextures", JobResult = typeof(ModCookResult))]
    public JobStarted CookTextures(ModIdParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id); // an unknown id fails before the job starts
        return jobs.Start($"Cook {p.Id}'s textures", (progress, ct) =>
        {
            var path = Cooker(ws, install, mod).CookInPlace(mod, Cooking(progress, 0.02, 0.98), ct);
            var message = path is null ? $"'{p.Id}' has no textures to cook." : $"Cooked '{p.Id}'s textures into {path}.";
            session.Log(message);
            return new ModCookResult(message, path);
        });
    }

    private static string KindName(SlotKind kind) => kind == SlotKind.Normal ? "normal" : kind == SlotKind.Data ? "mask" : "colour";

    private static ModTextureCooker Cooker(Workspace ws, GameInstall install, ModProject mod) =>
        // The index and dump are only needed to tell what a replacement fills; skin slots carry their kind (and parsing them is slow).
        mod.Manifest.Replace.Count > 0 ? ModTextureCooker.For(ws, TryIndex(ws)?.Assets, TrySpeciesFor(ws, install, mod)) : ModTextureCooker.For(ws, null, null);

    /// <summary>Cooking progress as job progress between two fractions: "Cooking textures 3/20 (file)...". Synchronous, so reports stay in order and end with the job.</summary>
    private static IProgress<CookProgress> Cooking(IProgress<JobProgress> progress, double from, double to) =>
        new CookingProgress(p => progress.Report(new JobProgress(from + (to - from) * p.Fraction, p.Message)));

    private sealed class CookingProgress(Action<CookProgress> report) : IProgress<CookProgress>
    {
        public void Report(CookProgress value) => report(value);
    }
}
