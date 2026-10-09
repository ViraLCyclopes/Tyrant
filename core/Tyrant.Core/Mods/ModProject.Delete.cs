using Tyrant.Core.Blender;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed partial class ModProject
{
    /// <summary>
    /// Deletes a mod from the workspace. With <paramref name="removeFromGame"/> its installed copy goes first (refused while the game
    /// runs, before anything moves). Then its folder, and its Blender projects, go to the Recycle Bin. Its skin numbers stay reserved,
    /// so saved animals keep their skins if it comes back. A broken mod.json does not stop it. Returns what went to the Recycle Bin
    /// and what stayed, with why.
    /// </summary>
    public static RemovedFiles Delete(Workspace ws, GameInstall install, string id, bool removeFromGame, GameMods game)
    {
        var dir = Path.Combine(RootOf(ws), id);
        if (!ModId.IsValid(id) || !File.Exists(Path.Combine(dir, ModManifest.FileName)))
            throw new TyrantException(TyrantErrorCode.ModNotFound, $"There is no mod '{id}' in this workspace ({RootOf(ws)}).");
        if (removeFromGame)
        {
            try
            {
                game.Remove(install, id);
            }
            catch (TyrantException ex) when (ex.Code == TyrantErrorCode.ModNotFound)
            {
                // not installed: nothing to remove
            }
        }
        try
        {
            RecycleBin.Current.Recycle(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TyrantException(TyrantErrorCode.DeleteFailed,
                $"'{id}' could not go to the Recycle Bin; is one of its files open in another program? Close it and delete again. ({ex.Message})",
                FixAction.None, ex);
        }
        var recycled = new List<string> { dir };
        var kept = new List<string>();
        var blender = BlenderProjectWriter.ModFolderOf(ws.Dir, id);
        if (Directory.Exists(blender))
        {
            try
            {
                RecycleBin.Current.Recycle(blender);
                recycled.Add(blender);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                kept.Add($"{blender}: {ex.Message}");
            }
        }
        return new RemovedFiles(recycled, kept);
    }
}
