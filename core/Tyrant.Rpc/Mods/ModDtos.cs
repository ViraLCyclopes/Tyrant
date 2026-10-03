namespace Tyrant.Rpc.Mods;

/// <summary>
/// A mod in the workspace or the game. State: notInstalled, installed, changed (edited since install) or gameOnly
/// (installed, but not in this workspace). Enabled is null when the mod is not installed.
/// </summary>
public sealed record ModRow(string Id, string Name, string Version, string? Author, int Replacements, string State, bool? Enabled, string? Dir, string? Error);

public sealed record ModsListResult(IReadOnlyList<ModRow> Mods, bool FrameworkInstalled);

public sealed record ModCreateParams(string Id, string? Name = null, string? Author = null);

/// <summary>Texture: name, Addressables path, GUID or ref. Png: null uses the texture's exported PNG.</summary>
public sealed record ModReplaceParams(string Id, string Texture, string? Png = null);

public sealed record ModIdParams(string Id);

public sealed record ModEnableParams(string Id, bool Enabled);

public sealed record ModCheckReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record ModInstallResult(string Message, IReadOnlyList<string> Warnings);
