using Tyrant.Core.Data;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class DataTypesCommand : Command<WorkspaceSettings>
{
    public override int Execute(CommandContext context, WorkspaceSettings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var store = DataStore.Open(ws);
        foreach (var type in store.Types()) Console.WriteLine($"{type.Count,6}  {type.ShortName,-36} {type.FullName}");
        Console.WriteLine($"Dump of game build {store.Manifest.BuildGuid} from {store.Manifest.CreatedUtc}.");
        return ExitCodes.Ok;
    }
}
