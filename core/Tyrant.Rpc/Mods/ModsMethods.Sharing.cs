using Tyrant.Core.Errors;
using Tyrant.Core.Jobs;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Core.Install;
using Tyrant.Framework.Core;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

/// <summary>mods.* for sharing: export a mod as a zip, import one, and bring looks made in the game back into the workspace.</summary>
public sealed partial class ModsMethods
{
    [RpcMethod("mods.export", JobResult = typeof(ModExportResult))]
    public JobStarted Export(ModExportParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id); // an unknown id fails before the job starts
        return jobs.Start($"Export {p.Id}", (progress, ct) =>
        {
            progress.Report(new JobProgress(0.1, $"Checking {p.Id}"));
            RebuildStaleModels(ws, install, mod, measureHeadRigs: true);
            var check = CheckMod(ws, install, mod);
            if (!check.Ok)
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{p.Id}' has problems, so it was not exported: {string.Join(" ", check.Errors)}");
            progress.Report(new JobProgress(0.6, $"Writing {p.Id}'s zip"));
            var cooker = Cooker(ws, install, mod);
            var path = ModSharing.Export(mod, p.Out ?? Path.Combine(ws.Dir, "exports", $"{mod.Id}-{mod.Manifest.Version}.zip"), FrameworkInfo.Version,
                dir => cooker.WritePack(mod.Manifest, mod.Dir, dir, Cooking(progress, 0.6, 0.95), ct), p.IncludePngs);
            session.Log($"Exported '{p.Id}' to {path}.");
            return new ModExportResult(path, check.Warnings);
        });
    }

    [RpcMethod("mods.import")]
    public ModImportResult Import(ModImportParams p)
    {
        var (ws, install) = session.Current();
        var imported = ModSharing.ImportReported(ws, p.File, p.Replace);
        session.Log($"Imported '{imported.Mod.Id}' from {Path.GetFileName(p.File)}.");
        return Imported(ws, install, imported);
    }

    /// <summary>Copy to workspace (Mods tab): a mod only in the game folder, its PNGs extracted from its pack if it has none.</summary>
    [RpcMethod("mods.copyFromGame")]
    public ModImportResult CopyFromGame(ModCopyFromGameParams p)
    {
        var (ws, install) = session.Current();
        var imported = ModSharing.CopyFromGame(ws, install, p.Id, p.Replace);
        session.Log($"Copied '{imported.Mod.Id}' from the game into the workspace.");
        return Imported(ws, install, imported);
    }

    /// <summary>The material folders the in-game editor or the workspace changed since install (the app asks before install, remove, delete and uninstall).</summary>
    [RpcMethod("mods.looksChanges")]
    public ModLooksChanges LooksChanges(ModIdParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        try
        {
            return new ModLooksChanges(GameLooks.Changes(install, mod).Select(c => new ModLooksChangeDto(c.Folder, c.GameChanged, c.ProjectChanged)).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TyrantException(TyrantErrorCode.GameLooksPending,
                $"Tyrant could not compare '{p.Id}' in the game with the workspace, so it cannot tell whether looks made in the game would be lost ({ex.Message}). Close the game or the program holding the file and try again.",
                FixAction.None, ex);
        }
    }

    /// <summary>Replaces these material folders in the workspace with the game's copy (the rest of the mod stays as it is).</summary>
    [RpcMethod("mods.bringInLooks")]
    public ModDetail BringInLooks(ModBringInLooksParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var brought = GameLooks.BringIn(install, mod, p.Folders);
        session.Log($"'{p.Id}': brought {brought} look folder(s) in from the game ({string.Join(", ", p.Folders)}).");
        return DetailOf(ws, mod);
    }

    private ModImportResult Imported(Workspace ws, GameInstall install, ImportedMod imported)
    {
        var check = CheckMod(ws, install, imported.Mod);
        return new ModImportResult(imported.Mod.Id, imported.Warnings.Concat(check.Errors).Concat(check.Warnings).ToList(), ListOf(ws, install), imported.Extracted);
    }
}
