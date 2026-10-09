using Spectre.Console.Cli;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

public sealed class UnityPrepareCommand : Command<UnityPrepareCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--project <PATH>")] public string Project { get; set; } = "";
        [CommandOption("--game <PATH>")] public string Game { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (!File.Exists(Path.Combine(settings.Project, "ProjectSettings", "ProjectVersion.txt")) || string.IsNullOrWhiteSpace(settings.Game))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Choose a Tyrant Unity project with --project and a PK game folder with --game.");
        UnityAuthoringReferences.Prepare(Path.GetFullPath(settings.Project), Path.Combine(Path.GetFullPath(settings.Game), "Prehistoric Kingdom_Data", "Managed"));
        Console.WriteLine("Prepared local PK authoring references. Open the project with Unity 2022.3.62f3.");
        return ExitCodes.Ok;
    }
}
