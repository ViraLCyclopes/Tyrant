using PK.Cli.Commands;
using PK.Core.Errors;
using Spectre.Console.Cli;

namespace PK.Cli;

public static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int PkError = 2;
    public const int Partial = 3;
}

public static class CliApp
{
    public static int Run(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("pk");
            config.PropagateExceptions();
            config.AddBranch("install", install =>
            {
                install.AddCommand<InstallDetectCommand>("detect")
                    .WithDescription("Find the Prehistoric Kingdom install and print its build fingerprint.");
            });
            config.AddBranch("workspace", workspace =>
            {
                workspace.AddCommand<WorkspaceInitCommand>("init")
                    .WithDescription("Create a workspace folder (must be outside the game folder).");
                workspace.AddCommand<WorkspaceStatusCommand>("status")
                    .WithDescription("Show the workspace's game, build and output freshness.");
            });
            config.AddCommand<DecompileCommand>("decompile")
                .WithDescription("Decompile game assemblies into <workspace>/source.");
        });

        try
        {
            return app.Run(args);
        }
        catch (PkException ex)
        {
            CliServices.PrintError(ex);
            return ExitCodes.PkError;
        }
        catch (CommandAppException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // File-system and bad-path problems are user-facing; anything else is a bug and keeps its stack trace.
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitCodes.PkError;
        }
    }
}
