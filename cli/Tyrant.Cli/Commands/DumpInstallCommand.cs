using System.ComponentModel;
using Tyrant.Core.Dumping;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class DumpInstallCommand : Command<DumpInstallCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--loader-zip <PATH>")]
        [Description("Use an already-downloaded MelonLoader.x64.zip (0.7.3) instead of downloading it.")]
        public string? LoaderZip { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var zip = settings.LoaderZip;
        var before = ModLoaderInstaller.GetState(install);
        var installer = new ModLoaderInstaller(isGameRunning: CliServices.Launcher.IsRunning);
        // Only a fresh install needs the MelonLoader archive; an existing MelonLoader just gets the mod.
        if (zip is null && before == InstallState.NotInstalled)
        {
            Console.WriteLine($"Downloading {ModLoaderInstaller.LoaderName} {ModLoaderInstaller.Version} ...");
            using var http = new HttpClient();
            zip = ModLoaderInstaller.DownloadAsync(http, Path.Combine(ws.CacheDir, "downloads"), CancellationToken.None).GetAwaiter().GetResult();
        }

        var record = installer.Install(install, zip, Path.Combine(AppContext.BaseDirectory, "dumper"));
        Console.WriteLine(ModLoaderInstaller.InstallSummary(before, record));
        Console.WriteLine("Normal play is unaffected: the dumper does nothing unless 'tyrant dump run' asks it to. Undo with 'tyrant dump uninstall'.");
        return ExitCodes.Ok;
    }
}
