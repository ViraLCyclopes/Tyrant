using System.ComponentModel;
using PK.Core.Decompile;
using Spectre.Console.Cli;

namespace PK.Cli.Commands;

public sealed class DecompileCommand : Command<DecompileCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--assemblies <NAMES>")]
        [Description("Comma-separated assembly names without .dll (default: game code assemblies).")]
        public string? Assemblies { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings.Workspace);
        var names = settings.Assemblies?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            var result = new DecompileService().DecompileGame(install, ws, names, new ConsoleProgress(), cts.Token);
            foreach (var r in result.Assemblies)
                Console.WriteLine(r.Success ? $"  ok    {r.AssemblyName} -> {r.OutputDir}" : $"  FAIL  {r.AssemblyName}: {r.Error}");
            return result.AllSucceeded ? ExitCodes.Ok : ExitCodes.Partial;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return ExitCodes.Partial;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
