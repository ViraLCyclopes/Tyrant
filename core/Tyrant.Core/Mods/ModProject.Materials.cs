using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed partial class ModProject
{
    /// <summary>Sets a skin's materials (materials/skins/&lt;skin&gt;/materials.json; null or {} removes them), validated like Check does.</summary>
    public void SetSkinMaterials(string skinId, string? materialsJson)
    {
        var skin = Skin(skinId);
        skin.Materials = ParseMaterials(materialsJson, skinId, skinLevel: true);
        Save();
    }

    /// <summary>
    /// Sets a species' materials (materials/species/&lt;species&gt;/materials.json; null or {} removes them), under the game's
    /// spelling of its id. They need no models entry.
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
    /// Sets a vanilla skin's materials (materials/vanilla-skins/&lt;species&gt;-&lt;skin&gt;/materials.json; null or {} removes
    /// them). They restyle every animal wearing that vanilla skin, not mod skins based on it, and a mask may come from one of
    /// the skin's own maps ("from"): the game has them.
    /// </summary>
    public void SetVanillaSkinMaterials(string species, string skinName, string? materialsJson)
    {
        var key = VanillaSkinKey(species, skinName);
        var materials = ParseMaterials(materialsJson, key, skinLevel: true);
        if (materials is null) Manifest.VanillaSkinMaterials.Remove(key);
        else Manifest.VanillaSkinMaterials[key] = materials;
        Save();
    }

    /// <summary>
    /// <see cref="SetMaterialFile(MaterialOwner, string, string, string?)"/> for a skin (<paramref name="skinId"/>) or else a
    /// species (<paramref name="speciesId"/>).
    /// </summary>
    public string SetMaterialFile(string? skinId, string? speciesId, string material, string part, string? png) =>
        SetMaterialFile(skinId is not null ? new MaterialOwner(MaterialOwnerKind.Skin, skinId, null, null)
            : new MaterialOwner(MaterialOwnerKind.Species, null, speciesId ?? "", null), material, part, png);

    /// <summary>
    /// Copies an image into the owner's material folder (iridescence-ramp.png, iridescence-mask.png, fur-mask.png or
    /// fur-infant-mask.png for the parts "ramp", "mask", "furMask" and "furInfantMask") and names it in the block of
    /// <paramref name="material"/>, or (null) removes it from the block. Skins, species and vanilla skins keep their images
    /// apart, so a skin and a species with the same id never share one. Returns the mod-relative path, or "" on removal.
    /// A species' materials must be set first (SetSpeciesMaterials).
    /// </summary>
    public string SetMaterialFile(MaterialOwner owner, string material, string part, string? png)
    {
        var name = part switch
        {
            "ramp" => "iridescence-ramp.png",
            "mask" => "iridescence-mask.png",
            "furMask" => "fur-mask.png",
            "furInfantMask" => "fur-infant-mask.png",
            _ => throw new TyrantException(TyrantErrorCode.ModInvalid, $"A material image is the ramp, the mask, furMask or furInfantMask (got '{part}')."),
        };
        owner = Canonical(owner);
        var blocks = MaterialsOf(owner);
        if (png is not null && blocks is null && owner.Kind == MaterialOwnerKind.Species)
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{owner.Species}' has no material settings yet; set them first.");

        var file = "";
        if (png is null)
        {
            if (blocks is not null && blocks.TryGetValue(material, out var existing))
            {
                Place(existing, part, null);
                Tidy(blocks, material);
            }
        }
        else
        {
            file = ImageFolder(owner) + "/" + name;
            CopyPng(png, file);
            blocks ??= new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
            if (!blocks.TryGetValue(material, out var block)) blocks[material] = block = new MaterialOverride();
            Place(block, part, file);
        }
        SetMaterialsOf(owner, blocks);
        Save();
        return file;
    }

    /// <summary>All of this mod's material settings for a species (any case of its id); null for none.</summary>
    public IReadOnlyDictionary<string, MaterialOverride>? SpeciesMaterials(string speciesId) =>
        Manifest.SpeciesMaterials.TryGetValue(speciesId, out var found) ? found : null;

    /// <summary>All of this mod's material settings for a vanilla skin ("&lt;species&gt;/&lt;skin name&gt;", any case); null for none.</summary>
    public IReadOnlyDictionary<string, MaterialOverride>? VanillaSkinMaterials(string key) =>
        Manifest.VanillaSkinMaterials.TryGetValue(key, out var found) ? found : null;

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

    /// <summary>"&lt;species&gt;/&lt;skin name&gt;": the species cannot hold a '/', the skin name can.</summary>
    private static string VanillaSkinKey(string? species, string? skinName)
    {
        var (s, k) = (species?.Trim() ?? "", skinName?.Trim() ?? "");
        if (s.Length == 0 || k.Length == 0 || s.Contains('/'))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"A vanilla skin is named by its species id and its skin's name (got '{species}' and '{skinName}').");
        return s + "/" + k;
    }

    /// <summary>The owner as the manifest already spells it (species and vanilla skins match in any case), so its folder stays the same.</summary>
    private MaterialOwner Canonical(MaterialOwner owner)
    {
        switch (owner.Kind)
        {
            case MaterialOwnerKind.Skin:
                return owner with { SkinId = Skin(owner.SkinId ?? "").Id };
            case MaterialOwnerKind.Species:
                var species = Manifest.SpeciesMaterials.Keys.FirstOrDefault(k => string.Equals(k, owner.Species, StringComparison.OrdinalIgnoreCase));
                return owner with { Species = species ?? owner.Species };
            default:
                var key = VanillaSkinKey(owner.Species, owner.SkinName);
                key = Manifest.VanillaSkinMaterials.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
                var slash = key.IndexOf('/');
                return owner with { Species = key[..slash], SkinName = key[(slash + 1)..] };
        }
    }

    /// <summary>
    /// The owner's folder for its images: its own folder, or with "-2", "-3"… while another owner's images are there (names
    /// that sanitize alike, e.g. vanilla skins "Alt 1/2" and "Alt 1_2"), so two owners never share an image.
    /// </summary>
    private string ImageFolder(MaterialOwner owner)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(IReadOnlyDictionary<string, MaterialOverride>? materials)
        {
            foreach (var file in MaterialFiles(materials)) taken.Add(Path.GetDirectoryName(Normal(file))!.Replace('\\', '/'));
        }
        foreach (var skin in Manifest.Skins.Where(s => owner.Kind != MaterialOwnerKind.Skin || s.Id != owner.SkinId)) Add(skin.Materials);
        foreach (var (key, materials) in Manifest.SpeciesMaterials)
            if (owner.Kind != MaterialOwnerKind.Species || !string.Equals(key, owner.Species, StringComparison.OrdinalIgnoreCase)) Add(materials);
        foreach (var (key, materials) in Manifest.VanillaSkinMaterials)
            if (owner.Kind != MaterialOwnerKind.VanillaSkin || !string.Equals(key, owner.Species + "/" + owner.SkinName, StringComparison.OrdinalIgnoreCase)) Add(materials);
        var folder = MaterialFolders.FolderOf(owner);
        var candidate = folder;
        for (var n = 2; taken.Contains(candidate); n++) candidate = folder + "-" + n;
        return candidate;
    }

    private Dictionary<string, MaterialOverride>? MaterialsOf(MaterialOwner owner) => owner.Kind switch
    {
        MaterialOwnerKind.Skin => Skin(owner.SkinId!).Materials,
        MaterialOwnerKind.Species => Manifest.SpeciesMaterials.GetValueOrDefault(owner.Species!),
        _ => Manifest.VanillaSkinMaterials.GetValueOrDefault(owner.Species + "/" + owner.SkinName),
    };

    /// <summary>Puts the owner's materials back; nothing left is null (no materials.json), not {}.</summary>
    private void SetMaterialsOf(MaterialOwner owner, Dictionary<string, MaterialOverride>? blocks)
    {
        blocks = blocks is { Count: > 0 } ? blocks : null;
        var map = owner.Kind switch
        {
            MaterialOwnerKind.Skin => null,
            MaterialOwnerKind.Species => Manifest.SpeciesMaterials,
            _ => Manifest.VanillaSkinMaterials,
        };
        var key = owner.Kind == MaterialOwnerKind.Species ? owner.Species! : owner.Species + "/" + owner.SkinName;
        if (map is null) Skin(owner.SkinId!).Materials = blocks;
        else if (blocks is null) map.Remove(key);
        else map[key] = blocks;
    }

    /// <summary>Names (or, with null, removes) an image part of a block.</summary>
    private static void Place(MaterialOverride block, string part, string? file)
    {
        var mask = file is null ? null : new MaskSource { File = file };
        if (part is "ramp" or "mask")
        {
            if (file is null && block.Iridescence is null) return;
            var iridescence = block.Iridescence ??= new IridescenceSettings();
            if (part == "ramp") iridescence.Ramp = file is null ? null : new RampSource { File = file };
            else iridescence.Mask = mask;
        }
        else
        {
            if (file is null && block.Fur is null) return;
            var fur = block.Fur ??= new FurMasks();
            if (part == "furMask") fur.Mask = mask;
            else fur.InfantMask = mask;
        }
    }

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

    /// <summary>Drops what a removed image leaves empty: the iridescence or fur, then the material.</summary>
    private static void Tidy(Dictionary<string, MaterialOverride> blocks, string material)
    {
        var block = blocks[material];
        if (block.Iridescence is { IsEmpty: true }) block.Iridescence = null;
        if (block.Fur is { IsEmpty: true }) block.Fur = null;
        if (block.IsEmpty) blocks.Remove(material);
    }
}
