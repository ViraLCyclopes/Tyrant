using System.ComponentModel;
using Tyrant.Core.Dumping;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

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
        // Ctrl+C stops waiting cleanly, so the request file is removed and the next normal game start stays idle.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;
        DumpManifestFile manifest;
        try
        {
            manifest = new DumpRunner(new SteamLauncher())
                .Run(install, ws, TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds, 1)), new ConsoleProgress(), cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled; the dump request was withdrawn. Close the game if it is still starting.");
            return ExitCodes.Error;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        Console.WriteLine($"Dumped {manifest.Counts.Values.Sum()} objects of {manifest.Counts.Count} types and {manifest.Languages.Count} languages -> {ws.DataDir}");
        foreach (var (type, count) in manifest.Counts.OrderByDescending(kv => kv.Value).Take(10))
            Console.WriteLine($"  {count,6}  {type}");
        foreach (var error in manifest.Errors.Take(10)) Console.WriteLine($"  WARN  {error}");
        if (manifest.Errors.Count > 10) Console.WriteLine($"  ... and {manifest.Errors.Count - 10} more warnings");
        return manifest.Errors.Count == 0 ? ExitCodes.Ok : ExitCodes.Partial;
    }
}
