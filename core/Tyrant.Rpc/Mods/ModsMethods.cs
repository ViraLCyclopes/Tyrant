using Tyrant.Core.Assets;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Mods;

/// <summary>mods.* — make mods in the workspace, check them, and install, switch or remove them in the game.</summary>
public sealed partial class ModsMethods(StudioSession session, JobManager jobs)
{
    private StudioOptions Options => session.Options;

    private GameMods Game => new(install => Options.Launcher.IsRunning(install));

    [RpcMethod("mods.list")]
    public ModsListResult List()
    {
        var (ws, install) = session.Current();
        return ListOf(ws, install);
    }

    [RpcMethod("mods.create")]
    public ModsListResult Create(ModCreateParams p)
    {
        var (ws, install) = session.Current();
        ModProject.Create(ws, p.Id, p.Name, p.Author);
        session.Log($"Created mod '{p.Id}'.");
        return ListOf(ws, install);
    }

    [RpcMethod("mods.replace")]
    public ModsListResult Replace(ModReplaceParams p)
    {
        var (ws, install) = session.Current();
        var entry = ModProject.Open(ws, p.Id).Replace(ws, AssetIndex.Load(AssetIndex.PathIn(ws)), p.Texture, p.Png);
        session.Log($"'{p.Id}' replaces {entry.Texture} with {entry.File}.");
        return ListOf(ws, install);
    }

    [RpcMethod("mods.check")]
    public ModCheckReport Check(ModIdParams p)
    {
        var (ws, install) = session.Current();
        var checkedMod = ModProject.Open(ws, p.Id);
        RebuildStaleModels(ws, install, checkedMod);
        var result = CheckMod(ws, install, checkedMod);
        return new ModCheckReport(result.Errors, result.Warnings, result.MissingCutouts);
    }

    /// <summary>Copies the vanilla transparency back into the mod's colour PNGs that lost it (the workspace copy; install again after).</summary>
    [RpcMethod("mods.restoreCutouts")]
    public ModRestoreCutoutsResult RestoreCutouts(ModIdParams p)
    {
        var (ws, install) = session.Current();
        var index = AssetIndex.Load(AssetIndex.PathIn(ws)); // without it the game textures are unknown: say so, not "nothing to restore"
        var mod = ModProject.Open(ws, p.Id);
        var result = new CutoutRestorer(Cutouts.GamePixels(install, Options.AssetReader)).Restore(mod, index, TrySpeciesFor(ws, install, mod));
        if (result.Restored.Count > 0) session.Log($"'{p.Id}': restored the see-through parts of {string.Join(", ", result.Restored)}.");
        return new ModRestoreCutoutsResult(result.Restored, result.Problems);
    }

