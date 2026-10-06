using Spectre.Console.Cli;
using Tyrant.Core.Updates;

namespace Tyrant.Cli.Commands;

/// <summary>Checks GitHub for a newer Tyrant; the app installs updates (Help → Check for updates).</summary>
public sealed class UpdateCheckCommand : Command
{
    public override int Execute(CommandContext context)
    {
        using var http = CliServices.Http();
        var checker = new UpdateChecker(http, UpdateChecker.CurrentVersion);
        Console.WriteLine($"This Tyrant: {UpdateChecker.CurrentVersion}");
        var release = checker.LatestGitHubReleaseAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (release is null)
        {
            Console.WriteLine($"No release is published yet ({UpdateSources.ReleasesPage}).");
            return ExitCodes.Ok;
        }
        Console.WriteLine($"GitHub: {release.Version} ({release.Page})");
        if (SemVer.Compare(release.Version, UpdateChecker.CurrentVersion) <= 0)
        {
            Console.WriteLine("You have the latest Tyrant.");
            return ExitCodes.Ok;
        }
        Console.WriteLine($"Tyrant {release.Version} is available: {release.SetupUrl ?? release.Page}");
        Console.WriteLine("Install it from the app (Help → Check for updates → Update now) or run the Setup.");
        return ExitCodes.Partial;
    }
}
