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
    /// model with errors is refused and mod.json is left as it was.
    /// </summary>
    public ModelReport ReplaceModel(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string glbPath, string? target, string? prefabRef, string? skinId, Func<IModelConverter>? converter = null)
    {
        if (!File.Exists(glbPath)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{glbPath}' does not exist.");
        if (!string.Equals(Path.GetExtension(glbPath), ".fbx", StringComparison.OrdinalIgnoreCase))
            return ReplaceModelFrom(install, index, species, reader, glbPath, glbPath, target, prefabRef, skinId);
        if (converter is null)
            throw new TyrantException(TyrantErrorCode.BlenderMissing,
                "FBX needs Blender: choose blender.exe on the Workspace tab (Blender card), or run 'tyrant blender set-path <blender.exe>'.");
        // Blender turns the FBX into a glb first; the mod keeps that glb, and the FBX stays the origin ("import again").
        var temp = Path.Combine(Path.GetTempPath(), $"tyrant-fbx-{Guid.NewGuid():N}.glb");
        try
        {
            converter().FbxToGlb(glbPath, temp);
            return ReplaceModelFrom(install, index, species, reader, temp, glbPath, target, prefabRef, skinId);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private ModelReport ReplaceModelFrom(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader,
        string glbPath, string originPath, string? target, string? prefabRef, string? skinId)
    {
        var skin = skinId is null ? null : Skin(skinId);
        var (speciesId, prefab) = ResolveModelTarget(index, species, skin?.Species ?? target, skin is null ? prefabRef : null);

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(glbPath)))[..8].ToLowerInvariant();
        var stem = TextureExporter.Sanitize(speciesId).ToLowerInvariant() + (skin is null ? "" : "-" + skin.Id);
        var file = $"{ModelsFolder}/{stem}-{hash}.glb";
        var destination = Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!string.Equals(Path.GetFullPath(glbPath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(glbPath, destination, overwrite: true);

        var copiedFromOutside = !string.Equals(Path.GetFullPath(glbPath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
        var report = ModelBuilder.Build(Dir, file, reader.ReadPrefabModel(install, prefab), copiedFromOutside ? Path.GetFullPath(originPath) : null);
        if (report.Errors.Count > 0)
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"The model was not added: {string.Join(" ", report.Errors)}");

        if (skin is not null) skin.Model = file;
        else
        {
            Manifest.Models.RemoveAll(m => string.Equals(m.Target, speciesId, StringComparison.Ordinal));
            Manifest.Models.Add(new ModelReplacement { Target = speciesId, Key = prefab.ContainerPath, File = file });
        }
        Save();
        return report;
    }

    /// <summary>Stops replacing a model (the files stay, so undo can bring it back).</summary>
    public void RemoveModel(string target, string? skinId)
    {
        if (skinId is not null)
        {
            var skin = Skin(skinId);
            if (skin.Model is null) throw new TyrantException(TyrantErrorCode.TargetNotFound, $"Skin '{skin.Name}' has no model of its own.");
            skin.Model = null;
        }
        else if (Manifest.Models.RemoveAll(m => string.Equals(m.Target, target, StringComparison.Ordinal)) == 0)
            throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{Id}' does not replace the model of '{target}'.");
        Save();
    }

    /// <summary>Rebuilds every model whose .glb changed since it was built; returns the .glb files rebuilt.</summary>
    public IReadOnlyList<string> RebuildStaleModels(GameInstall install, AssetIndex index, IReadOnlyList<SpeciesSkins> species, IAssetReader reader)
    {
        var rebuilt = new List<string>();
        foreach (var (speciesId, file) in ModelEntries())
        {
            if (!File.Exists(Path.Combine(Dir, file)) || !ModelBuilder.IsStale(Dir, file)) continue;
            var (_, prefab) = ResolveModelTarget(index, species, speciesId, null);
            ModelBuilder.Build(Dir, file, reader.ReadPrefabModel(install, prefab));
            rebuilt.Add(file);
        }
        return rebuilt;
    }

    /// <summary>Every (species, .glb file) the mod uses whose file lies inside the mod: species replacements and skin models.</summary>
    public IEnumerable<(string Species, string File)> ModelEntries() => AllModelEntries().Where(e => IsInsideMod(e.File));

    /// <summary>Every model entry as mod.json writes it, including files that point outside the mod (Check reports those).</summary>
    public IEnumerable<(string Species, string File)> AllModelEntries() =>
        Manifest.Models.Select(m => (m.Target, m.File))
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
