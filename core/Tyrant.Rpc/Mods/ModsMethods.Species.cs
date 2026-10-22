using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Jobs;

namespace Tyrant.Rpc.Mods;

public sealed record ModAnimalDto(string Id, string Key, string Base, string Name, string Genus, string Epithet,
    int? Cost, IReadOnlyDictionary<string, double> Stats, int? InheritedCost, IReadOnlyDictionary<string, double> InheritedStats,
    IReadOnlyList<ModAnimalFieldDto> DataFields, IReadOnlyDictionary<string, string> Localization, string? Icon, SpeciesPrefab? Prefab,
    int StartingSkin = 0, string? StartingSkinName = null, string? Swatch = null,
    IReadOnlyDictionary<string, string>? Portraits = null, string? StartingSkinId = null, string? PaleopediaPicture = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Texts = null);
public sealed record ModAnimalFieldDto(string Path, string Kind, string Value, string TemplateValue, bool Editable);
/// <summary>The Nursery picture (the game's heroRender); a null Png goes back to the template's.</summary>
public sealed record ModSetSpeciesIconParams(string Id, string Revision, string SpeciesId, string? Png = null);
/// <summary>The species' own Paleopedia page picture; a null Png goes back to the template's.</summary>
public sealed record ModSetSpeciesPaleopediaPictureParams(string Id, string Revision, string SpeciesId, string? Png = null);
public sealed record ModSetSpeciesPrefabParams(string Id, string Revision, string SpeciesId, string? Bundle = null, string? Asset = null);
public sealed record ModUnityProjectDto(string Directory, string Readme);
public sealed record ModStatDto(string Id, string Label, string Unit, double Min, double Max);
public sealed record ModAnimalDatabase(IReadOnlyList<ModAnimalDto> Animals, IReadOnlyList<ModStatDto> Fields);
public sealed record ModAddSpeciesParams(string Id, string Revision, string SpeciesId, string Template, string Name,
    string Genus, string Epithet, int? Cost = null, IReadOnlyDictionary<string, double>? Stats = null, int StartingSkin = 0,
    string? StartFrom = null, int? StartSkin = null, string? StartPng = null, string? StartPngFemale = null, string? StartName = null);
/// <summary>An editable starting skin for a species without one: a copy of a game skin (StartFrom/StartSkin, default the template's), or PNGs.</summary>
public sealed record ModAddStartingSkinParams(string Id, string Revision, string SpeciesId, string? StartFrom = null, int? StartSkin = null,
    string? StartPng = null, string? StartPngFemale = null, string? StartName = null);
public sealed record ModSetSpeciesParams(string Id, string Revision, string SpeciesId, string Name, string Genus, string Epithet,
    int? Cost, IReadOnlyDictionary<string, double> Stats, IReadOnlyDictionary<string, string>? DataFields = null,
    IReadOnlyDictionary<string, string>? Localization = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Texts = null);

public sealed record ModSpeciesParams(string Id, string SpeciesId);
/// <summary>DeleteFiles: its files, language files, icon and Blender projects go to the Recycle Bin too.</summary>
public sealed record ModRemoveSpeciesParams(string Id, string Revision, string SpeciesId, bool DeleteFiles = true);
public sealed record ModMakeStartingSkinParams(string Id, string Revision, string Skin);

public sealed record ModSetSpeciesStartingSkinParams(string Id, string Revision, string SpeciesId, int StartingSkin, string? Name = null);
public sealed record ModSetSpeciesSwatchParams(string Id, string Revision, string SpeciesId, string? Png = null);
public sealed record ModCustomSpeciesParams(string? Id = null);
public sealed record CustomSpeciesRow(string SpeciesId, string Name, string ModId, string ModName, bool Own, bool InWorkspace, bool Installed, bool Enabled,
    IReadOnlyList<VanillaSkinRow> Skins);
