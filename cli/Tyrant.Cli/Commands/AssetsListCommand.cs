using System.ComponentModel;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class AssetsListCommand : Command<AssetsListCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandOption("--type <TYPE>")]
        [Description("Only this Unity type, e.g. Texture2D, Mesh, MonoBehaviour.")]
        public string? Type { get; set; }

        [CommandOption("--filter <TEXT>")]
        [Description("Case-insensitive text to match in name, path, GUID, script or bundle.")]
        public string? Filter { get; set; }

        [CommandOption("--limit <N>")]
        [Description("Maximum rows to print (default 50).")]
        public int Limit { get; set; } = 50;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var matches = CliServices.LoadIndex(ws, install).Query(settings.Type, settings.Filter).ToList();
        foreach (var a in matches.Take(Math.Max(settings.Limit, 0)))
        {
            var label = a.ContainerPath ?? (a.Script is null ? a.Name : $"{a.Name} [{a.Script}]");
            Console.WriteLine($"{a.Type,-16} {label}  {a.Guid ?? "-"}  {a.Ref}");
        }
        Console.WriteLine(matches.Count == 0 ? "No assets match." : $"Showing {Math.Min(matches.Count, Math.Max(settings.Limit, 0))} of {matches.Count} matching assets.");
        return ExitCodes.Ok;
    }
}
