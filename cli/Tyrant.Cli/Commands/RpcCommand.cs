using System.ComponentModel;
using Spectre.Console.Cli;
using Tyrant.Core.Dumping;
using Tyrant.Core.Install;
using Tyrant.Rpc;
using Tyrant.Rpc.Studio;
using Tyrant.Rpc.TypeScript;

namespace Tyrant.Cli.Commands;

public sealed class RpcCommand : Command<RpcCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--emit-ts <PATH>")]
        [Description("Write the protocol's TypeScript declarations to PATH and exit (used by the app's build).")]
        public string? EmitTs { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var options = ProductionOptions();
        if (settings.EmitTs is not null)
        {
            var (server, _) = RpcHost.Build(options, _ => { }, TextWriter.Null);
            var path = Path.GetFullPath(settings.EmitTs);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, TsEmitter.Emit(server.Methods));
            Console.WriteLine($"Wrote {path}");
            return ExitCodes.Ok;
        }
        RpcHost.RunConsoleAsync(options).GetAwaiter().GetResult();
        return ExitCodes.Ok;
    }

    internal static StudioOptions ProductionOptions()
    {
        var launcher = new SteamLauncher();
        return new StudioOptions
        {
            Locator = new GameInstallLocator(new RegistrySteamRootProvider()),
            Launcher = launcher,
            DumperDir = Path.Combine(AppContext.BaseDirectory, "dumper"),
            LoaderZip = (ws, ct) =>
            {
                using var http = new HttpClient();
                return ModLoaderInstaller.DownloadAsync(http, Path.Combine(ws.CacheDir, "downloads"), ct).GetAwaiter().GetResult();
            },
            Installer = () => new ModLoaderInstaller(isGameRunning: launcher.IsRunning),
        };
    }
}
