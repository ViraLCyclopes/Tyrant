using System.Security.Cryptography;
using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.ModelReplacements;
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
    /// for (an empty one clears it); null keeps the destination's rig edit.
    /// </summary>
    public ModelReport ReplaceModel(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string glbPath, string? target, string? prefabRef, string? skinId, Func<IModelConverter>? converter = null,
        IReadOnlyDictionary<string, RigOffset>? rig = null)
    {
        if (!File.Exists(glbPath)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{glbPath}' does not exist.");
        if (!string.Equals(Path.GetExtension(glbPath), ".fbx", StringComparison.OrdinalIgnoreCase))
            return ReplaceModelFrom(install, index, species, reader, glbPath, glbPath, target, prefabRef, skinId, rig);
        if (converter is null)
            throw new TyrantException(TyrantErrorCode.BlenderMissing,
                "FBX needs Blender: choose blender.exe on the Workspace tab (Blender card), or run 'tyrant blender set-path <blender.exe>'.");
        // Blender turns the FBX into a glb first; the mod keeps that glb, and the FBX stays the origin ("import again").
        var temp = Path.Combine(Path.GetTempPath(), $"tyrant-fbx-{Guid.NewGuid():N}.glb");
        try
        {
            converter().FbxToGlb(glbPath, temp);
            return ReplaceModelFrom(install, index, species, reader, temp, glbPath, target, prefabRef, skinId, rig);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private ModelReport ReplaceModelFrom(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string glbPath, string originPath, string? target, string? prefabRef, string? skinId, IReadOnlyDictionary<string, RigOffset>? rig)
    {
        var skin = skinId is null ? null : Skin(skinId);
        var (speciesId, prefab) = ResolveModelTarget(index, species, skin?.Species ?? target, skin is null ? prefabRef : null);
        var effective = Owned(rig ?? RigOf(speciesId, skinId));

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(glbPath)))[..8].ToLowerInvariant();
        var stem = TextureExporter.Sanitize(speciesId).ToLowerInvariant() + (skin is null ? "" : "-" + skin.Id);
        var file = $"{ModelsFolder}/{stem}-{hash}.glb";
        var destination = Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!string.Equals(Path.GetFullPath(glbPath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(glbPath, destination, overwrite: true);

        var copiedFromOutside = !string.Equals(Path.GetFullPath(glbPath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
        var report = ModelBuilder.Build(Dir, file, reader.ReadPrefabModel(install, prefab), copiedFromOutside ? Path.GetFullPath(originPath) : null, effective);
        if (report.Errors.Count > 0)
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"The model was not added: {string.Join(" ", report.Errors)}");

        if (skin is not null)
        {
            skin.Model = file;
            skin.Rig = effective;
        }
        else
        {
            Manifest.Models.RemoveAll(m => string.Equals(m.Target, speciesId, StringComparison.Ordinal));
            Manifest.Models.Add(new ModelReplacement { Target = speciesId, Key = prefab.ContainerPath, File = file, Rig = effective });
        }
        Save();
        return report;
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
        var rebuilt = new List<string>();
        foreach (var (speciesId, file) in ModelEntries())
        {
            var rig = RigOfFile(file);
            if (!File.Exists(Path.Combine(Dir, file)) || !ModelBuilder.IsStale(Dir, file, rig)) continue;
            var (_, prefab) = ResolveModelTarget(index, species, speciesId, null);
            ModelBuilder.Build(Dir, file, reader.ReadPrefabModel(install, prefab), null, rig);
            rebuilt.Add(file);
        }
        return rebuilt;
    }

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
