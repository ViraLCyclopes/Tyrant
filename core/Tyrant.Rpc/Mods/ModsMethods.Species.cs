using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Jobs;

namespace Tyrant.Rpc.Mods;

public sealed record ModAnimalDto(string Id, string Key, string Base, string Name, string Genus, string Epithet,
    int? Cost, IReadOnlyDictionary<string, double> Stats, int? InheritedCost, IReadOnlyDictionary<string, double> InheritedStats,
    IReadOnlyList<ModAnimalFieldDto> DataFields, IReadOnlyDictionary<string, string> Localization, string? Icon, SpeciesPrefab? Prefab);
public sealed record ModAnimalFieldDto(string Path, string Kind, string Value, string TemplateValue, bool Editable);
public sealed record ModSetSpeciesIconParams(string Id, string Revision, string SpeciesId, string? Png = null);
public sealed record ModSetSpeciesPrefabParams(string Id, string Revision, string SpeciesId, string? Bundle = null, string? Asset = null);
public sealed record ModUnityProjectDto(string Directory, string Readme);
public sealed record ModStatDto(string Id, string Label, string Unit, double Min, double Max);
public sealed record ModAnimalDatabase(IReadOnlyList<ModAnimalDto> Animals, IReadOnlyList<ModStatDto> Fields);
public sealed record ModAddSpeciesParams(string Id, string Revision, string SpeciesId, string Template, string Name,
    string Genus, string Epithet, int? Cost = null, IReadOnlyDictionary<string, double>? Stats = null);
public sealed record ModSetSpeciesParams(string Id, string Revision, string SpeciesId, string Name, string Genus, string Epithet,
    int? Cost, IReadOnlyDictionary<string, double> Stats, IReadOnlyDictionary<string, string>? DataFields = null,
    IReadOnlyDictionary<string, string>? Localization = null);

public sealed record ModSpeciesTemplateParams(string Template);
/// <summary>Found: the game data dump has this species; Cost and Stats are what a new species based on it inherits.</summary>
public sealed record ModSpeciesTemplateDto(string Template, bool Found, int? Cost, IReadOnlyDictionary<string, double> Stats);

public sealed partial class ModsMethods
{
    [RpcMethod("mods.speciesTemplate")]
    public ModSpeciesTemplateDto SpeciesTemplate(ModSpeciesTemplateParams p)
    {
        var (ws, _) = session.Current();
        var stats = SpeciesDatabase.TemplateStats(ws, p.Template);
        return new ModSpeciesTemplateDto(p.Template, stats is not null, stats?.Cost, stats?.Stats ?? new Dictionary<string, double>());
    }

    [RpcMethod("mods.createUnityProject")]
    public ModUnityProjectDto CreateUnityProject(ModIdParams p)
    {
        var (ws, install) = session.Current();
        ModProject.Open(ws, p.Id);
        var directory = UnitySpeciesProject.Create(ws, p.Id, Path.Combine(AppContext.BaseDirectory, "unity", "SpeciesAuthoring"));
        UnityAuthoringReferences.Prepare(directory, install.ManagedDir);
        return new ModUnityProjectDto(directory, Path.Combine(directory, "README.md"));
    }

    [RpcMethod("mods.animalDatabase")]
    public ModAnimalDatabase AnimalDatabase(ModIdParams p)
    {
        var (ws, _) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        return new ModAnimalDatabase(mod.Manifest.Species.Select(s =>
        {
            var inherited = SpeciesDatabase.Template(ws, s.Base);
            return new ModAnimalDto(s.Id, s.Key(mod.Id), s.Base, s.Name, s.Genus, s.Epithet, s.Cost, s.Stats,
                inherited?.Cost, inherited?.Stats ?? new Dictionary<string, double>(),
                (s.Fields.Count > 0 ? s.Fields : inherited?.Fields ?? []).Select(f => new ModAnimalFieldDto(f.Path, f.Kind, f.Value, f.TemplateValue, f.Editable)).ToList(),
                s.Localization, s.Icon, s.Prefab);
        }).ToList(), SpeciesStats.Fields.Select(f => new ModStatDto(f.Id, f.Label, f.Unit, f.Min, f.Max)).ToList());
    }

    [RpcMethod("mods.addSpecies")]
    public ModDetail AddSpecies(ModAddSpeciesParams p) => Edit(p.Id, p.Revision, mod =>
    {
        var (ws, _) = session.Current();
        var stats = p.Stats ?? new Dictionary<string, double>();
        var vanilla = SpeciesSkinsReader.Load(ws);
        var template = vanilla.FirstOrDefault(s => string.Equals(s.SpeciesId, p.Template, StringComparison.OrdinalIgnoreCase));
        var inherited = SpeciesDatabase.Template(ws, template?.SpeciesId ?? p.Template);
        ValidateStats(stats, inherited);
        mod.AddSpecies(vanilla, p.SpeciesId, p.Template, p.Name, p.Genus, p.Epithet, p.Cost, stats, inherited);
    }, "added species " + p.Name);

    [RpcMethod("mods.setSpecies")]
    public ModDetail SetSpecies(ModSetSpeciesParams p) => Edit(p.Id, p.Revision, mod =>
    {
        var (ws, _) = session.Current();
        var entry = mod.Manifest.Species.FirstOrDefault(s => s.Id == p.SpeciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + p.SpeciesId);
        ValidateStats(p.Stats, SpeciesDatabase.Template(ws, entry.Base));
        mod.SetSpecies(p.SpeciesId, p.Name, p.Genus, p.Epithet, p.Cost, p.Stats, p.DataFields, SpeciesDatabase.Template(ws, entry.Base),
            p.Localization);
    }, "updated species " + p.SpeciesId);

    private static void ValidateStats(IReadOnlyDictionary<string, double> stats, SpeciesTemplateStats? template)
    {
        try { SpeciesDatabase.ValidateEffective(stats, template?.Stats ?? new Dictionary<string, double>()); }
        catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
    }

    [RpcMethod("mods.setSpeciesPrefab")]
    public ModDetail SetSpeciesPrefab(ModSetSpeciesPrefabParams p) => Edit(p.Id, p.Revision,
        mod => mod.SetSpeciesPrefab(p.SpeciesId, p.Bundle, p.Asset), "updated species prefab " + p.SpeciesId);

    [RpcMethod("mods.setSpeciesIcon")]
    public ModDetail SetSpeciesIcon(ModSetSpeciesIconParams p) => Edit(p.Id, p.Revision,
        mod => mod.SetSpeciesIcon(p.SpeciesId, p.Png), "updated species icon " + p.SpeciesId);
}
