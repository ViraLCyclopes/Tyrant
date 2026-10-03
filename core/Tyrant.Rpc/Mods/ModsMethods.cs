using Tyrant.Core.Assets;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Mods;

/// <summary>mods.* — make mods in the workspace, check them, and install, switch or remove them in the game.</summary>
public sealed class ModsMethods(StudioSession session, JobManager jobs)
{
    private StudioOptions Options => session.Options;

    private GameMods Game => new(install => Options.Launcher.IsRunning(install));

    [RpcMethod("mods.list")]
    public ModsListResult List()
    {
        var (ws, install) = session.Current();
        return ListOf(ws, install);
    }

    [RpcMethod("mods.create")]
    public ModsListResult Create(ModCreateParams p)
    {
        var (ws, install) = session.Current();
        ModProject.Create(ws, p.Id, p.Name, p.Author);
        session.Log($"Created mod '{p.Id}'.");
        return ListOf(ws, install);
    }

    [RpcMethod("mods.replace")]
    public ModsListResult Replace(ModReplaceParams p)
    {
        var (ws, install) = session.Current();
        var entry = ModProject.Open(ws, p.Id).Replace(ws, AssetIndex.Load(AssetIndex.PathIn(ws)), p.Texture, p.Png);
        session.Log($"'{p.Id}' replaces {entry.Texture} with {entry.File}.");
        return ListOf(ws, install);
    }

    [RpcMethod("mods.check")]
    public ModCheckReport Check(ModIdParams p)
    {
        var (ws, install) = session.Current();
        var result = ModChecker.ForGame(install).Check(ModProject.Open(ws, p.Id), TryIndex(ws));
        return new ModCheckReport(result.Errors, result.Warnings);
    }

    [RpcMethod("mods.install", JobResult = typeof(ModInstallResult))]
    public JobStarted Install(ModIdParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id); // an unknown id fails before the job starts
        return jobs.Start($"Install {p.Id}", (progress, ct) =>
        {
            progress.Report(new JobProgress(0.05, $"Checking {p.Id}"));
            var check = ModChecker.ForGame(install).Check(mod, TryIndex(ws));
            if (!check.Ok)
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{p.Id}' has problems, so it was not installed: {string.Join(" ", check.Errors)}");
            if (ModLoaderInstaller.FrameworkStatus(install, Options.DumperDir) != FrameworkState.Current)
            {
                string? zip = null;
                if (ModLoaderInstaller.GetState(install) == InstallState.NotInstalled)
                {
                    progress.Report(new JobProgress(0.2, $"Downloading {ModLoaderInstaller.LoaderName} {ModLoaderInstaller.Version}"));
                    zip = Options.LoaderZip(ws, ct);
                }
                progress.Report(new JobProgress(0.5, "Installing or updating Tyrant's framework in the game"));
                Options.Installer().Install(install, zip, Options.DumperDir);
            }
            progress.Report(new JobProgress(0.8, $"Installing {p.Id}"));
            Game.Install(install, mod);
            session.Log($"Installed '{p.Id}' into the game.");
            return new ModInstallResult($"Installed '{p.Id}' into the game. Start the game to see it.", check.Warnings);
        });
    }

    [RpcMethod("mods.remove")]
    public ModsListResult Remove(ModIdParams p)
    {
        var (ws, install) = session.Current();
        Game.Remove(install, p.Id);
        session.Log($"Removed '{p.Id}' from the game.");
        return ListOf(ws, install);
    }

    [RpcMethod("mods.enable")]
    public ModsListResult Enable(ModEnableParams p)
    {
        var (ws, install) = session.Current();
        Game.SetEnabled(install, p.Id, p.Enabled);
        return ListOf(ws, install);
    }

    private ModsListResult ListOf(Workspace ws, GameInstall install)
    {
        var game = Game;
        var installed = game.List(install).ToDictionary(m => m.Id);
        var rows = new List<ModRow>();
        foreach (var id in ModProject.Ids(ws))
        {
            installed.TryGetValue(id, out var inGame);
            try
            {
                var mod = ModProject.Open(ws, id);
                rows.Add(new ModRow(id, mod.Manifest.Name, mod.Manifest.Version, mod.Manifest.Author, mod.Manifest.Replace.Count,
                    Wire(game.StateOf(install, mod)), inGame?.Enabled, mod.Dir, null));
            }
            catch (TyrantException ex)
            {
                rows.Add(new ModRow(id, id, "", null, 0, "notInstalled", inGame?.Enabled, Path.Combine(ModProject.RootOf(ws), id), ex.Message));
            }
            installed.Remove(id);
        }
        rows.AddRange(installed.Values.Select(m => new ModRow(m.Id, m.Name, m.Version, null, m.Replacements, "gameOnly", m.Enabled, m.Dir, m.Error)));
        return new ModsListResult(rows, ModLoaderInstaller.HasFramework(install));
    }

    private static string Wire(ModInstallState state) => state switch
    {
        ModInstallState.Installed => "installed",
        ModInstallState.Changed => "changed",
        _ => "notInstalled",
    };

    private static AssetIndex? TryIndex(Workspace ws)
    {
        try
        {
            return AssetIndex.Load(AssetIndex.PathIn(ws));
        }
        catch (TyrantException)
        {
            return null;
        }
    }
}
