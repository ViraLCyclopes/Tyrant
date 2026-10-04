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
public sealed class ModsMethods(StudioSession session, JobManager jobs)
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
        var result = ModChecker.ForGame(install).Check(checkedMod, TryIndex(ws), checkedMod.Manifest.Skins.Count > 0 ? TrySpecies(ws) : null);
        return new ModCheckReport(result.Errors, result.Warnings, result.MissingCutouts);
    }

    /// <summary>Copies the vanilla transparency back into the mod's colour PNGs that lost it (the workspace copy; install again after).</summary>
    [RpcMethod("mods.restoreCutouts")]
    public ModRestoreCutoutsResult RestoreCutouts(ModIdParams p)
    {
        var (ws, install) = session.Current();
        var index = AssetIndex.Load(AssetIndex.PathIn(ws)); // without it the game textures are unknown: say so, not "nothing to restore"
        var result = new CutoutRestorer(Cutouts.GamePixels(install, Options.AssetReader)).Restore(ModProject.Open(ws, p.Id), index, TrySpecies(ws));
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
            var check = ModChecker.ForGame(install).Check(mod, TryIndex(ws), mod.Manifest.Skins.Count > 0 ? TrySpecies(ws) : null);
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
            Game.Install(install, mod);
            session.Log($"Installed '{p.Id}' into the game.");
            return new ModInstallResult($"Installed '{p.Id}' into the game. Start the game to see it.", check.Warnings);
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
        var installed = game.List(install).ToDictionary(m => m.Id);
        var rows = new List<ModRow>();
        foreach (var id in ModProject.Ids(ws))
        {
            installed.TryGetValue(id, out var inGame);
            try
            {
                var mod = ModProject.Open(ws, id);
                rows.Add(new ModRow(id, mod.Manifest.Name, mod.Manifest.Version, mod.Manifest.Author, mod.Manifest.Replace.Count, mod.Manifest.Skins.Count,
                    Wire(game.StateOf(install, mod)), inGame?.Enabled, mod.Dir, null));
            }
            catch (TyrantException ex)
            {
                rows.Add(new ModRow(id, id, "", null, 0, 0, "notInstalled", inGame?.Enabled, Path.Combine(ModProject.RootOf(ws), id), ex.Message));
            }
            installed.Remove(id);
        }
        rows.AddRange(installed.Values.Select(m => new ModRow(m.Id, m.Name, m.Version, null, m.Replacements, m.Skins, "gameOnly", m.Enabled, m.Dir, m.Error)));
        return new ModsListResult(rows, ModLoaderInstaller.HasFramework(install));
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
            new SpeciesSkinsRow(s.SpeciesId, s.Vivarium, s.Skins.Select(k => new VanillaSkinRow(k.Index, k.Name, k.Male.Count > 0, k.Female.Count > 0)).ToList())).ToList());
    }

    [RpcMethod("mods.addSkin")]
    public ModsListResult AddSkin(ModAddSkinParams p)
    {
        var (ws, install) = session.Current();
        var entry = ModProject.Open(ws, p.Id).AddSkin(ws, install, AssetIndex.Load(AssetIndex.PathIn(ws)), Options.AssetReader, SpeciesSkinsReader.Load(ws),
            p.Species, p.Name, p.Base, new SkinTemplateOptions(p.Male, p.Female, p.Maps));
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
        Edit(p.Id, p.Revision, mod => mod.RemoveSkin(p.Skin, p.DeleteFiles), $"removed skin '{p.Skin}'{(p.DeleteFiles ? " and its files" : "")}");

    [RpcMethod("mods.setColors")]
    public ModDetail SetColors(ModSetColorsParams p) => Edit(p.Id, p.Revision, mod => mod.SetColors(p.Skin, p.Colors), $"set the colours of '{p.Skin}'");

    [RpcMethod("mods.setSkinFile")]
    public ModDetail SetSkinFile(ModSetSkinFileParams p) =>
        Edit(p.Id, p.Revision, mod => mod.SetSkinFile(p.Skin, p.Sex, p.Slot, p.Png),
            p.Png is null ? $"'{p.Skin}' {p.Sex} {p.Slot} uses the base skin's texture" : $"'{p.Skin}' {p.Sex} {p.Slot} uses {p.Png}");

    [RpcMethod("mods.setThumbnail")]
    public ModDetail SetThumbnail(ModSetThumbnailParams p) => Edit(p.Id, p.Revision, mod => mod.SetThumbnail(p.Skin, p.Png), $"set the thumbnail of '{p.Skin}'");

    [RpcMethod("mods.removeReplacement")]
    public ModDetail RemoveReplacement(ModRemoveReplacementParams p) => Edit(p.Id, p.Revision, mod => mod.RemoveReplacement(p.Texture), $"no longer replaces {p.Texture}");

    [RpcMethod("mods.saveManifest")]
    public ModDetail SaveManifest(ModSaveManifestParams p)
    {
        var (ws, _) = session.Current();
        var mod = ModProject.SaveManifest(ws, p.Id, p.Manifest, p.Revision);
        session.Log($"'{p.Id}': mod.json put back to an earlier version (undo/redo).");
        return DetailOf(ws, mod);
    }

    [RpcMethod("mods.colorPreview")]
    public ModPreviewFiles ColorPreview(ModColorPreviewParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        return new ModPreviewFiles(Images(install).ColorPreview(mod, p.Skin, p.Colors, p.Variant, p.Sex, p.Seed, p.Count, Math.Clamp(p.Size, 16, 512),
            PreviewsDir(ws, "colors"), TryIndex(ws), TrySpecies(ws)));
    }

    [RpcMethod("mods.sampleColors")]
    public ModSampledColors SampleColors(ModSampleColorsParams p)
    {
        var (ws, _) = session.Current();
        var skin = ModProject.Open(ws, p.Id).Skin(p.Skin);
        Tyrant.Framework.Core.SkinColors? colors;
        try
        {
            colors = string.IsNullOrWhiteSpace(p.Colors) ? skin.Colors : Tyrant.Framework.Core.SkinColors.Parse(Tyrant.Framework.Core.Json.Parse(p.Colors), p.Skin);
        }
        catch (Exception ex) when (ex is Tyrant.Framework.Core.ManifestException or FormatException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"These colours cannot be previewed: {ex.Message}");
        }
        var (set, tint) = Tyrant.Core.Mods.ColorPreview.For(colors, p.Variant);
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
        var species = TrySpecies(ws) ?? throw new TyrantException(TyrantErrorCode.DataMissing,
            "The 3D view needs the game's data: on the Workspace tab click Run data dump (or run 'tyrant dump run').", FixAction.RefreshWorkspace);
        var index = TryIndex(ws) ?? throw new TyrantException(TyrantErrorCode.AssetIndexMissing,
            "The 3D view needs the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index').", FixAction.ReindexAssets);
        var guid = species.FirstOrDefault(s => s.SpeciesId == skin.Species)?.PrefabGuid;
        var prefab = (guid is null ? null : index.Assets.FirstOrDefault(a => a.Type == "GameObject" && string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase)))
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound,
                $"'{skin.Species}' has no model in the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index') and try again.", FixAction.ReindexAssets);
        var based = BaseSkin(species, skin);
        var maps = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in new[] { "diffuse", "normal", "extra", "pattern" })
            if (MapFile(ws, install, index, mod, skin, based, p.Sex, slot) is { } file) maps[slot] = file;
        return new ModSkinModel(prefab.Ref, maps);
    }

    /// <summary>The skin's own PNG for the slot, else the base skin's texture (written once to the preview cache). Infants use the male infant slots, then the adult ones.</summary>
    private string? MapFile(Workspace ws, GameInstall install, AssetIndex index, ModProject mod, Tyrant.Framework.Core.SkinEntry skin, VanillaSkin? based, string sex, string slot)
    {
        var candidates = sex == "infant" ? new[] { "infant" + char.ToUpperInvariant(slot[0]) + slot[1..], slot } : [slot];
        // One sex only, as in the game (infants wear the male skin's infant slots).
        IReadOnlyDictionary<string, string>?[] own = [sex == "female" ? skin.Female : skin.Male];
        IReadOnlyDictionary<string, string>?[] vanilla = [sex == "female" ? based?.Female : based?.Male];
        foreach (var candidate in candidates)
        {
            if (own.Select(d => d?.GetValueOrDefault(candidate)).FirstOrDefault(f => !string.IsNullOrEmpty(f)) is { } file)
                return Path.GetFullPath(Path.Combine(mod.Dir, file));
            if (vanilla.Select(d => d?.GetValueOrDefault(candidate)).FirstOrDefault(g => !string.IsNullOrEmpty(g)) is { } textureGuid
                && index.Assets.FirstOrDefault(a => a.Type == "Texture2D" && string.Equals(a.Guid, textureGuid, StringComparison.OrdinalIgnoreCase)) is { } texture)
            {
                var png = Path.Combine(PreviewsDir(ws, "skin-model"), $"{texture.Guid}.png");
                if (!File.Exists(png))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(png)!);
                    Options.AssetReader.WriteTexture(install, texture, png);
                }
                return png;
            }
        }
        return null;
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
        var species = mod.Manifest.Skins.Count > 0 ? TrySpecies(ws) : null;
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
                    based is null ? null : ShaderSlots.Shown(based.Female.Keys.ToList(), shader));
            }).ToList());
    }

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

    private static VanillaSkin? BaseSkin(IReadOnlyList<SpeciesSkins>? species, Tyrant.Framework.Core.SkinEntry skin)
    {
        var target = species?.FirstOrDefault(s => s.SpeciesId == skin.Species);
        return target is null ? null
            : int.TryParse(skin.Base, out var number) ? target.Skins.FirstOrDefault(v => v.Index == number)
            : target.Skins.FirstOrDefault(v => string.Equals(v.Name, skin.Base, StringComparison.OrdinalIgnoreCase));
    }

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
