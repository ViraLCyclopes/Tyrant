using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Species;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

public sealed partial class ModsMethods
{
    private const string NeedsDump = "Material settings need the game's data: on the Workspace tab click Run data dump (or run 'tyrant dump run').";
    private const string NeedsIndex = "Material settings need the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index').";

    [RpcMethod("mods.setMaterials")]
    public ModDetail SetMaterials(ModSetMaterialsParams p)
    {
        Target(p.Skin, p.Species);
        if (p.Skin is { } skin) return Edit(p.Id, p.Revision, mod => mod.SetSkinMaterials(skin, p.Materials), $"set the materials of '{skin}'");
        var (ws, install) = session.Current();
        var dump = TrySpecies(ws) ?? throw new TyrantException(TyrantErrorCode.DataMissing, NeedsDump, FixAction.RefreshWorkspace);
        var index = TryIndex(ws) ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing, NeedsIndex, FixAction.ReindexAssets);
        return Edit(p.Id, p.Revision, mod => mod.SetSpeciesMaterials(index, mod.SpeciesFor(ws, install, dump), p.Species!, p.Materials),
            $"set the materials of species '{p.Species}'");
    }

    [RpcMethod("mods.setMaterialFile")]
    public ModDetail SetMaterialFile(ModSetMaterialFileParams p)
    {
        Target(p.Skin, p.Species);
        return Edit(p.Id, p.Revision, mod => mod.SetMaterialFile(p.Skin, p.Species, p.Material, p.Part, p.Png),
            $"{(p.Png is null ? "removed" : "set")} the {p.Part} of '{p.Material}' on '{p.Skin ?? p.Species}'");
    }

    [RpcMethod("mods.materialInfo")]
    public ModMaterialInfo MaterialInfo(ModMaterialInfoParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var speciesId = Target(p.Skin, p.Species) is { } skinId ? mod.Skin(skinId).Species : p.Species!;
        var empty = MaterialProperties.All.Select(x => Property(x, null, "default")).ToList();
        if (TrySpecies(ws) is not { } dump) return new ModMaterialInfo(speciesId, speciesId, empty, null, false, NeedsDump);
        if (TryIndex(ws) is not { } index) return new ModMaterialInfo(speciesId, speciesId, empty, null, false, NeedsIndex);
        var game = GameMaterial(install, mod.SpeciesFor(ws, install, dump), index, speciesId);
        if (game is null) return new ModMaterialInfo(speciesId, speciesId, empty, null, false, $"'{speciesId}' has no animal material to change.");

        var (below, sources) = MaterialPreview.Inherited(mod, p.Skin, p.Species, game.Name);
        var rows = MaterialProperties.All.Select(x =>
        {
            if (below is not null && below.Settings.TryGetValue(x.Key, out var set))
                return Property(x, Json.Write(set.ToJson()), sources.GetValueOrDefault(x.Key, "species"));
            if (GameValue(game, x) is { } value) return Property(x, Json.Write(value.ToJson()), "game");
            return Property(x, null, "default");
        }).ToList();
        string? iridescence = null;
        if (below?.Iridescence is { } i)
        {
            var json = MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride> { [game.Name] = new MaterialOverride { Iridescence = i } });
            if (json[game.Name] is Dictionary<string, object?> entry && entry.TryGetValue("iridescence", out var block)) iridescence = Json.Write(block);
        }
        return new ModMaterialInfo(game.Name, speciesId, rows, iridescence, game.Ramp is not null, null);
    }

    [RpcMethod("mods.materialPreview")]
    public ModMaterialPreview MaterialPreviewOf(ModMaterialPreviewParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var speciesId = Target(p.Skin, p.Species) is { } skinId ? mod.Skin(skinId).Species : p.Species!;
        var dump = TrySpecies(ws);
        var index = TryIndex(ws);
        var game = dump is null || index is null ? null : GameMaterial(install, mod.SpeciesFor(ws, install, dump), index, speciesId);
        Dictionary<string, MaterialOverride>? blocks;
        try
        {
            blocks = string.IsNullOrWhiteSpace(p.Materials) ? null : MaterialOverrides.Parse(Json.Parse(p.Materials), p.Skin ?? speciesId, p.Skin is not null);
        }
        catch (Exception ex) when (ex is ManifestException or FormatException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"These material settings cannot be previewed: {ex.Message}");
        }
        var material = game?.Name ?? blocks?.Keys.FirstOrDefault() ?? "";
        var draft = blocks is not null && blocks.TryGetValue(material, out var found) ? found : null;
        var dir = PreviewsDir(ws, "materials");
        var result = MaterialPreview.Build(mod, p.Skin, p.Species, material, draft, p.Sex, game, dir,
            record => record.Guid is null || index is null ? null
                : SkinMaps.Write("guid:" + record.Guid, install, index, Options.AssetReader, Path.Combine(dir, $"{record.Guid}.png")));
        return new ModMaterialPreview(result.On, result.Strength, result.Scale, result.Power, result.Bias, InCache(ws, mod, dir, result.RampFile),
            InCache(ws, mod, dir, result.MaskFile), result.MaskSt, result.Numbers.TryGetValue("_Smoothness", out var smoothness) ? smoothness : null);
    }

    [RpcMethod("mods.thinFilm")]
    public ModThinFilm ThinFilmStops(ModThinFilmParams p) =>
        new(ThinFilm.Ramp(p.Thickness, p.Index, Math.Clamp(p.Count, 1, IridescenceSettings.MaxRampStops)).Select(c => c.ToString()).ToList());

    /// <summary>Requires exactly one of skin and species; returns the skin (null for a species).</summary>
    private static string? Target(string? skin, string? species)
    {
        if ((skin is null) == (species is null)) throw new TyrantException(TyrantErrorCode.ModInvalid, "Give a skin or a species, not both.");
        return skin;
    }

    private AnimalMaterial? GameMaterial(GameInstall install, IReadOnlyList<SpeciesSkins> species, AssetIndex index, string speciesId)
    {
        var guid = species.FirstOrDefault(s => s.SpeciesId == speciesId)?.PrefabGuid;
        var prefab = guid is null ? null : index.Assets.FirstOrDefault(a => a.Type == "GameObject" && string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase));
        return prefab is null ? null : AnimalMaterial.Of(install, Options.AssetReader, index, prefab);
    }

    private static MaterialValue? GameValue(AnimalMaterial game, MaterialProperty x) => x.Kind switch
    {
        MaterialValueKind.Number => game.Floats.TryGetValue(x.Key, out var n) ? MaterialValue.OfNumber(n) : null,
        MaterialValueKind.Color => game.Colors.TryGetValue(x.Key, out var c) && c.Length >= 3 ? MaterialValue.OfColor(new Rgb(c[0], c[1], c[2])) : null,
        _ => game.Colors.TryGetValue(x.Key, out var v) ? MaterialValue.OfVector(v) : null,
    };

    private static ModMaterialProperty Property(MaterialProperty x, string? inherited, string source) =>
        new(x.Key, x.Group, x.Label, x.Kind.ToString().ToLowerInvariant(), x.Min, x.Max, inherited, source);

    /// <summary>The preview file as a path in the preview cache (the app may only load files there); a mod's own file is copied.</summary>
    private static string? InCache(Workspace ws, ModProject mod, string dir, string? file) =>
        file is null ? null
            : Path.GetFullPath(file).StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase) ? file
            : OwnMapCopy(ws, mod, Path.GetRelativePath(mod.Dir, file));

    private static IReadOnlyList<ModSpeciesMaterialsDto>? SpeciesMaterialsOf(ModManifest m)
    {
        var list = m.Models.Where(x => x.Materials is not null)
            .Select(x => new ModSpeciesMaterialsDto(x.Target, Json.Write(MaterialOverrides.ToJson(x.Materials!)))).ToList();
        return list.Count == 0 ? null : list;
    }
}