public sealed record ModCustomSpeciesResult(IReadOnlyList<CustomSpeciesRow> Species);

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

    [RpcMethod("mods.speciesModel")]
    public ModSkinModel SpeciesModel(ModSpeciesModelParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var entry = mod.Manifest.Species.FirstOrDefault(s => s.Id == p.SpeciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + p.SpeciesId);
        var key = entry.Key(mod.Id);
        var sex = p.Sex == "male" ? "male-only" : p.Sex; // one sex only, as in the game
        return ShownModel(ws, install, mod, key, entry.Base, () => OwnModelOf(mod, key, null), (species, index, slot) =>
            MapFromSource(ws, install, index, SkinMaps.Source(null, null, species.FirstOrDefault(s => s.SpeciesId == key)?.Skins.FirstOrDefault(), slot, sex)));
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
                SpeciesDatabase.FillEmptyObjects(s.Fields.Count > 0 ? s.Fields : inherited?.Fields ?? [], inherited?.Fields).Where(f => !SpeciesSizes.IsSizePath(f.Path)).Select(f => new ModAnimalFieldDto(f.Path, f.Kind, f.Value, f.TemplateValue, f.Editable)).ToList(),
                s.Localization, s.Icon, s.Prefab, s.StartingSkin, s.StartingSkinName, s.Swatch, s.Portraits.Count == 0 ? null : s.Portraits,
                mod.StartingSkinOf(s.Id)?.Id, s.PaleopediaPicture, mod.SpeciesTexts(s.Id));
        }).ToList(), SpeciesStats.Fields.Select(f => new ModStatDto(f.Id, f.Label, f.Unit, f.Min, f.Max)).ToList());
    }

    [RpcMethod("mods.addSpecies")]
    public ModDetail AddSpecies(ModAddSpeciesParams p) => Edit(p.Id, p.Revision, mod =>
    {
        var (ws, install) = session.Current();
        var stats = p.Stats ?? new Dictionary<string, double>();
        var vanilla = SpeciesSkinsReader.Load(ws);
        var template = vanilla.FirstOrDefault(s => string.Equals(s.SpeciesId, p.Template, StringComparison.OrdinalIgnoreCase));
        var inherited = SpeciesDatabase.Template(ws, template?.SpeciesId ?? p.Template);
        ValidateStats(stats, inherited);
        var source = StartSource(p.StartFrom, p.StartSkin, p.StartPng, p.StartPngFemale, template?.SpeciesId ?? p.Template, p.StartingSkin);
        mod.CreateSpecies(install, AssetIndex.Load(AssetIndex.PathIn(ws)), Options.AssetReader, vanilla, p.SpeciesId, p.Template, p.Name, p.Genus, p.Epithet,
            p.Cost, stats, inherited, p.StartingSkin, source, p.StartName);
    }, "added species " + p.Name);

    [RpcMethod("mods.addStartingSkin")]
    public ModDetail AddStartingSkin(ModAddStartingSkinParams p) => Edit(p.Id, p.Revision, mod =>
    {
        var (ws, install) = session.Current();
        var entry = mod.Manifest.Species.FirstOrDefault(s => s.Id == p.SpeciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + p.SpeciesId);
        mod.AddStartingSkin(install, AssetIndex.Load(AssetIndex.PathIn(ws)), Options.AssetReader, SpeciesSkinsReader.Load(ws), p.SpeciesId,
            StartSource(p.StartFrom, p.StartSkin, p.StartPng, p.StartPngFemale, entry.Base, entry.StartingSkin), p.StartName);
    }, "added a starting skin to " + p.SpeciesId);

    /// <summary>Own PNGs when given, else a copy of StartFrom's skin StartSkin (default: the template's starting skin).</summary>
    private static StartingSkinSource StartSource(string? from, int? skin, string? png, string? pngFemale, string template, int templateSkin)
    {
        if (png is not null && (from is not null || skin is not null))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Copy a skin or use your own PNGs, not both.");
        if (png is null && pngFemale is not null)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "A female PNG needs the male one too.");
        return png is not null ? new OwnPngs(png, pngFemale) : new CopySkin(from ?? template, skin ?? (from is null ? templateSkin : 0));
    }

    /// <summary>What mods.removeSpecies would take with it, and which other workspace mods use the species.</summary>
    [RpcMethod("mods.speciesRemoval")]
    public SpeciesRemoval SpeciesRemoval(ModSpeciesParams p)
    {
        var (ws, _) = session.Current();
        return ModProject.Open(ws, p.Id).SpeciesRemovalOf(ws, p.SpeciesId);
    }

    [RpcMethod("mods.removeSpecies")]
    public ModDetail RemoveSpecies(ModRemoveSpeciesParams p) =>
        Edit(p.Id, p.Revision, mod =>
        {
            foreach (var line in mod.RemoveSpecies(p.SpeciesId, p.DeleteFiles).Kept) session.Log($"'{p.Id}': {line}");
        }, $"removed species '{p.SpeciesId}'{(p.DeleteFiles ? " and its files" : "")}");

    [RpcMethod("mods.makeStartingSkin")]
    public ModDetail MakeStartingSkin(ModMakeStartingSkinParams p) =>
        Edit(p.Id, p.Revision, mod => mod.MakeStartingSkin(p.Skin), $"made skin '{p.Skin}' its species' starting skin");

    [RpcMethod("mods.setSpecies")]
    public ModDetail SetSpecies(ModSetSpeciesParams p) => Edit(p.Id, p.Revision, mod =>
    {
        var (ws, _) = session.Current();
        var entry = mod.Manifest.Species.FirstOrDefault(s => s.Id == p.SpeciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + p.SpeciesId);
        ValidateStats(p.Stats, SpeciesDatabase.Template(ws, entry.Base));
        if (p.Texts != null) ModProject.ValidateSpeciesTexts(p.Texts);
        mod.SetSpecies(p.SpeciesId, p.Name, p.Genus, p.Epithet, p.Cost, p.Stats, p.DataFields, SpeciesDatabase.Template(ws, entry.Base),
            p.Localization);
        if (p.Texts != null) mod.SetSpeciesTexts(p.SpeciesId, p.Texts);
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
        mod => mod.SetSpeciesIcon(p.SpeciesId, p.Png), "updated Nursery picture " + p.SpeciesId);

    [RpcMethod("mods.setSpeciesPaleopediaPicture")]
    public ModDetail SetSpeciesPaleopediaPicture(ModSetSpeciesPaleopediaPictureParams p) => Edit(p.Id, p.Revision,
        mod => mod.SetSpeciesPaleopediaPicture(p.SpeciesId, p.Png), "updated Paleopedia picture " + p.SpeciesId);

    [RpcMethod("mods.setSpeciesStartingSkin")]
    public ModDetail SetSpeciesStartingSkin(ModSetSpeciesStartingSkinParams p) => Edit(p.Id, p.Revision, mod =>
    {
        var (ws, _) = session.Current();
        mod.SetSpeciesStartingSkin(SpeciesSkinsReader.Load(ws), p.SpeciesId, p.StartingSkin, p.Name);
    }, "updated starting skin " + p.SpeciesId);

    [RpcMethod("mods.setSpeciesSwatch")]
    public ModDetail SetSpeciesSwatch(ModSetSpeciesSwatchParams p) => Edit(p.Id, p.Revision,
        mod => mod.SetSpeciesSwatch(p.SpeciesId, p.Png), "updated species swatch " + p.SpeciesId);

    /// <summary>Every custom species a skin can target: this mod's (Own) and other mods' in the workspace or installed in the game.</summary>
    [RpcMethod("mods.customSpecies")]
    public ModCustomSpeciesResult CustomSpecies(ModCustomSpeciesParams p)
    {
        var (ws, install) = session.Current();
        var vanilla = TrySpecies(ws) ?? [];
        var own = p.Id is null ? null : ModProject.Open(ws, p.Id).Manifest;
        var others = OtherModSpecies.Load(ws, Game.List(install), p.Id);
        var rows = new List<CustomSpeciesRow>();
        void Add(string key, SpeciesEntry entry, string modId, string modName, bool isOwn, OtherModState? state)
        {
            var row = ModProject.WithSpecies(vanilla, new ModManifest { Id = modId, Name = modName }, [new OtherSpecies(key, modId, modName, entry)])
                .FirstOrDefault(s => s.SpeciesId == key);
            if (row is null) return; // its template is not in this workspace's data dump
            rows.Add(new CustomSpeciesRow(key, entry.Name, modId, modName, isOwn, state?.InWorkspace ?? true, state?.Installed ?? false, state?.Enabled ?? false,
                row.Skins.Select(k => new VanillaSkinRow(k.Index, k.Name, k.Male.Count > 0, k.Female.Count > 0)).ToList()));
        }
        if (own is not null) foreach (var entry in own.Species) Add(entry.Key(own.Id), entry, own.Id, own.Name, true, null);
        foreach (var other in others.Species) Add(other.Key, other.Entry, other.ModId, other.ModName, false, others.Mods[other.ModId]);
        return new ModCustomSpeciesResult(rows);
    }
}
