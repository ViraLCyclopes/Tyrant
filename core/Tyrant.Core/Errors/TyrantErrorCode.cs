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
}

/// <summary>A suggested remedy the UI can render as a button.</summary>
public enum FixAction
{
    None,
    PickGameFolder,
    PickWorkspaceFolder,
    RefreshWorkspace,
}

public static class TyrantErrorCodeExtensions
{
    /// <summary>Stable wire form used by the CLI and the RPC protocol, e.g. GAME_NOT_FOUND.</summary>
    public static string ToWire(this TyrantErrorCode code) =>
        Regex.Replace(code.ToString(), "(?<!^)([A-Z])", "_$1").ToUpperInvariant();
}
