using System.ComponentModel;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class AssetsDumpCommand : Command<AssetsDumpCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<KEY>")]
        [Description("Asset ref (bundle#pathId), catalog GUID, or container path.")]
        public string Key { get; set; } = "";

        [CommandOption("--type <TYPE>")]
        [Description("Unity type to pick when the key matches several objects.")]
        public string? Type { get; set; }

        [CommandOption("--out <FILE>")]
        [Description("Write the JSON to a file instead of the console.")]
        public string? Out { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        if (settings.Out is not null && install.ContainsPath(settings.Out))
            throw new TyrantException(TyrantErrorCode.OutputInGameFolder,
                $"Refusing to write '{settings.Out}' inside the game folder. Mods and tools never write game files.");

        var asset = CliServices.LoadIndex(ws, install).Resolve(settings.Key, settings.Type);
        using var session = new AssetSession(install);
        var (_, baseField) = session.Open(asset);
        var json = FieldJsonWriter.ToJson(baseField);

        if (settings.Out is null)
        {
            Console.WriteLine(json);
        }
        else
        {
            var full = Path.GetFullPath(settings.Out);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, json);
            Console.WriteLine($"{asset.Type} {asset.Ref} -> {full}");
        }
        return ExitCodes.Ok;
    }
}
