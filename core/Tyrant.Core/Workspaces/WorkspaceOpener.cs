using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Workspaces;

public static class WorkspaceOpener
{
    /// <summary>
    /// Opens a workspace and its game install. A <paramref name="gamePath"/> override re-points (and saves) a workspace
    /// whose game folder moved; a workspace inside the game folder is refused.
    /// </summary>
    public static (Workspace Workspace, GameInstall Install, bool Repointed) Open(string dir, string? gamePath, GameInstallLocator locator)
    {
        var ws = Workspace.Open(dir);
        var install = locator.FromPath(gamePath ?? ws.Data.GameRoot);
        if (install.ContainsPath(ws.Dir))
            throw new TyrantException(TyrantErrorCode.WorkspaceInGameFolder,
                $"The workspace '{ws.Dir}' is inside the game folder. Mods and tools never write game files; move it elsewhere.",
                FixAction.PickWorkspaceFolder);
        var repointed = gamePath is not null && !string.Equals(ws.Data.GameRoot, install.RootDir, StringComparison.OrdinalIgnoreCase);
        if (repointed) ws.SetGameRoot(install.RootDir);
        return (ws, install, repointed);
    }
}
