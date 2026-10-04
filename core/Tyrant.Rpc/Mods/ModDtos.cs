namespace Tyrant.Rpc.Mods;

/// <summary>
/// A mod in the workspace or the game. State: notInstalled, installed, changed (edited since install) or gameOnly
/// (installed, but not in this workspace). Enabled is null when the mod is not installed.
/// </summary>
public sealed record ModRow(string Id, string Name, string Version, string? Author, int Replacements, int Skins, string State, bool? Enabled, string? Dir, string? Error);

public sealed record ModsListResult(IReadOnlyList<ModRow> Mods, bool FrameworkInstalled);

public sealed record ModCreateParams(string Id, string? Name = null, string? Author = null);

/// <summary>Texture: name, Addressables path, GUID or ref. Png: null uses the texture's exported PNG.</summary>
public sealed record ModReplaceParams(string Id, string Texture, string? Png = null);

public sealed record ModIdParams(string Id);

public sealed record ModEnableParams(string Id, bool Enabled);

/// <summary>MissingCutouts: colour PNGs that lost the see-through parts (feathers, hair) of their vanilla texture.</summary>
public sealed record ModCheckReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, IReadOnlyList<string>? MissingCutouts = null);

public sealed record ModRestoreCutoutsResult(IReadOnlyList<string> Restored, IReadOnlyList<string> Problems);

public sealed record ModInstallResult(string Message, IReadOnlyList<string> Warnings);

public sealed record VanillaSkinRow(int Index, string Name, bool Male, bool Female);

public sealed record SpeciesSkinsRow(string SpeciesId, bool Vivarium, IReadOnlyList<VanillaSkinRow> Skins);

/// <summary>Species and their vanilla skins from the data dump; HasDump false when there is none yet.</summary>
public sealed record ModSpeciesResult(bool HasDump, IReadOnlyList<SpeciesSkinsRow> Species);

public sealed record ModAddSkinParams(string Id, string Species, string Name, string? Base = null, bool Male = true, bool Female = true, bool Maps = false);

public sealed record OrphanSkinRow(string Species, string Key, int Number);

public sealed record SkinSlotsResult(IReadOnlyList<OrphanSkinRow> Orphans);

public sealed record ModForgetSkinsParams(IReadOnlyList<string> Keys);

/// <summary>One added skin as the mod editor shows it. BaseMaleSlots / BaseFemaleSlots: the base skin's slots (null without a data dump).</summary>
public sealed record ModSkinDto(string Id, string Key, string Species, string Name, string Base, string? Thumbnail,
    IReadOnlyDictionary<string, string>? Male, IReadOnlyDictionary<string, string>? Female, string? ColorsJson,
    IReadOnlyList<string>? BaseMaleSlots, IReadOnlyList<string>? BaseFemaleSlots, string? Model = null);

/// <summary>One converted level of detail: its .tmesh, its vertices, 32-bit indices, and the game LOD's vertices.</summary>
public sealed record ModModelLodDto(string File, int Vertices, bool Index32, int Vanilla);

/// <summary>
/// A model replacement (Skin null: the species'); Stale: its .glb changed since it was built; Origin: the user's own .glb it was
/// added from, OriginChanged: that file changed since (a re-export to import again).
/// </summary>
public sealed record ModModelDto(string Target, string? Skin, string File, IReadOnlyList<ModModelLodDto> Lods, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, bool Stale, string? Origin, bool OriginChanged);

/// <summary>Revision is null from the Assets tab (no editor open); Target or PrefabRef names the species, Skin gives a skin its own model.</summary>
public sealed record ModReplaceModelParams(string Id, string File, string? Revision = null, string? Target = null, string? PrefabRef = null, string? Skin = null);

public sealed record ModRemoveModelParams(string Id, string Revision, string Target, string? Skin = null);

public sealed record ModRebuildModelsParams(string Id, string Revision);

public sealed record ModModelPreviewParams(string Id, string Target, string? Skin = null);

public sealed record ModModelPreviewLod(string File, int Vertices);

/// <summary>A replacement model as .glb files (one per LOD) on the game's skeleton, and the materials the viewer dresses.</summary>
public sealed record ModModelPreview(string PrefabRef, IReadOnlyList<ModModelPreviewLod> Lods, IReadOnlyList<Tyrant.Rpc.Assets.PreviewMaterial> Materials);

public sealed record ModReplacementDto(string Texture, string? Key, string? Guid, string File);

/// <summary>A whole mod for the editor. Revision: SHA-256 of mod.json, sent back with every change; ManifestJson: the file itself (undo).</summary>
public sealed record ModDetail(string Id, string Name, string Version, string? Author, string? Description, string Dir, string Revision,
    string ManifestJson, IReadOnlyList<ModReplacementDto> Replace, IReadOnlyList<ModSkinDto> Skins, IReadOnlyList<ModModelDto> Models);

public sealed record ModSetDetailsParams(string Id, string Revision, string Name, string Version, string? Author = null, string? Description = null);

public sealed record ModRenameSkinParams(string Id, string Revision, string Skin, string Name);

public sealed record ModRemoveSkinParams(string Id, string Revision, string Skin, bool DeleteFiles = false);

/// <summary>Colors: the skin's "colors" JSON, or null to remove them.</summary>
public sealed record ModSetColorsParams(string Id, string Revision, string Skin, string? Colors = null);

/// <summary>Png null: use the base skin's texture for that slot.</summary>
public sealed record ModSetSkinFileParams(string Id, string Revision, string Skin, string Sex, string Slot, string? Png = null);

public sealed record ModSetThumbnailParams(string Id, string Revision, string Skin, string? Png = null);

public sealed record ModRemoveReplacementParams(string Id, string Revision, string Texture);

public sealed record ModSaveManifestParams(string Id, string Revision, string Manifest);

/// <summary>Variant: normal, albino, melanistic or leucistic. Colors null: the skin's saved colours.</summary>
public sealed record ModColorPreviewParams(string Id, string Skin, string? Colors = null, string Variant = "normal", string Sex = "male", int Seed = 1, int Count = 6, int Size = 256);

public sealed record ModPreviewFiles(IReadOnlyList<string> Files);

public sealed record ModSampleColorsParams(string Id, string Skin, string? Colors = null, string Variant = "normal", int Seed = 1);

/// <summary>One animal's colours (#rrggbb, null = keep the texture), the same as the 2D strip's first animal with that seed.</summary>
public sealed record ModSampledColors(string? A, string? B, string? Secondary, string? Eye, float Strength, float Softness, float Hue, float Saturation, float Value);

/// <summary>Sex: male, female or infant.</summary>
public sealed record ModSkinModelParams(string Id, string Skin, string Sex = "male");

/// <summary>The species prefab to preview and the skin's maps (diffuse, normal, extra, pattern) as PNG files.</summary>
public sealed record ModSkinModel(string PrefabRef, IReadOnlyDictionary<string, string> Maps);

public sealed record ModThumbnailParams(string Id, string File, int Size = 96);

public sealed record ModThumbnailResult(string? File);
