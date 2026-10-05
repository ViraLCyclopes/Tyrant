using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
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
}
