using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed partial class ModProject
{
    /// <summary>Sets a skin's "materials" JSON (null or {} removes it), validated like Check does.</summary>
    public void SetSkinMaterials(string skinId, string? materialsJson)
    {
        var skin = Skin(skinId);
        skin.Materials = ParseMaterials(materialsJson, skinId, skinLevel: true);
        Save();
    }

    /// <summary>
    /// Sets a species' "materials" JSON (null or {} removes it). They are kept per species id in the manifest, apart from its
    /// models entry.
    /// </summary>
    public void SetSpeciesMaterials(AssetIndex index, IReadOnlyList<SpeciesSkins> species, string speciesId, string? materialsJson)
    {
        var (id, _) = ResolveModelTarget(index, WithSpecies(species, Manifest), speciesId, null);
        var materials = ParseMaterials(materialsJson, id, skinLevel: false);
        if (materials is null) Manifest.SpeciesMaterials.Remove(id);
        else Manifest.SpeciesMaterials[id] = materials;
        Save();
    }

    /// <summary>
    /// Copies a ramp or mask PNG into the mod (materials/skins/&lt;skin&gt;/ or materials/species/&lt;species&gt;/iridescence-&lt;part&gt;.png:
    /// apart, so a skin and a species with the same id never share an image) and names it in the block
    /// of <paramref name="material"/>, or (null) removes it from the block. Returns the mod-relative path, or "" on removal.
    /// A species' materials must be set first (SetSpeciesMaterials).
    /// </summary>
    public string SetMaterialFile(string? skinId, string? speciesId, string material, string part, string? png)
    {
        if (part is not ("ramp" or "mask")) throw new TyrantException(TyrantErrorCode.ModInvalid, $"A material image is the ramp or the mask (got '{part}').");
        Dictionary<string, MaterialOverride>? blocks;
        string owner;
        if (skinId is not null)
        {
            blocks = Skin(skinId).Materials;
            owner = "skins/" + skinId;
        }
        else
        {
            blocks = speciesId is not null && Manifest.SpeciesMaterials.TryGetValue(speciesId, out var found) ? found : null;
            owner = "species/" + TextureExporter.Sanitize(speciesId ?? "");
            if (png is not null && blocks is null)
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{speciesId}' has no material settings yet; set them first.");
        }

        string file;
        if (png is null)
        {
            file = "";
            if (blocks is not null && blocks.TryGetValue(material, out var existing) && existing.Iridescence is { } iridescence)
            {
                if (part == "ramp") iridescence.Ramp = null;
                else iridescence.Mask = null;
                Tidy(blocks, material);
            }
        }
        else
        {
            file = $"materials/{owner}/iridescence-{part}.png";
            CopyPng(png, file);
            if (blocks is null)
            {
                blocks = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
                if (skinId is not null) Skin(skinId).Materials = blocks;
                else Manifest.SpeciesMaterials[speciesId!] = blocks;
            }
            if (!blocks.TryGetValue(material, out var block)) blocks[material] = block = new MaterialOverride();
            block.Iridescence ??= new IridescenceSettings();
            if (part == "ramp") block.Iridescence.Ramp = new RampSource { File = file };
            else block.Iridescence.Mask = new MaskSource { File = file };
        }

        if (skinId is not null) Skin(skinId).Materials = blocks is { Count: > 0 } ? blocks : null;
        else if (speciesId is not null)
        {
            if (blocks is { Count: > 0 }) Manifest.SpeciesMaterials[speciesId] = blocks;
            else Manifest.SpeciesMaterials.Remove(speciesId);
        }
        Save();
        return file;
    }

    /// <summary>All of this mod's material settings for a species (any case of its id); null for none.</summary>
    public IReadOnlyDictionary<string, MaterialOverride>? SpeciesMaterials(string speciesId) =>
        Manifest.SpeciesMaterials.TryGetValue(speciesId, out var found) ? found : null;

    /// <summary>Removes a species' material settings without needing the game's data (any case of its id); false when it had none.</summary>
    public bool ClearSpeciesMaterials(string speciesId)
    {
        if (!Manifest.SpeciesMaterials.Remove(speciesId)) return false;
        Save();
        return true;
    }

    /// <summary>This mod's settings for one material of a species; null for none.</summary>
    public MaterialOverride? SpeciesMaterial(string speciesId, string material) =>
        Manifest.SpeciesMaterials.TryGetValue(speciesId, out var blocks)
        && blocks.TryGetValue(material, out var found) ? found : null;

    /// <summary>This mod's settings for one material of a skin; null for none.</summary>
    public MaterialOverride? SkinMaterial(string skinId, string material) =>
        Skin(skinId).Materials is { } blocks && blocks.TryGetValue(material, out var found) ? found : null;

    private static Dictionary<string, MaterialOverride>? ParseMaterials(string? json, string who, bool skinLevel)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return MaterialOverrides.Parse(Json.Parse(json), who, skinLevel);
        }
        catch (Exception ex) when (ex is ManifestException or FormatException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"The materials of '{who}' cannot be saved: {ex.Message}");
        }
    }

    /// <summary>Drops what a removed ramp or mask leaves empty: the iridescence, then the material.</summary>
    private static void Tidy(Dictionary<string, MaterialOverride> blocks, string material)
    {
        var block = blocks[material];
        if (block.Iridescence is { IsEmpty: true }) block.Iridescence = null;
        if (block.IsEmpty) blocks.Remove(material);
    }
}
