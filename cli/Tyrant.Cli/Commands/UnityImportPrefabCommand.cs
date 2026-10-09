using Spectre.Console.Cli;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;

namespace Tyrant.Cli.Commands;

public sealed class UnityImportPrefabCommand : Command<UnityImportPrefabCommand.Settings>
{
    public sealed class Settings : WorkspaceSettings
    {
        [CommandArgument(0, "<MOD>")] public string Mod { get; set; } = "";
        [CommandArgument(1, "<SPECIES>")] public string Species { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, install) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Mod);
        var species = mod.Manifest.Species.FirstOrDefault(s => s.Id == settings.Species)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + settings.Species);
        var template = SpeciesSkinsReader.Load(ws).FirstOrDefault(s => s.SpeciesId == species.Base);
        if (template?.PrefabGuid is null) throw new TyrantException(TyrantErrorCode.AssetNotFound, "The template has no indexed animal prefab.");
        var index = CliServices.LoadIndex(ws, install);
        var prefab = index.Resolve(template.PrefabGuid, "GameObject");
        var project = UnitySpeciesProject.Create(ws, mod.Id, Path.Combine(AppContext.BaseDirectory, "unity", "SpeciesAuthoring"));
        var result = UnityPrefabImport.Run(ws, install, index, prefab, project, species.Id, species.Name,
            new Progress<Tyrant.Core.Jobs.JobProgress>(p => Console.WriteLine(p.Message)));
        Console.WriteLine("Imported prefab: " + result.Prefab);
        Console.WriteLine("Unity project: " + result.Directory);
        Console.WriteLine("Report: " + result.Report);
        return ExitCodes.Ok;
    }
}
