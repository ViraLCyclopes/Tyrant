using System.ComponentModel;
using System.Text.Json;
using Tyrant.Core.Data;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class DataShowCommand : Command<DataShowCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<TYPE>")]
        [Description("Data type, full or short name (see 'tyrant data types').")]
        public string Type { get; set; } = "";

        [CommandArgument(1, "[NAME]")]
        [Description("Object name; omit to list the objects of the type.")]
        public string? Name { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var store = DataStore.Open(ws);
        var type = store.FindType(settings.Type);
        if (settings.Name is null)
        {
            foreach (var name in store.ObjectNames(type)) Console.WriteLine(name);
            return ExitCodes.Ok;
        }
        Console.WriteLine(JsonSerializer.Serialize(store.Load(type, settings.Name), DataStore.ReadableJson));
        return ExitCodes.Ok;
    }
}
