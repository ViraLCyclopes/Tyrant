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
    /// Sets a species' "materials" JSON (null or {} removes it). They live in the species' models entry, which has no file of its
    /// own then; clearing them removes an entry that has no model file and no rig edit.
    /// </summary>
    public void SetSpeciesMaterials(AssetIndex index, IReadOnlyList<SpeciesSkins> species, string speciesId, string? materialsJson)
    {
        var (id, prefab) = ResolveModelTarget(index, WithSpecies(species, Manifest), speciesId, null);
        var materials = ParseMaterials(materialsJson, id, skinLevel: false);
        var entry = SpeciesEntry(id);
        if (entry is null && materials is not null)
            Manifest.Models.Add(entry = new ModelReplacement { Target = id, Key = prefab.ContainerPath, File = "" });
        if (entry is not null)
        {
            entry.Materials = materials;
            DropIfBare(entry);
        }
        Save();
    }

    /// <summary>
    /// Copies a ramp or mask PNG into the mod (materials/&lt;skin or species&gt;/iridescence-&lt;part&gt;.png) and names it in the block
    /// of <paramref name="material"/>, or (null) removes it from the block. Returns the mod-relative path, or "" on removal.
    /// A species' materials must be set first (SetSpeciesMaterials).
    /// </summary>
    public string SetMaterialFile(string? skinId, string? speciesId, string material, string part, string? png)
    {
        if (part is not ("ramp" or "mask")) throw new TyrantException(TyrantErrorCode.ModInvalid, $"A material image is the ramp or the mask (got '{part}').");
        ModelReplacement? entry = null;
        Dictionary<string, MaterialOverride>? blocks;
        string owner;
        if (skinId is not null)
        {
            blocks = Skin(skinId).Materials;
            owner = skinId;
        }
        else
        {
            entry = speciesId is null ? null : SpeciesEntry(speciesId);
            blocks = entry?.Materials;
            owner = TextureExporter.Sanitize(entry?.Target ?? speciesId ?? "");
            if (png is not null && entry is null)
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
                else entry!.Materials = blocks;
            }
            if (!blocks.TryGetValue(material, out var block)) blocks[material] = block = new MaterialOverride();
            block.Iridescence ??= new IridescenceSettings();
            if (part == "ramp") block.Iridescence.Ramp = new RampSource { File = file };
            else block.Iridescence.Mask = new MaskSource { File = file };
        }

        if (skinId is not null) Skin(skinId).Materials = blocks is { Count: > 0 } ? blocks : null;
        else if (entry is not null)
        {
            entry.Materials = blocks is { Count: > 0 } ? blocks : null;
            DropIfBare(entry);
        }
        Save();
        return file;
    }

    /// <summary>All of this mod's material settings for a species (any case of its id); null for none.</summary>
    public IReadOnlyDictionary<string, MaterialOverride>? SpeciesMaterials(string speciesId) => SpeciesEntry(speciesId)?.Materials;

    /// <summary>Removes a species' material settings without needing the game's data (any case of its id); false when it had none.</summary>
    public bool ClearSpeciesMaterials(string speciesId)
    {
        if (SpeciesEntry(speciesId) is not { Materials: not null } entry) return false;
        entry.Materials = null;
        DropIfBare(entry);
        Save();
        return true;
    }

    /// <summary>This mod's settings for one material of a species; null for none.</summary>
    public MaterialOverride? SpeciesMaterial(string speciesId, string material) =>
        SpeciesEntry(speciesId)?.Materials is { } blocks
        && blocks.TryGetValue(material, out var found) ? found : null;

    /// <summary>This mod's settings for one material of a skin; null for none.</summary>
    public MaterialOverride? SkinMaterial(string skinId, string material) =>
        Skin(skinId).Materials is { } blocks && blocks.TryGetValue(material, out var found) ? found : null;

    /// <summary>A species' models entry (any case of its id); null for none.</summary>
    private ModelReplacement? SpeciesEntry(string speciesId) =>
        Manifest.Models.FirstOrDefault(m => string.Equals(m.Target, speciesId, StringComparison.OrdinalIgnoreCase));

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

    private void DropIfBare(ModelReplacement entry)
    {
        if (entry.File.Length == 0 && entry.Rig is not { Count: > 0 } && entry.Materials == null) Manifest.Models.Remove(entry);
    }
}
