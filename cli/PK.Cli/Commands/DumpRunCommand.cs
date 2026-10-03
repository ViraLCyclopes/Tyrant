using System.ComponentModel;
using PK.Core.Dumping;
using Spectre.Console.Cli;

namespace PK.Cli.Commands;

public sealed class DumpRunCommand : Command<DumpRunCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--timeout <SECONDS>")]
        [Description("How long to wait for the game to start and dump (default 300).")]
        public int TimeoutSeconds { get; set; } = 300;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        Console.WriteLine("Starting Prehistoric Kingdom through Steam; it will close by itself when the dump is done.");
        var manifest = new DumpRunner(new SteamLauncher())
            .Run(install, ws, TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds, 1)), new ConsoleProgress(), CancellationToken.None);

        Console.WriteLine($"Dumped {manifest.Counts.Values.Sum()} objects of {manifest.Counts.Count} types and {manifest.Languages.Count} languages -> {ws.DataDir}");
        foreach (var (type, count) in manifest.Counts.OrderByDescending(kv => kv.Value).Take(10))
            Console.WriteLine($"  {count,6}  {type}");
        foreach (var error in manifest.Errors.Take(10)) Console.WriteLine($"  WARN  {error}");
        if (manifest.Errors.Count > 10) Console.WriteLine($"  ... and {manifest.Errors.Count - 10} more warnings");
        return manifest.Errors.Count == 0 ? ExitCodes.Ok : ExitCodes.Partial;
    }
}
