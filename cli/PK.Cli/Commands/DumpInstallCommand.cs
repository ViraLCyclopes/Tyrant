using System.ComponentModel;
using PK.Core.Dumping;
using Spectre.Console.Cli;

namespace PK.Cli.Commands;

public sealed class DumpInstallCommand : Command<DumpInstallCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--bepinex-zip <PATH>")]
        [Description("Use an already-downloaded BepInEx_win_x64_5.4.23.5.zip instead of downloading it.")]
        public string? BepInExZip { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var zip = settings.BepInExZip;
        if (zip is null)
        {
            Console.WriteLine($"Downloading BepInEx {BepInExInstaller.Version} ...");
            using var http = new HttpClient();
            zip = BepInExInstaller.DownloadAsync(http, Path.Combine(ws.CacheDir, "downloads"), CancellationToken.None).GetAwaiter().GetResult();
        }
        var record = new BepInExInstaller().Install(install, zip, Path.Combine(AppContext.BaseDirectory, "dumper"));
        Console.WriteLine(record.InstalledBepInEx
            ? $"Installed BepInEx {BepInExInstaller.Version} and the dumper ({record.Files.Count} files added to the game folder)."
            : "Added the dumper to your existing BepInEx.");
        Console.WriteLine("Normal play is unaffected: the dumper does nothing unless 'pk dump run' asks it to. Undo with 'pk dump uninstall'.");
        return ExitCodes.Ok;
    }
}
