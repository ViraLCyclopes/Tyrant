using Tyrant.Core.Errors;
using Tyrant.Core.Jobs;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

/// <summary>mods.* for sharing: export a mod as a zip, import one.</summary>
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
            RebuildStaleModels(ws, install, mod);
            var check = CheckMod(ws, install, mod);
            if (!check.Ok)
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{p.Id}' has problems, so it was not exported: {string.Join(" ", check.Errors)}");
            progress.Report(new JobProgress(0.6, $"Writing {p.Id}'s zip"));
            var path = ModSharing.Export(mod, p.Out ?? Path.Combine(ws.Dir, "exports", $"{mod.Id}-{mod.Manifest.Version}.zip"), FrameworkInfo.Version);
            session.Log($"Exported '{p.Id}' to {path}.");
            return new ModExportResult(path, check.Warnings);
        });
    }

    [RpcMethod("mods.import")]
    public ModImportResult Import(ModImportParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModSharing.Import(ws, p.File, p.Replace);
        var check = CheckMod(ws, install, mod);
        session.Log($"Imported '{mod.Id}' from {Path.GetFileName(p.File)}.");
        return new ModImportResult(mod.Id, check.Errors.Concat(check.Warnings).ToList(), ListOf(ws, install));
    }
}
