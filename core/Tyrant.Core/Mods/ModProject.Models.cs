using System.Security.Cryptography;
using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Models;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed partial class ModProject
{
    public const string ModelsFolder = "models";

    /// <summary>
    /// Adds (or replaces) a model: for a skin (<paramref name="skinId"/>), else for a species named by <paramref name="target"/>
    /// or by the prefab <paramref name="prefabRef"/> (Assets tab). The .glb (or an .fbx, converted by Blender through
    /// <paramref name="converter"/>) is copied under a content-hashed name, so an earlier model's files stay for undo; a
    /// model with errors is refused and mod.json is left as it was. <paramref name="rig"/>: the rig edit the model is made
    /// for (Send's; an empty one clears it). Without it the rig edit is read from the file (its .rig.json or its bones, see
    /// <see cref="PreviewModelRig"/>) and put on as <paramref name="rigChoice"/> says.
    /// <paramref name="ws"/>: checks that rig edit with the rig rules (errors refuse the model) and keeps converted FBX files.
    /// </summary>
    public ModelReport ReplaceModel(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string glbPath, string? target, string? prefabRef, string? skinId, Func<IModelConverter>? converter = null,
        IReadOnlyDictionary<string, RigOffset>? rig = null, ModelRigChoice rigChoice = ModelRigChoice.Auto, Workspace? ws = null)
    {
        species = WithSpecies(species, Manifest);
        return WithModelGlb(glbPath, converter, ws,
            glb => ReplaceModelFrom(install, index, species, reader, glb, glbPath, target, prefabRef, skinId, rig, rigChoice, ws));
    }

    /// <summary>
    /// What adding the model would do to the destination's rig edit, saving nothing: the rig edit found in the file, the
    /// destination's current one, the question to ask (Auto's answer preselected), and whether Auto changes it.
    /// </summary>
    public ModelRigPreview PreviewModelRig(Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string modelPath, string? target, string? prefabRef, string? skinId, Func<IModelConverter>? converter)
    {
        species = WithSpecies(species, Manifest);
        return WithModelGlb(modelPath, converter, ws, glb =>
        {
            var skin = skinId is null ? null : Skin(skinId);
            var (speciesId, prefab) = ResolveModelTarget(index, species, skin?.Species ?? target, skin is null ? prefabRef : null);
            return PreviewRig(ws, install, index, species, reader, speciesId, skin, reader.ReadPrefabModel(install, prefab), glb, modelPath);
        });
    }

    /// <summary>The model as a glb: the file itself, or an FBX converted by Blender (kept in the workspace's cache when there is one).</summary>
    private static T WithModelGlb<T>(string modelPath, Func<IModelConverter>? converter, Workspace? ws, Func<string, T> use)
    {
        if (!File.Exists(modelPath)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{modelPath}' does not exist.");
        if (!string.Equals(Path.GetExtension(modelPath), ".fbx", StringComparison.OrdinalIgnoreCase)) return use(modelPath);
        if (converter is null)
            throw new TyrantException(TyrantErrorCode.BlenderMissing,
                "FBX needs Blender: choose blender.exe on the Workspace tab (Blender card), or run 'tyrant blender set-path <blender.exe>'.");
        // Blender turns the FBX into a glb first; the mod keeps that glb, and the FBX stays the origin ("import again").
        var temp = Path.Combine(Path.GetTempPath(), $"tyrant-fbx-{Guid.NewGuid():N}.glb");
        try
        {
            var blender = converter();
            if (ws is not null && blender is not CachingModelConverter) blender = new CachingModelConverter(blender, Path.Combine(ws.CacheDir, "fbx-glb"));
            blender.FbxToGlb(modelPath, temp);
            return use(temp);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private ModelReport ReplaceModelFrom(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string glbPath, string originPath, string? target, string? prefabRef, string? skinId, IReadOnlyDictionary<string, RigOffset>? rig,
        ModelRigChoice rigChoice, Workspace? ws)
    {
        var skin = skinId is null ? null : Skin(skinId);
        var (speciesId, prefab) = ResolveModelTarget(index, species, skin?.Species ?? target, skin is null ? prefabRef : null);
        var game = reader.ReadPrefabModel(install, prefab);
        var effective = Owned(rig ?? RigOf(speciesId, skinId));
        if (rig is null)
        {
            ModelRigPreview? preview = null;
            try
            {
                preview = PreviewRig(null, install, index, species, reader, speciesId, skin, game, glbPath, originPath);
            }
            catch (TyrantException ex) when (ex.Code == TyrantErrorCode.ModInvalid)
            {
                // the file cannot be read: the build below says why
            }
            if (preview is not null)
            {
                effective = Owned(Choose(preview.Reading, preview.Current, rigChoice));
                // Checked before anything is written, as Send does.
                if (ws is not null && effective is not null
                    && RigProblems(ws, install, index, species, reader, speciesId, skin, effective).Errors is { Count: > 0 } rigErrors)
                {
                    if (RigEdit.Hash(effective) != RigEdit.Hash(preview.Current))
                        throw new TyrantException(TyrantErrorCode.ModInvalid, $"The model was not added: {string.Join(" ", rigErrors)}");
                    var clear = skin is null ? $"tyrant mod rig {Id} \"{speciesId}\" --clear" : $"tyrant mod rig {Id} \"{speciesId}\" --skin {skin.Id} --clear";
                    throw new TyrantException(TyrantErrorCode.ModInvalid,
                        $"The model was not added: the rig edit already on {(skin is null ? speciesId : $"skin '{skin.Id}'")} breaks a rule, not the model. {string.Join(" ", rigErrors)} "
                        + $"Clear it (Clear rig edit on the model page, or '{clear}'), then add the model again.");
                }
            }
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(glbPath)))[..8].ToLowerInvariant();
        var stem = TextureExporter.Sanitize(speciesId).ToLowerInvariant() + (skin is null ? "" : "-" + skin.Id);
        var file = $"{ModelsFolder}/{stem}-{hash}.glb";
        var destination = Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!string.Equals(Path.GetFullPath(glbPath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(glbPath, destination, overwrite: true);

        var copiedFromOutside = !string.Equals(Path.GetFullPath(glbPath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
        var report = ModelBuilder.Build(Dir, file, game, copiedFromOutside ? Path.GetFullPath(originPath) : null, effective);
        if (report.Errors.Count > 0)
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"The model was not added: {string.Join(" ", report.Errors)}");

        if (skin is not null)
        {
            skin.Model = file;
            skin.Rig = effective;
        }
        else
        {
            var previous = Manifest.Models.FirstOrDefault(m => string.Equals(m.Target, speciesId, StringComparison.Ordinal));
            // The head-look correction belongs to the rig edit: it stays while the rig edit does.
            var headRig = previous?.HeadRig is { } kept && kept.RigHash == RigEdit.Hash(effective) ? kept : null;
            Manifest.Models.RemoveAll(m => string.Equals(m.Target, speciesId, StringComparison.Ordinal));
            Manifest.Models.Add(new ModelReplacement { Target = speciesId, Key = prefab.ContainerPath, File = file, Rig = effective, HeadRig = headRig });
        }
        Save();
        return report;
    }

    /// <summary>
    /// Reads the glb's rig edit (the glb made from <paramref name="originPath"/>, whose .rig.json counts) for the destination;
    /// with <paramref name="ws"/>, the rig rules' errors and warnings for the rig edit Auto would save.
    /// </summary>
    private ModelRigPreview PreviewRig(Workspace? ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string speciesId, SkinEntry? skin, PrefabModel game, string glbPath, string originPath)
    {
        var current = Owned(RigOf(speciesId, skin?.Id));
        var file = GlbModelReader.ReadSkeleton(glbPath, game);
        var warnings = new List<string>(file.LodWarnings);
        var rigFilePath = ModelRigReader.RigFilePathFor(originPath);
        var rigFileName = rigFilePath is null ? null : Path.GetFileName(rigFilePath);
        Dictionary<string, RigOffset>? rigFile = null;
        if (rigFilePath is not null)
        {
            try
            {
                rigFile = RigEdit.Parse(Json.Parse(File.ReadAllText(rigFilePath)), rigFileName!);
            }
            catch (Exception ex) when (ex is ManifestException or FormatException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{rigFileName} could not be read ({ex.Message}), so the rig edit was read from the model's bones.");
            }
        }
        // Named as the app shows the species: this mod's own species by its name, a game species by its display name.
        var name = Manifest.Species.FirstOrDefault(s => s.Key(Id) == speciesId && s.Name.Length > 0)?.Name
            ?? species.FirstOrDefault(s => string.Equals(s.SpeciesId, speciesId, StringComparison.Ordinal))?.Name ?? speciesId;
        var reading = ModelRigReader.Read(file, game, current, rigFile, name, rigFileName);
        warnings.AddRange(reading.Warnings);

        var question = reading.Source switch
        {
            _ when current is null => ModelRigQuestion.None,
            RigSource.Saved => ModelRigQuestion.None,
            RigSource.GameSkeleton => ModelRigQuestion.KeepOrRemove,
            _ => RigEdit.Hash(reading.Rig) != RigEdit.Hash(current) ? ModelRigQuestion.UseOrKeep : ModelRigQuestion.None,
        };
        var auto = Owned(Choose(reading, current, ModelRigChoice.Auto));
        var errors = new List<string>();
        if (ws is not null && auto is not null)
        {
            var (rigErrors, rigWarnings) = RigProblems(ws, install, index, species, reader, speciesId, skin, auto);
            errors.AddRange(rigErrors);
            warnings.AddRange(rigWarnings);
        }
        return new ModelRigPreview(reading, current, question, RigEdit.Hash(auto) != RigEdit.Hash(current), errors, warnings);
    }

    /// <summary>
    /// The rig edit to save. Auto: the file's when it has one (a different current one is replaced), the current one when
    /// the file has the game's skeleton or matches the current one. UseFile: the file's, even none. KeepCurrent: the current one.
    /// </summary>
    private static IReadOnlyDictionary<string, RigOffset>? Choose(RigReading reading, IReadOnlyDictionary<string, RigOffset>? current, ModelRigChoice choice) =>
        choice switch
        {
            ModelRigChoice.UseFile => reading.Rig,
            ModelRigChoice.KeepCurrent => current,
            _ => reading.Source is RigSource.GameSkeleton or RigSource.Saved ? current : reading.Rig,
        };

    /// <summary>The rig rules (Send's) for a rig edit on the destination's new model.</summary>
    private static (List<string> Errors, List<string> Warnings) RigProblems(Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species,
        IAssetReader reader, string speciesId, SkinEntry? skin, IReadOnlyDictionary<string, RigOffset> rig)
    {
        var info = Rigging.RigInfoService.For(ws, install, index, species, reader, speciesId);
        var where = skin is null ? $"Rig edit of {speciesId}" : $"Skin '{skin.Id}'";
        return Rigging.RigRules.Problems(rig, info, hasModel: true, where, perSkin: skin is not null);
    }

    /// <summary>Stops replacing a model (the files stay, so undo can bring it back); a rig edit stays, on the game's mesh.</summary>
    public void RemoveModel(string target, string? skinId)
    {
        if (skinId is not null)
        {
            var skin = Skin(skinId);
            if (skin.Model is null) throw new TyrantException(TyrantErrorCode.TargetNotFound, $"Skin '{skin.Name}' has no model of its own.");
            skin.Model = null;
        }
        else
        {
            var entry = Manifest.Models.FirstOrDefault(m => string.Equals(m.Target, target, StringComparison.Ordinal) && m.File.Length > 0)
                ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{Id}' does not replace the model of '{target}'.");
            if (entry.Rig is { Count: > 0 }) entry.File = "";
            else Manifest.Models.Remove(entry);
        }
        Save();
    }

    /// <summary>The rig edit of a skin (its own), or of a species (its entry); null for none.</summary>
    public IReadOnlyDictionary<string, RigOffset>? RigOf(string speciesId, string? skinId) =>
        skinId is not null ? Skin(skinId).Rig
        : Manifest.Models.FirstOrDefault(m => string.Equals(m.Target, speciesId, StringComparison.Ordinal))?.Rig;

    /// <summary>The rig edit of the entry (species or skin) whose model is that .glb; null for none.</summary>
    public IReadOnlyDictionary<string, RigOffset>? RigOfFile(string file) =>
        (IReadOnlyDictionary<string, RigOffset>?)Manifest.Models.FirstOrDefault(m => m.File.Length > 0 && m.File == file)?.Rig
        ?? Manifest.Skins.FirstOrDefault(s => s.Model == file)?.Rig;

    /// <summary>
    /// Sets (or, with null or empty, clears) a species' or a skin's rig edit and rebuilds its model, if it has one, for the
    /// new skeleton (returned; null when there is no model). A species rig edit without a model is an entry with no file.
    /// </summary>
    public ModelReport? SetRig(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string speciesId, string? skinId, IReadOnlyDictionary<string, RigOffset>? rig)
    {
        species = WithSpecies(species, Manifest);
        var skin = skinId is null ? null : Skin(skinId);
        var (id, prefab) = ResolveModelTarget(index, species, skin?.Species ?? speciesId, null);
        var owned = Owned(rig);
        string? file;
        if (skin is not null)
        {
            skin.Rig = owned;
            file = skin.Model;
        }
        else
        {
            var entry = Manifest.Models.FirstOrDefault(m => string.Equals(m.Target, id, StringComparison.Ordinal));
            if (entry is null && owned is not null)
                Manifest.Models.Add(entry = new ModelReplacement { Target = id, Key = prefab.ContainerPath, File = "" });
            if (entry is not null)
            {
                entry.Rig = owned;
                if (entry.File.Length == 0 && owned is null) Manifest.Models.Remove(entry);
            }
            file = entry is { File.Length: > 0 } ? entry.File : null;
        }
        ModelReport? report = null;
        if (file is not null && IsInsideMod(file) && File.Exists(Path.Combine(Dir, file)))
            report = ModelBuilder.Build(Dir, file, reader.ReadPrefabModel(install, prefab), null, owned);
        Save();
        return report;
    }

    /// <summary>The mod's own copy of a rig edit; null for none or an empty one.</summary>
    private static Dictionary<string, RigOffset>? Owned(IReadOnlyDictionary<string, RigOffset>? rig) =>
        rig is { Count: > 0 } ? new Dictionary<string, RigOffset>(rig, StringComparer.Ordinal) : null;

    /// <summary>Rebuilds every model whose .glb changed since it was built; returns the .glb files rebuilt.</summary>
    public IReadOnlyList<string> RebuildStaleModels(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader)
    {
        species = WithSpecies(species, Manifest);
        var rebuilt = new List<string>();
        foreach (var (speciesId, file) in ModelEntries())
        {
            var rig = RigOfFile(file);
            if (!File.Exists(Path.Combine(Dir, file))) continue;
            PrefabModel? game = null;
            PrefabModel Game() => game ??= reader.ReadPrefabModel(install, ResolveModelTarget(index, species, speciesId, null).Prefab);
            if (!ModelBuilder.IsStale(Dir, file, rig, Game))
            {
                // Built before the top-bone fix, with a rig edit it does not change: noted, so the game is not read for it again.
                if (game is not null) ModelBuilder.MarkBindsCurrent(Dir, file);
                continue;
            }
            ModelBuilder.Build(Dir, file, Game(), null, rig);
            rebuilt.Add(file);
        }
        return rebuilt;
    }

    /// <summary>
    /// Measures how each species-wide rig edit changes its species' head-look boom and keeps it on the entry ("headRig"),
    /// which the framework puts onto the species. Run after a rig edit is set and before install (mods made before this).
    /// quadrupedalOf gives a game species' locomotion from the data dump (null: unknown, the entry is left as it is).
    /// True when an entry changed (the mod is saved then).
    /// </summary>
    public bool RefreshHeadRigs(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        Func<string, bool?> quadrupedalOf)
    {
        species = WithSpecies(species, Manifest);
        var changed = false;
        foreach (var entry in Manifest.Models)
        {
            HeadRigCorrection? correction = null;
            if (entry.Rig is { Count: > 0 })
            {
                if (entry.HeadRig is { } measured && measured.RigHash == RigEdit.Hash(entry.Rig)) continue; // measured for this rig edit
                // A custom species walks like its template.
                var template = Manifest.Species.FirstOrDefault(s => s.Key(Id) == entry.Target)?.Base ?? entry.Target;
                if (quadrupedalOf(template) is not { } quadrupedal) continue;
                try
                {
                    var (_, prefab) = ResolveModelTarget(index, species, entry.Target, null);
                    correction = Rigging.HeadRigMeasure.For(reader.ReadPrefabModel(install, prefab), entry.Rig, quadrupedal);
                }
                catch (Exception ex) when (ex is TyrantException or ArgumentException or InvalidDataException)
                {
                    continue; // the prefab cannot be read or measured here: keep what the entry has
                }
            }
            if (SameHeadRig(entry.HeadRig, correction)) continue;
            entry.HeadRig = correction;
            changed = true;
        }
        if (changed) Save();
        return changed;
    }

    /// <summary><see cref="RefreshHeadRigs(GameInstall, AssetIndex, IReadOnlyList{SpeciesSkins}, IAssetReader, Func{string, bool?})"/> with the locomotion from the workspace's data dump.</summary>
    public bool RefreshHeadRigs(Workspaces.Workspace ws, GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader)
    {
        if (!Manifest.Models.Any(m => m.Rig is { Count: > 0 } || m.HeadRig is not null)) return false;
        var known = new Dictionary<string, bool?>(StringComparer.Ordinal);
        return RefreshHeadRigs(install, index, species, reader, id => known.TryGetValue(id, out var q) ? q : known[id] = SpeciesDatabase.IsQuadrupedal(ws, id));
    }

    private static bool SameHeadRig(HeadRigCorrection? a, HeadRigCorrection? b) =>
        a is null || b is null ? a is null && b is null
        : Math.Abs(a.LengthScale - b.LengthScale) < 1e-5f
          && Math.Abs(RigQuaternion.Dot(a.BoomTurn, b.BoomTurn)) > 1f - 1e-6f
          && Math.Abs(RigQuaternion.Dot(a.HeadTurn, b.HeadTurn)) > 1f - 1e-6f;

    /// <summary>Every (species, .glb file) the mod uses whose file lies inside the mod: species replacements and skin models.</summary>
    public IEnumerable<(string Species, string File)> ModelEntries() => AllModelEntries().Where(e => IsInsideMod(e.File));

    /// <summary>
    /// Every model entry as mod.json writes it, including files that point outside the mod (Check reports those); a rig edit
    /// without a model of its own is not a model entry.
    /// </summary>
    public IEnumerable<(string Species, string File)> AllModelEntries() =>
        Manifest.Models.Where(m => m.File.Length > 0).Select(m => (m.Target, m.File))
            .Concat(Manifest.Skins.Where(s => s.Model is not null).Select(s => (s.Species, s.Model!)));

    public bool IsInsideMod(string file) => ModPaths.IsInside(Path.Combine(Dir, file), Dir);

    /// <summary>The species and its prefab: from the prefab (Assets tab) or from the species id (data dump → animalRef → index).</summary>
    public static (string Species, AssetRecord Prefab) ResolveModelTarget(AssetIndex index, IReadOnlyList<SpeciesSkins> species, string? target, string? prefabRef)
    {
        if (prefabRef is not null)
        {
            var prefab = index.Resolve(prefabRef, "GameObject");
            var owner = prefab.Guid is null ? null : species.FirstOrDefault(s => string.Equals(s.PrefabGuid, prefab.Guid, StringComparison.OrdinalIgnoreCase));
            if (owner is null)
                throw new TyrantException(TyrantErrorCode.ModInvalid,
                    $"'{prefab.Name}' is not an animal's model. Replacing objects (fences, scenery, buildings) is not supported yet: objects come in a later update.");
            return (owner.SpeciesId, prefab);
        }
        var entry = species.FirstOrDefault(s => string.Equals(s.SpeciesId, target, StringComparison.OrdinalIgnoreCase))
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound,
                $"Species '{target}' is not in the game data. Check the spelling (Species tab), or run the data dump (Workspace → Run data dump, or 'tyrant dump run').");
        var found = entry.PrefabGuid is null ? null
            : index.Assets.FirstOrDefault(a => a.Type == "GameObject" && string.Equals(a.Guid, entry.PrefabGuid, StringComparison.OrdinalIgnoreCase));
        return (entry.SpeciesId, found ?? throw new TyrantException(TyrantErrorCode.TargetNotFound,
            $"'{entry.SpeciesId}' has no model in the asset index: on the Workspace tab click Index assets (or run 'tyrant assets index')."));
    }
}

/// <summary>
/// How the rig edit a model file carries goes on its destination. Auto: the file's rig edit when it has one, else the
/// destination keeps its own. UseFile: the file's, even the game's skeleton (which removes the rig edit). KeepCurrent: the
/// destination's own, whatever the file says.
/// </summary>
public enum ModelRigChoice { Auto, UseFile, KeepCurrent }

/// <summary>
/// What to ask before adding a model. KeepOrRemove: the file has the game's skeleton and the destination a rig edit (Auto
/// keeps it). UseOrKeep: the file has another rig edit than the destination's (Auto uses the file's).
/// </summary>
public enum ModelRigQuestion { None, KeepOrRemove, UseOrKeep }

/// <summary>
/// What adding a model does to its destination's rig edit. Current: the destination's (null for none). Changes: Auto saves
/// another rig edit than Current. Errors and Warnings: the reading's, the file's LODs', and the rig rules' for Auto's rig edit.
/// </summary>
public sealed record ModelRigPreview(RigReading Reading, IReadOnlyDictionary<string, RigOffset>? Current, ModelRigQuestion Question, bool Changes,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