    [RpcMethod("mods.install", JobResult = typeof(ModInstallResult))]
    public JobStarted Install(ModIdParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id); // an unknown id fails before the job starts
        return jobs.Start($"Install {p.Id}", (progress, ct) =>
        {
            progress.Report(new JobProgress(0.05, $"Checking {p.Id}"));
            RebuildStaleModels(ws, install, mod, measureHeadRigs: true);
            var check = CheckMod(ws, install, mod);
            if (!check.Ok)
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{p.Id}' has problems, so it was not installed: {string.Join(" ", check.Errors)}");
            if (ModLoaderInstaller.FrameworkStatus(install, Options.DumperDir) != FrameworkState.Current)
            {
                string? zip = null;
                if (ModLoaderInstaller.GetState(install) == InstallState.NotInstalled)
                {
                    progress.Report(new JobProgress(0.2, $"Downloading {ModLoaderInstaller.LoaderName} {ModLoaderInstaller.Version}"));
                    zip = Options.LoaderZip(ws, ct);
                }
                progress.Report(new JobProgress(0.5, "Installing or updating Tyrant's framework in the game"));
                Options.Installer().Install(install, zip, Options.DumperDir);
            }
            progress.Report(new JobProgress(0.8, $"Installing {p.Id}"));
            var cooker = Cooker(ws, install, mod);
            var warnings = Game.Install(install, mod, staging => cooker.CookForInstall(mod, staging, Cooking(progress, 0.8, 0.98), ct));
            session.Log($"Installed '{p.Id}' into the game.");
            foreach (var warning in warnings) session.Log(warning);
            return new ModInstallResult($"Installed '{p.Id}' into the game. Start the game to see it.", check.Warnings.Concat(warnings).ToList());
        });
    }

    [RpcMethod("mods.remove")]
    public ModsListResult Remove(ModIdParams p)
    {
        var (ws, install) = session.Current();
        Game.Remove(install, p.Id);
        session.Log($"Removed '{p.Id}' from the game.");
        return ListOf(ws, install);
    }

    [RpcMethod("mods.enable")]
    public ModsListResult Enable(ModEnableParams p)
    {
        var (ws, install) = session.Current();
        Game.SetEnabled(install, p.Id, p.Enabled);
        return ListOf(ws, install);
    }

    private ModsListResult ListOf(Workspace ws, GameInstall install)
    {
        var game = Game;
        var snapshot = game.List(install);
        var installed = snapshot.ToDictionary(m => m.Id);
        OtherMods? everyone = null; // read only when a mod's skins use other mods' species
        var rows = new List<ModRow>();
        foreach (var id in ModProject.Ids(ws))
        {
            installed.TryGetValue(id, out var inGame);
            try
            {
                var mod = ModProject.Open(ws, id);
                var uses = SpeciesUse.Of(mod.Manifest);
                if (uses.Count > 0) everyone ??= OtherModSpecies.Load(ws, snapshot, null);
                rows.Add(new ModRow(id, mod.Manifest.Name, mod.Manifest.Version, mod.Manifest.Author, mod.Manifest.Replace.Count, mod.Manifest.Skins.Count,
                    Wire(game.StateOf(install, mod)), inGame?.Enabled, mod.Dir, null, inGame?.Clashes, UsesOf(uses, everyone), inGame?.Cooked,
                    inGame is not null && HasGameLooks(install, mod)));
            }
            catch (TyrantException ex)
            {
                rows.Add(new ModRow(id, id, "", null, 0, 0, "notInstalled", inGame?.Enabled, Path.Combine(ModProject.RootOf(ws), id), ex.Message));
            }
            installed.Remove(id);
        }
        rows.AddRange(installed.Values.Select(m => new ModRow(m.Id, m.Name, m.Version, null, m.Replacements, m.Skins, "gameOnly", m.Enabled, m.Dir, m.Error, m.Clashes, Cooked: m.Cooked,
            GameLooks: GameLooks.HasMaterials(m.Dir))));
        return new ModsListResult(rows, ModLoaderInstaller.HasFramework(install),
            ModLoaderInstaller.FrameworkStatus(install, Options.DumperDir) == FrameworkState.Outdated);
    }

    /// <summary>Whether the in-game editor changed the installed copy's looks; a file that cannot be read counts as yes, so the app asks first.</summary>
    private static bool HasGameLooks(GameInstall install, ModProject mod)
    {
        try
        {
            return GameLooks.Changes(install, mod).Any(c => c.GameChanged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // unknown: the app asks (mods.looksChanges then says what could not be read) rather than overwrite
        }
    }

    private static string Wire(ModInstallState state) => state switch
    {
        ModInstallState.Installed => "installed",
        ModInstallState.Changed => "changed",
        _ => "notInstalled",
    };

    [RpcMethod("mods.species")]
    public ModSpeciesResult Species()
    {
        var (ws, _) = session.Current();
        var species = TrySpecies(ws);
        return new ModSpeciesResult(species is not null, (species ?? []).Select(s =>
            new SpeciesSkinsRow(s.SpeciesId, s.Vivarium, s.Skins.Select(k => new VanillaSkinRow(k.Index, k.Name, k.Male.Count > 0, k.Female.Count > 0)).ToList(), s.PrefabGuid)).ToList());
    }

    [RpcMethod("mods.addSkin")]
    public ModsListResult AddSkin(ModAddSkinParams p)
    {
        var (ws, install) = session.Current();
        var entry = ModProject.Open(ws, p.Id).AddSkin(ws, install, AssetIndex.Load(AssetIndex.PathIn(ws)), Options.AssetReader, SpeciesSkinsReader.Load(ws),
            p.Species, p.Name, p.Base, new SkinTemplateOptions(p.Male, p.Female, p.Maps), p.Group,
            OtherModSpecies.Load(ws, Game.List(install), p.Id).Species);
        session.Log($"'{p.Id}' adds skin '{entry.Name}' to {entry.Species}.");
        return ListOf(ws, install);
    }

    [RpcMethod("mods.skinSlots")]
    public SkinSlotsResult SkinNumbers()
    {
        var (_, install) = session.Current();
        return SlotsOf(install);
    }

    [RpcMethod("mods.forgetSkins")]
    public SkinSlotsResult ForgetSkins(ModForgetSkinsParams p)
    {
        var (_, install) = session.Current();
        var forgotten = new SkinSlots(i => Options.Launcher.IsRunning(i)).Forget(install, p.Keys);
        session.Log($"Forgot {forgotten} skin number(s).");
        return SlotsOf(install);
    }

    private static SkinSlotsResult SlotsOf(GameInstall install) =>
        new(new SkinSlots().Orphans(install).Select(o => new OrphanSkinRow(o.Species, o.Key, o.Number)).ToList());

    private ModImages Images(GameInstall install) => new(Cutouts.GamePixels(install, Options.AssetReader, onlyIfAlpha: false));

    private static string PreviewsDir(Workspace ws, string kind) => Path.Combine(ws.Dir, "cache", "previews", kind);

    [RpcMethod("mods.get")]
    public ModDetail Get(ModIdParams p)
    {
        var (ws, _) = session.Current();
        return DetailOf(ws, ModProject.Open(ws, p.Id));
    }

    [RpcMethod("mods.setDetails")]
    public ModDetail SetDetails(ModSetDetailsParams p) => Edit(p.Id, p.Revision, mod => mod.SetDetails(p.Name, p.Version, p.Author, p.Description), "changed its details");

    [RpcMethod("mods.renameSkin")]
    public ModDetail RenameSkin(ModRenameSkinParams p) => Edit(p.Id, p.Revision, mod => mod.RenameSkin(p.Skin, p.Name), $"renamed skin '{p.Skin}' to '{p.Name}'");

    [RpcMethod("mods.removeSkin")]
    public ModDetail RemoveSkin(ModRemoveSkinParams p) =>
        Edit(p.Id, p.Revision, mod =>
        {
            foreach (var line in mod.RemoveSkin(p.Skin, p.DeleteFiles).Kept) session.Log($"'{p.Id}': {line}");
        }, $"removed skin '{p.Skin}'{(p.DeleteFiles ? " and its files" : "")}");

    [RpcMethod("mods.setColors")]
    public ModDetail SetColors(ModSetColorsParams p) => Edit(p.Id, p.Revision, mod => mod.SetColors(p.Skin, p.Colors), $"set the colours of '{p.Skin}'");

    [RpcMethod("mods.setSkinFile")]
    public ModDetail SetSkinFile(ModSetSkinFileParams p) =>
        Edit(p.Id, p.Revision, mod => mod.SetSkinFile(p.Skin, p.Sex, p.Slot, p.Png),
            p.Png is null ? $"'{p.Skin}' {p.Sex} {p.Slot} uses the base skin's texture" : $"'{p.Skin}' {p.Sex} {p.Slot} uses {p.Png}");

    [RpcMethod("mods.copyBaseFile")]
    public ModDetail CopyBaseFile(ModCopyBaseFileParams p)
    {
        var (ws, install) = session.Current();
        var dump = TrySpecies(ws) ?? throw new TyrantException(TyrantErrorCode.DataMissing,
            "Copying a base texture needs the game's data: on the Workspace tab click Run data dump (or run 'tyrant dump run').", FixAction.RefreshWorkspace);
        var index = TryIndex(ws) ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing,
            "Copying a base texture needs the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index').", FixAction.ReindexAssets);
        return Edit(p.Id, p.Revision, mod => mod.CopyBaseFile(install, index, Options.AssetReader, mod.SpeciesFor(ws, install, dump), p.Skin, p.Sex, p.Slot),
            $"'{p.Skin}' {p.Sex} {p.Slot} is now a copy of the base skin's texture, to edit");
    }

    [RpcMethod("mods.setThumbnail")]
    public ModDetail SetThumbnail(ModSetThumbnailParams p) => Edit(p.Id, p.Revision, mod => mod.SetThumbnail(p.Skin, p.Png), $"set the thumbnail of '{p.Skin}'");

    [RpcMethod("mods.removeReplacement")]
    public ModDetail RemoveReplacement(ModRemoveReplacementParams p) => Edit(p.Id, p.Revision, mod => mod.RemoveReplacement(p.Texture), $"no longer replaces {p.Texture}");

    [RpcMethod("mods.colorPreview")]
    public ModPreviewFiles ColorPreview(ModColorPreviewParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        return new ModPreviewFiles(Images(install).ColorPreview(mod, p.Skin, p.Colors, p.Variant, p.Sex, p.Seed, p.Count, Math.Clamp(p.Size, 16, 512),
            PreviewsDir(ws, "colors"), TryIndex(ws), TrySpeciesFor(ws, install, mod)));
    }

    [RpcMethod("mods.sampleColors")]
    public ModSampledColors SampleColors(ModSampleColorsParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var skin = mod.Skin(p.Skin);
        Tyrant.Framework.Core.SkinColors? colors;
        try
        {
            colors = string.IsNullOrWhiteSpace(p.Colors) ? skin.Colors : Tyrant.Framework.Core.SkinColors.Parse(Tyrant.Framework.Core.Json.Parse(p.Colors), p.Skin);
        }
        catch (Exception ex) when (ex is Tyrant.Framework.Core.ManifestException or FormatException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"These colours cannot be previewed: {ex.Message}");
        }
        var based = MorphColors.Variants.Contains(p.Variant) ? MorphColors.Of(BaseSkin(TrySpeciesFor(ws, install, mod), skin), p.Sex, p.Variant) : null;
        var (set, tint) = Tyrant.Core.Mods.ColorPreview.For(colors, p.Variant, based);
        var s = Tyrant.Core.Mods.ColorPreview.Sample(set, tint, new Random(p.Seed * 7919)); // the strip's first animal (ModImages)
        return new ModSampledColors(s.A?.ToString(), s.B?.ToString(), s.Secondary?.ToString(), s.Eye?.ToString(),
            s.Strength, s.Softness, s.Hue, s.Saturation, s.Value);
    }

    [RpcMethod("mods.skinModel")]
    public ModSkinModel SkinModel(ModSkinModelParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var skin = mod.Skin(p.Skin);
        return ShownModel(ws, install, mod, skin.Species, skin.Species, () => OwnModelOf(mod, skin.Species, skin.Model is not null ? skin.Id : null, p.Lod, p.Strict),
            (species, index, slot) => MapFile(ws, install, index, mod, skin, BaseSkin(species, skin), p.Sex, slot));
    }

    /// <summary>
    /// The 3D view's model: the species prefab and one map per slot from <paramref name="mapOf"/> (species, index, slot).
    /// Shared by skins and custom species; needs the data dump and the asset index.
    /// </summary>
    private ModSkinModel ShownModel(Workspace ws, GameInstall install, ModProject mod, string speciesKey, string modelName, Func<ModSkinOwnModel?> own,
        Func<IReadOnlyList<SpeciesSkins>, AssetIndex, string, string?> mapOf)
    {
        var species = TrySpeciesFor(ws, install, mod) ?? throw new TyrantException(TyrantErrorCode.DataMissing,
            "The 3D view needs the game's data: on the Workspace tab click Run data dump (or run 'tyrant dump run').", FixAction.RefreshWorkspace);
        var index = TryIndex(ws) ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing,
            "The 3D view needs the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index').", FixAction.ReindexAssets);
        var guid = species.FirstOrDefault(s => s.SpeciesId == speciesKey)?.PrefabGuid;
        var prefab = (guid is null ? null : index.Assets.FirstOrDefault(a => a.Type == "GameObject" && string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase)))
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound,
                $"'{modelName}' has no model in the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index') and try again.", FixAction.ReindexAssets);
        var maps = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in new[] { "diffuse", "normal", "extra", "pattern" })
            if (mapOf(species, index, slot) is { } file) maps[slot] = file;
        return new ModSkinModel(prefab.Ref, maps, own());
    }

    /// <summary>
    /// The model the skin wears in this mod (its own, else the mod's species replacement) at <paramref name="lod"/> (default the first; past the
    /// last, the last; below the first, the first); null when it is the game's or not built. <paramref name="strict"/> throws why it is not built instead of returning null.
    /// </summary>
    private ModSkinOwnModel? OwnModelOf(ModProject mod, string species, string? skinId, int? lod = null, bool strict = false)
    {
        if (skinId is null && !mod.Manifest.Models.Any(m => m.Target == species)) return null;
        try
        {
            var preview = ModelPreview(new ModModelPreviewParams(mod.Id, species, skinId));
            // The game's animal-shader materials wear the skin's maps (the model page shows the game's own textures instead).
            if (preview.Lods.Count == 0)
            {
                if (strict) throw new TyrantException(TyrantErrorCode.ModInvalid, "The model has no built levels of detail to show: fix its problems (see Check), then click Rebuild LODs.");
                return null;
            }
            return new ModSkinOwnModel(preview.Lods[Math.Clamp(lod ?? 0, 0, preview.Lods.Count - 1)].File, preview.Materials.Select(m => m with { Skinnable = m.Animal }).ToList());
        }
        catch (TyrantException) when (!strict)
        {
            return null; // not built yet (its page says why): the game's model is shown meanwhile
        }
    }

    /// <summary>The skin's own PNG for the slot, else the base skin's texture (written once to the preview cache). Infants use the male infant slots, then the adult ones.</summary>
    private string? MapFile(Workspace ws, GameInstall install, AssetIndex index, ModProject mod, Tyrant.Framework.Core.SkinEntry skin, VanillaSkin? based, string sex, string slot)
    {
        // One sex only, as in the game (infants wear the male skin's infant slots).
        var inherited = Tyrant.Framework.Core.StartingSkins.InheritsFrom(mod.Manifest, skin, out var starting) ? starting : null;
        var source = SkinMaps.Source(skin, mod.Dir, based, slot, sex == "male" ? "male-only" : sex, inherited);
        if (source is not null && source.StartsWith("file:", StringComparison.Ordinal)) return OwnMapCopy(ws, mod, Path.GetRelativePath(mod.Dir, source[5..]));
        return MapFromSource(ws, install, index, source);
    }

    /// <summary>A game texture source ("guid:...") as a PNG in the preview cache; null when there is none.</summary>
    private string? MapFromSource(Workspace ws, GameInstall install, AssetIndex index, string? source) =>
        source is null ? null : SkinMaps.Write(source, install, index, Options.AssetReader, Path.Combine(PreviewsDir(ws, "skin-model"), $"{source[5..]}.png"));

    /// <summary>
    /// A copy of the skin's own PNG in the preview cache: the app may only load files there (the asset protocol's scope).
    /// Named after its path, size and write time, so a replaced file gets a new URL. Null when the file is missing or outside the mod.
    /// </summary>
    private static string? OwnMapCopy(Workspace ws, ModProject mod, string file)
    {
        var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
        if (!Tyrant.Framework.Core.ModPaths.IsInside(path, mod.Dir) || !File.Exists(path)) return null;
        var info = new FileInfo(path);
        var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..16].ToLowerInvariant();
        var copy = Path.Combine(PreviewsDir(ws, "skin-model"), $"own-{name}.png");
        if (!File.Exists(copy))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(path, copy, overwrite: true);
        }
        return copy;
    }

    [RpcMethod("mods.thumbnail")]
    public ModThumbnailResult Thumbnail(ModThumbnailParams p)
    {
        var (ws, install) = session.Current();
        return new ModThumbnailResult(Images(install).Thumbnail(ModProject.Open(ws, p.Id), p.File, Math.Clamp(p.Size, 16, 512), PreviewsDir(ws, "mods")));
    }

    private ModDetail Edit(string id, string revision, Action<ModProject> change, string what)
    {
        var (ws, _) = session.Current();
        var mod = ModProject.Open(ws, id, revision);
        change(mod);
        session.Log($"'{id}' {what}.");
        return DetailOf(ws, mod);
    }

    private ModDetail DetailOf(Workspace ws, ModProject mod)
    {
        var (_, install) = session.Current();
        var uses = SpeciesUse.Of(mod.Manifest);
        var others = uses.Count > 0 ? OtherModSpecies.Load(ws, Game.List(install), mod.Id) : null;
        var species = mod.Manifest.Skins.Count > 0 ? TrySpecies(ws) : null;
        if (species is not null) species = ModProject.WithSpecies(species, mod.Manifest, others?.Species);
        var m = mod.Manifest;
        return new ModDetail(m.Id, m.Name, m.Version, m.Author, m.Description, mod.Dir, mod.Revision(),
            File.ReadAllText(Path.Combine(mod.Dir, Tyrant.Framework.Core.ModManifest.FileName)),
            m.Replace.Select(r => new ModReplacementDto(r.Texture, r.Key, r.Guid, r.File)).ToList(),
            m.Skins.Select(s =>
            {
                var based = BaseSkin(species, s);
                var shader = based is null ? null : ShaderSlotsOf(ws, species, s.Species);
                return new ModSkinDto(s.Id, s.Key(m.Id), s.Species, s.Name, s.Base, s.Thumbnail, s.Male, s.Female,
                    s.Colors is null ? null : Tyrant.Framework.Core.Json.Write(s.Colors.ToJson()),
                    based is null ? null : ShaderSlots.Shown(based.Male.Keys.ToList(), shader),
                    based is null ? null : ShaderSlots.Shown(based.Female.Keys.ToList(), shader), s.Model, ModRigBone.Of(s.Rig), s.Group, s.Portraits,
                    ReferenceEquals(Tyrant.Framework.Core.StartingSkins.Of(m, s.Species), s), s.CopiedFrom?.Species, s.CopiedFrom?.Skin,
                    s.Materials is null ? null : Tyrant.Framework.Core.Json.Write(Tyrant.Framework.Core.MaterialOverrides.ToJson(s.Materials)));
            }).ToList(),
            m.Models.Where(x => x.HasModelOrRig).Select(x => x.File.Length == 0
                    ? new ModModelDto(x.Target, null, "", [], [], [], false, null, false, ModRigBone.Of(x.Rig))
                    : ModelDto(mod, x.Target, null, x.File))
                .Concat(m.Skins.Where(s => s.Model is not null).Select(s => ModelDto(mod, s.Species, s.Id, s.Model!)))
                .ToList(),
            m.Sounds.Select(SoundDto).ToList(), mod.FilesStamp(),
            PrefabFiles: mod.PrefabFiles(),
            PrefabTargets: mod.PrefabTargets().Select(t => new ModPrefabTargetDto(t.Overrides.Target, t.File, t.Overrides.Fields.Count)).ToList(),
            UsesSpeciesFrom: UsesOf(uses, others),
            SpeciesMaterials: SpeciesMaterialsOf(m),
            VanillaSkinMaterials: VanillaSkinMaterialsOf(m),
            MaterialFiles: mod.MaterialFileTexts(),
            SizeFiles: mod.SizeFiles(),
            SkinOrders: SkinOrdersOf(mod, install));
    }

    /// <summary>The shown picker order of every own species with a starting skin; null for none.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? SkinOrdersOf(ModProject mod, GameInstall install)
    {
        var orders = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var species in mod.Manifest.Species)
        {
            var key = species.Key(mod.Id);
            if (mod.ShownSkinOrder(key, SkinSlots.NumbersOf(install, key)) is { } order) orders[key] = order;
        }
        return orders.Count == 0 ? null : orders;
    }

    /// <summary>The other mods whose species the skins use, with whether each is here, installed and on; null for none.</summary>
    private static IReadOnlyList<ModSpeciesUseDto>? UsesOf(IReadOnlyList<SpeciesUseCount> uses, OtherMods? others) =>
        uses.Count == 0 ? null : uses.Select(u => others is not null && others.Mods.TryGetValue(u.ModId, out var s)
            ? new ModSpeciesUseDto(u.ModId, s.Name, u.Skins, s.InWorkspace, s.Installed, s.Enabled)
            : new ModSpeciesUseDto(u.ModId, null, u.Skins, false, false, false)).ToList();

    /// <summary>Game build + species → the slots its shader uses (null: unknown). Reading a prefab costs a bundle load, so it is cached.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>?> ShaderSlotCache = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<string>? ShaderSlotsOf(Workspace ws, IReadOnlyList<SpeciesSkins>? species, string speciesId)
    {
        var guid = species?.FirstOrDefault(s => s.SpeciesId == speciesId)?.PrefabGuid;
        if (guid is null || TryIndex(ws) is not { } index) return null;
        var prefab = index.Assets.FirstOrDefault(a => a.Type == "GameObject" && string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase));
        if (prefab is null) return null;
        var key = $"{index.Fingerprint?.BuildGuid}|{ws.Dir}|{prefab.Ref}";
        if (ShaderSlotCache.TryGetValue(key, out var known)) return known;
        try
        {
            var slots = ShaderSlots.FromMaterials(Options.AssetReader.ReadMaterials(session.Current().Install, prefab));
            ShaderSlotCache[key] = slots;
            return slots;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // the base skin's own list is shown, as before (not cached: the bundle may come back)
        }
    }

    private static VanillaSkin? BaseSkin(IReadOnlyList<SpeciesSkins>? species, Tyrant.Framework.Core.SkinEntry skin) => SkinMaps.BaseOf(species, skin);

    private static IReadOnlyList<SpeciesSkins>? TrySpecies(Workspace ws)
    {
        try
        {
            return SpeciesSkinsReader.Load(ws);
        }
        catch (TyrantException)
        {
            return null;
        }
    }

    /// <summary>The data dump's species plus the custom ones the mod's skins can be for (<see cref="ModProject.SpeciesFor"/>); null without the dump.</summary>
    private static IReadOnlyList<SpeciesSkins>? TrySpeciesFor(Workspace ws, GameInstall install, ModProject mod) =>
        TrySpecies(ws) is { } dump ? mod.SpeciesFor(ws, install, dump) : null;

    private static AssetIndex? TryIndex(Workspace ws)
    {
        try
        {
            return AssetIndex.Load(AssetIndex.PathIn(ws));
        }
        catch (TyrantException)
        {
            return null;
        }
    }
}
