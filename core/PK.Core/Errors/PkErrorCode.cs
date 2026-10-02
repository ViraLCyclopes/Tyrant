using System.Text.RegularExpressions;

namespace PK.Core.Errors;

public enum PkErrorCode
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
}

/// <summary>A suggested remedy the UI can render as a button.</summary>
public enum FixAction
{
    None,
    PickGameFolder,
    PickWorkspaceFolder,
    RefreshWorkspace,
}

public static class PkErrorCodeExtensions
{
    /// <summary>Stable wire form used by the CLI and the RPC protocol, e.g. GAME_NOT_FOUND.</summary>
    public static string ToWire(this PkErrorCode code) =>
        Regex.Replace(code.ToString(), "(?<!^)([A-Z])", "_$1").ToUpperInvariant();
}
