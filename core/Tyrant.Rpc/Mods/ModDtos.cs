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
