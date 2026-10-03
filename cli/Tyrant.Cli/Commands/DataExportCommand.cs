using System.ComponentModel;
using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class DataExportCommand : Command<DataExportCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<TYPE>")]
        [Description("Data type, full or short name (see 'tyrant data types').")]
        public string Type { get; set; } = "";

        [CommandOption("--format <FORMAT>")]
        [Description("csv (default) or json.")]
        public string Format { get; set; } = "csv";

        [CommandOption("--out <FILE>")]
        [Description("Output file (default <workspace>/exports/<Type>.<format>).")]
        public string? Out { get; set; }

        public override Spectre.Console.ValidationResult Validate() =>
            Format is "csv" or "json" ? Spectre.Console.ValidationResult.Success() : Spectre.Console.ValidationResult.Error("--format must be csv or json");
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        if (settings.Out is not null && install.ContainsPath(settings.Out))
            throw new TyrantException(TyrantErrorCode.OutputInGameFolder, $"Refusing to write '{settings.Out}' inside the game folder.");

        var store = DataStore.Open(ws);
        var type = store.FindType(settings.Type);
        var output = Path.GetFullPath(settings.Out ?? Path.Combine(ws.Dir, "exports", $"{type.ShortName}.{settings.Format}"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var objects = store.LoadAll(type).ToList();
        if (settings.Format == "csv")
            File.WriteAllText(output, DataStore.ToCsv(objects), DataStore.CsvEncoding);
        else
            File.WriteAllText(output, JsonSerializer.Serialize(objects.Select(o => o.Root), DataStore.ReadableJson));
        Console.WriteLine($"Exported {objects.Count} {type.ShortName} objects -> {output}");
        return ExitCodes.Ok;
    }
}
