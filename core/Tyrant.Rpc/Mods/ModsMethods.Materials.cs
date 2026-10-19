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
        OneOf(p.Skin, p.Species, p.VanillaSkin);
        if (p.Skin is { } skin) return Edit(p.Id, p.Revision, mod => mod.SetSkinMaterials(skin, p.Materials), $"set the materials of '{skin}'");
        if (p.VanillaSkin is { } vanilla)
        {
            var (vanillaSpecies, skinName) = VanillaParts(vanilla);
            return Edit(p.Id, p.Revision, mod => mod.SetVanillaSkinMaterials(vanillaSpecies, skinName, p.Materials), $"set the materials of vanilla skin '{vanilla}'");
        }
        var (ws, install) = session.Current();
        var dump = TrySpecies(ws) ?? throw new TyrantException(TyrantErrorCode.DataMissing, NeedsDump, FixAction.RefreshWorkspace);
        var index = TryIndex(ws) ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing, NeedsIndex, FixAction.ReindexAssets);
        return Edit(p.Id, p.Revision, mod => mod.SetSpeciesMaterials(index, mod.SpeciesFor(ws, install, dump), p.Species!, p.Materials),
            $"set the materials of species '{p.Species}'");
    }

    [RpcMethod("mods.setMaterialFile")]
    public ModDetail SetMaterialFile(ModSetMaterialFileParams p)
    {
        OneOf(p.Skin, p.Species, p.VanillaSkin);
        MaterialOwner owner;
        if (p.Skin is not null) owner = new MaterialOwner(MaterialOwnerKind.Skin, p.Skin, null, null);
        else if (p.VanillaSkin is not null)
        {
            var (vanillaSpecies, skinName) = VanillaParts(p.VanillaSkin);
            owner = new MaterialOwner(MaterialOwnerKind.VanillaSkin, null, vanillaSpecies, skinName);
        }
        else owner = new MaterialOwner(MaterialOwnerKind.Species, null, p.Species, null);
        return Edit(p.Id, p.Revision, mod => mod.SetMaterialFile(owner, p.Material, p.Part, p.Png),
            $"{(p.Png is null ? "removed" : "set")} the {p.Part} of '{p.Material}' on '{p.Skin ?? p.VanillaSkin ?? p.Species}'");
    }

    [RpcMethod("mods.materialInfo")]
    public ModMaterialInfo MaterialInfo(ModMaterialInfoParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var speciesId = SpeciesOf(mod, p.Skin, p.Species, p.VanillaSkin);
        var empty = MaterialProperties.All.Select(x => Property(x, null, "default")).ToList();
        if (TrySpecies(ws) is not { } dump) return new ModMaterialInfo(speciesId, speciesId, empty, null, null, false, NeedsDump);
        if (TryIndex(ws) is not { } index) return new ModMaterialInfo(speciesId, speciesId, empty, null, null, false, NeedsIndex);
        var species = mod.SpeciesFor(ws, install, dump);
        var game = GameMaterial(install, species, index, speciesId);
        if (game is null) return new ModMaterialInfo(speciesId, speciesId, empty, null, null, false, $"'{speciesId}' has no animal material to change.");

        var (below, sources) = MaterialPreview.Inherited(mod, p.Skin, p.Species, game.Name, p.VanillaSkin);
        var rows = MaterialProperties.All.Select(x =>
        {
            if (below is not null && below.Settings.TryGetValue(x.Key, out var set))
                return Property(x, Json.Write(set.ToJson(Count(x))), sources.GetValueOrDefault(x.Key, "species"));
            if (GameValue(game, x) is { } value) return Property(x, Json.Write(value.ToJson(Count(x))), "game");
            return Property(x, null, "default");
        }).ToList();
        var iridescence = below?.Iridescence is { } i ? Part(game.Name, new MaterialOverride { Iridescence = i }, "iridescence") : null;
        var fur = below?.Fur is { } f ? Part(game.Name, new MaterialOverride { Fur = f }, "fur") : null;
        return new ModMaterialInfo(game.Name, speciesId, rows, iridescence, VanillaIridescence(mod, species, p.Skin, p.VanillaSkin, null), game.Ramp is not null, null, fur);
    }

    /// <summary>One part ("iridescence", "fur") of a block as materials.json writes it; null when it writes nothing.</summary>
    private static string? Part(string material, MaterialOverride block, string part) =>
        MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride> { [material] = block }).TryGetValue(material, out var written)
        && written is Dictionary<string, object?> entry && entry.TryGetValue(part, out var value) ? Json.Write(value) : null;

    [RpcMethod("mods.materialPreview")]
    public ModMaterialPreview MaterialPreviewOf(ModMaterialPreviewParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var speciesId = SpeciesOf(mod, p.Skin, p.Species, p.VanillaSkin);
        var dump = TrySpecies(ws);
        var index = TryIndex(ws);
        var species = dump is null ? null : mod.SpeciesFor(ws, install, dump);
        var game = species is null || index is null ? null : GameMaterial(install, species, index, speciesId);
        Dictionary<string, MaterialOverride>? blocks;
        try
        {
            blocks = string.IsNullOrWhiteSpace(p.Materials) ? null : MaterialOverrides.Parse(Json.Parse(p.Materials), p.Skin ?? p.VanillaSkin ?? speciesId, p.Species is null);
        }
        catch (Exception ex) when (ex is ManifestException or FormatException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"These material settings cannot be previewed: {ex.Message}");
        }
        var material = game?.Name ?? blocks?.Keys.FirstOrDefault() ?? "";
        var draft = blocks is not null && blocks.TryGetValue(material, out var found) ? found : null;
        var dir = PreviewsDir(ws, "materials");
        // The game's maps for masks taken from a slot: the vanilla skin's, or a mod skin's base skin's (for a slot it has no PNG for).
        var vanilla = species is null ? null : p.VanillaSkin is not null ? VanillaOf(species, p.VanillaSkin)
            : p.Skin is not null ? SkinMaps.BaseOf(species, mod.Skin(p.Skin)) : null;
        var result = MaterialPreview.Build(mod, p.Skin, p.Species, material, draft, p.Sex, game, dir,
            record => record.Guid is null || index is null ? null
                : SkinMaps.Write("guid:" + record.Guid, install, index, Options.AssetReader, Path.Combine(dir, $"{record.Guid}.png")),
            species is null ? null : VanillaIridescence(mod, species, p.Skin, p.VanillaSkin, p.Sex),
            p.VanillaSkin,
            slot => SkinMaps.Source(null, null, vanilla, slot, p.Sex) is { } source && index is not null
                ? SkinMaps.Write(source, install, index, Options.AssetReader, Path.Combine(dir, source[5..] + ".png")) : null,
            id => dump is null || index is null ? null : GameMaterial(install, dump, index, id)); // a ramp or mask taken from a species: the game's files
        return new ModMaterialPreview(result.On, result.Strength, result.Scale, result.Power, result.Bias, InCache(ws, mod, dir, result.RampFile),
            InCache(ws, mod, dir, result.MaskFile), result.MaskSt, result.Numbers.TryGetValue("_Smoothness", out var smoothness) ? smoothness : null);
    }

    [RpcMethod("mods.thinFilm")]
    public ModThinFilm ThinFilmStops(ModThinFilmParams p) =>
        new(ThinFilm.Ramp(p.Thickness, p.Index, Math.Clamp(p.Count, 1, IridescenceSettings.MaxRampStops)).Select(c => c.ToString()).ToList());

    [RpcMethod("mods.speciesPresets")]
    public ModSpeciesPresets SpeciesPresets()
    {
        var (ws, _) = session.Current();
        if (TrySpecies(ws) is not { } dump) return new ModSpeciesPresets([], [], NeedsDump);
        static List<ModSpeciesPresetDto> Of(IReadOnlyList<SpeciesSkins> dump, PresetKind kind) =>
            GamePresets.List(dump, kind).Select(s => new ModSpeciesPresetDto(s.Id, s.Name)).ToList();
        return new ModSpeciesPresets(Of(dump, PresetKind.Iridescence), Of(dump, PresetKind.Fur), null);
    }

    [RpcMethod("mods.speciesPreset")]
    public ModSpeciesPreset SpeciesPresetOf(ModSpeciesPresetParams p)
    {
        var kind = GamePresets.KindOf(p.Kind)
            ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"A preset is \"iridescence\" or \"fur\", not \"{p.Kind}\".");
        var (ws, install) = session.Current();
        var dump = TrySpecies(ws) ?? throw new TyrantException(TyrantErrorCode.DataMissing, NeedsDump, FixAction.RefreshWorkspace);
        var index = TryIndex(ws) ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing, NeedsIndex, FixAction.ReindexAssets);
        var species = dump.FirstOrDefault(s => string.Equals(s.SpeciesId, p.Species, StringComparison.Ordinal))
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"\"{p.Species}\" is not a species in the game data.");
        var preset = GamePresets.Of(species, GameMaterial(install, dump, index, species.SpeciesId));
        return new ModSpeciesPreset(species.SpeciesId, species.Name, GamePresets.FragmentJson(preset, kind, p.Target));
    }

    [RpcMethod("mods.vanillaSkins")]
    public ModVanillaSkinList VanillaSkinsOf(ModVanillaSkinsParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var species = mod.SpeciesFor(ws, install, RequireSpecies(ws));
        var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, p.Species, StringComparison.OrdinalIgnoreCase));
        return new ModVanillaSkinList(target?.Skins.Select(v => v.Name).ToList() ?? []);
    }

    /// <summary>Requires exactly one of skin, species and vanilla skin.</summary>
    private static void OneOf(string? skin, string? species, string? vanillaSkin)
    {
        if ((skin is null ? 0 : 1) + (species is null ? 0 : 1) + (vanillaSkin is null ? 0 : 1) != 1)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Give a skin, a species or a vanilla skin, only one.");
    }

    /// <summary>A vanilla skin's key "&lt;species&gt;/&lt;skin name&gt;" as its species and name (the name may hold '/').</summary>
    private static (string Species, string SkinName) VanillaParts(string key) =>
        key.IndexOf('/') is > 0 and var slash && slash < key.Length - 1 ? (key[..slash], key[(slash + 1)..])
            : throw new TyrantException(TyrantErrorCode.ModInvalid, $"Name a vanilla skin as \"<species>/<skin name>\", not \"{key}\".");

    /// <summary>The species of the one target given (a skin's, a vanilla skin's or the species itself).</summary>
    private static string SpeciesOf(ModProject mod, string? skin, string? species, string? vanillaSkin)
    {
        OneOf(skin, species, vanillaSkin);
        return skin is not null ? mod.Skin(skin).Species : vanillaSkin is not null ? VanillaParts(vanillaSkin).Species : species!;
    }

    /// <summary>The vanilla skin of the key "&lt;species&gt;/&lt;skin name&gt;" (any case); null when the dump has none.</summary>
    private static VanillaSkin? VanillaOf(IReadOnlyList<SpeciesSkins> species, string key)
    {
        var (speciesId, name) = VanillaParts(key);
        return species.FirstOrDefault(s => string.Equals(s.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase))
            ?.Skins.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The own iridescence of a skin's base (vanilla) skin, or of a vanilla skin itself, for a sex (null: the stronger); null for a species or when the dump cannot tell.</summary>
    private static float? VanillaIridescence(ModProject mod, IReadOnlyList<SpeciesSkins> species, string? skinId, string? vanillaSkin, string? sex) =>
        skinId is not null ? SkinMaps.BaseOf(species, mod.Skin(skinId))?.Iridescence(sex)
            : vanillaSkin is not null ? VanillaOf(species, vanillaSkin)?.Iridescence(sex) : null;

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
        _ => game.Colors.TryGetValue(x.Key, out var v) ? MaterialValue.OfVector(x.Components == 3 && v.Length > 3 ? v[..3] : v) : null,
    };

    /// <summary>How many numbers a vector setting is written with (exactly 3, or all four).</summary>
    private static int Count(MaterialProperty x) => x.Components == 3 ? 3 : 4;

    private static ModMaterialProperty Property(MaterialProperty x, string? inherited, string source) =>
        new(x.Key, x.Group, x.Label, x.Kind.ToString().ToLowerInvariant(), x.Min, x.Max, inherited, source, x.Components);

    private static IReadOnlyList<ModVanillaSkinMaterialsDto>? VanillaSkinMaterialsOf(ModManifest m)
    {
        var list = m.VanillaSkinMaterials.Where(x => x.Value.Count > 0).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x =>
        {
            var (species, skin) = VanillaParts(x.Key);
            return new ModVanillaSkinMaterialsDto(species, skin, Json.Write(MaterialOverrides.ToJson(x.Value)));
        }).ToList();
        return list.Count == 0 ? null : list;
    }

    /// <summary>The preview file as a path in the preview cache (the app may only load files there); a mod's own file is copied.</summary>
    private static string? InCache(Workspace ws, ModProject mod, string dir, string? file) =>
        file is null ? null
            : Path.GetFullPath(file).StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase) ? file
            : OwnMapCopy(ws, mod, Path.GetRelativePath(mod.Dir, file));

    private static IReadOnlyList<ModSpeciesMaterialsDto>? SpeciesMaterialsOf(ModManifest m)
    {
        var list = m.SpeciesMaterials.Where(x => x.Value.Count > 0)
            .Select(x => new ModSpeciesMaterialsDto(x.Key, Json.Write(MaterialOverrides.ToJson(x.Value)))).ToList();
        return list.Count == 0 ? null : list;
    }
}
