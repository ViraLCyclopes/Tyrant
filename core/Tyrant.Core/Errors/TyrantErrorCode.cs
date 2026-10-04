using System.Text.RegularExpressions;

namespace Tyrant.Core.Errors;

public enum TyrantErrorCode
{
    GameNotFound,
    WorkspaceInvalid,
    WorkspaceInGameFolder,
    WorkspaceStale,
    DecompileFailed,
    CatalogInvalid,
    AssetIndexMissing,
    AssetNotFound,
    AssetAmbiguous,
    OutputInGameFolder,
    AssetUnreadable,
    DumperNotInstalled,
    DumperInstallFailed,
    ModLoaderConflict,
    DumpTimeout,
    DumpFailed,
    DataMissing,
    JobRunning,
    ModNotFound,
    ModInvalid,
    ModIdInvalid,
    ModChanged,
    GameRunning,
    FrameworkMissing,
    TargetNotFound,
}

/// <summary>A suggested remedy the UI can render as a button.</summary>
public enum FixAction
{
    None,
    PickGameFolder,
    PickWorkspaceFolder,
    RefreshWorkspace,
    InstallDumper,
    ReindexAssets,
}

public static class TyrantErrorCodeExtensions
{
    /// <summary>Stable wire form used by the CLI and the RPC protocol, e.g. GAME_NOT_FOUND.</summary>
    public static string ToWire(this TyrantErrorCode code) => Snake(code.ToString());

    /// <summary>Wire form of a fix action, e.g. PICK_GAME_FOLDER; null when there is no fix.</summary>
    public static string? ToWire(this FixAction fix) => fix == FixAction.None ? null : Snake(fix.ToString());

    private static string Snake(string name) => Regex.Replace(name, "(?<!^)([A-Z])", "_$1").ToUpperInvariant();
}
