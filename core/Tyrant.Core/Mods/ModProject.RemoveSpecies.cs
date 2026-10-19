using Tyrant.Core.Blender;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Another workspace mod's skins, models and sounds for a custom species.</summary>
public sealed record SpeciesUser(string ModId, string Name, int Skins, int Models, int Sounds);

/// <summary>What removing one of this mod's species takes with it (skin names), and which other mods use it.</summary>
public sealed record SpeciesRemoval(string Key, string Name, IReadOnlyList<string> Skins, int Models, int Sounds, bool PrefabSettings,
    IReadOnlyList<SpeciesUser> UsedBy);

public sealed partial class ModProject
{
    private SpeciesEntry SpeciesEntryOf(string speciesId) =>
        Manifest.Species.FirstOrDefault(s => s.Id == speciesId)
        ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{Id}' has no species '{speciesId}'. Its species: {string.Join(", ", Manifest.Species.Select(s => s.Id))}.");

    /// <summary>The species' skins, models (by target), sounds (for it or one of its skins) and prefab settings file in this mod.</summary>
    private (List<SkinEntry> Skins, List<ModelReplacement> Models, List<SoundReplacement> Sounds, string? Prefab) PartsOf(string key)
    {
        var skins = Manifest.Skins.Where(s => s.Species == key).ToList();
        var skinKeys = skins.Select(s => s.Key(Id)).ToHashSet(StringComparer.Ordinal);
        var models = Manifest.Models.Where(m => string.Equals(m.Target, key, StringComparison.Ordinal)).ToList();
        var sounds = Manifest.Sounds.Where(s => s.Species == key || (s.Skin is not null && skinKeys.Contains(s.Skin))).ToList();
        var prefab = PrefabTargets().FirstOrDefault(t => string.Equals(t.Overrides.Target, key, StringComparison.OrdinalIgnoreCase)).File;
        return (skins, models, sounds, prefab);
    }

    public SpeciesRemoval SpeciesRemovalOf(Workspace ws, string speciesId)
    {
        var entry = SpeciesEntryOf(speciesId);
        var key = entry.Key(Id);
        var (skins, models, sounds, prefab) = PartsOf(key);
        var users = new List<SpeciesUser>();
        foreach (var id in Ids(ws).Where(id => id != Id))
        {
            ModManifest m;
            try { m = Open(ws, id).Manifest; }
            catch (Exception ex) when (ex is TyrantException or IOException or UnauthorizedAccessException) { continue; } // its own page reports why
            var user = new SpeciesUser(id, m.Name, m.Skins.Count(s => s.Species == key), m.Models.Count(x => x.Target == key), m.Sounds.Count(s => s.Species == key));
            if (user.Skins + user.Models + user.Sounds > 0) users.Add(user);
        }
        return new SpeciesRemoval(key, entry.Name, skins.Select(s => s.Name).ToList(), models.Count, sounds.Count, prefab is not null, users);
    }

    /// <summary>
    /// Removes one of this mod's species: its animals.db row, its skins (the starting skin too), its models and rig edits, the
    /// sounds for it or its skins, and its prefab settings. With <paramref name="deleteFiles"/>, their files, its language files,
    /// icon, swatch, portraits, prefab bundle and Blender projects go to the Recycle Bin too, except files something else still
    /// uses. Saved animals of the species no longer load, as when its mod is removed.
    /// </summary>
    public RemovedFiles RemoveSpecies(string speciesId, bool deleteFiles)
    {
        var entry = SpeciesEntryOf(speciesId);
        var key = entry.Key(Id);
        var (skins, models, sounds, prefab) = PartsOf(key);
        var speciesMaterials = Manifest.SpeciesMaterials.GetValueOrDefault(key);

        // mod.json first: a species without skins is still a valid mod, skins whose species is gone are not.
        var (oldSkins, oldModels, oldSounds) = (Manifest.Skins.ToList(), Manifest.Models.ToList(), Manifest.Sounds.ToList());
        Manifest.Skins.RemoveAll(skins.Contains);
        Manifest.Models.RemoveAll(models.Contains);
        Manifest.Sounds.RemoveAll(sounds.Contains);
        Manifest.SpeciesMaterials.Remove(key);
        Save();
        var at = Manifest.Species.IndexOf(entry);
        try
        {
            Manifest.Species.RemoveAt(at);
            SaveSpecies();
        }
        catch
        {
            Manifest.Species.Insert(at, entry);
            Manifest.Skins.Clear(); Manifest.Skins.AddRange(oldSkins);
            Manifest.Models.Clear(); Manifest.Models.AddRange(oldModels);
            Manifest.Sounds.Clear(); Manifest.Sounds.AddRange(oldSounds);
            if (speciesMaterials is not null) Manifest.SpeciesMaterials[key] = speciesMaterials;
            Save();
            throw;
        }
        if (prefab is not null)
        {
            File.Delete(Path.Combine(PrefabFolder, prefab));
            RemoveEmptyPrefabFolder();
        }
        if (!deleteFiles) return new RemovedFiles([], []);

        var languageFiles = AnimalLocalization.Codes.SelectMany(code => entry.Localization.Values.Select(term => AnimalLocalization.FileFor(Dir, code, term)))
            .Select(path => Normal(Path.GetRelativePath(Dir, path))).ToList();
        var files = skins.SelectMany(s => FilesOf(s).Concat(ModSharing.ModelFilesOf(this, s.Model)))
            .Concat(models.SelectMany(EntryFiles))
            .Concat(MaterialFiles(speciesMaterials))
            .Concat(sounds.SelectMany(s => s.Files))
            .Concat(new[] { entry.Icon, entry.Swatch, entry.Prefab?.Bundle }.OfType<string>())
            .Concat(entry.Portraits.Values)
            .Concat(languageFiles);
        var removed = new List<string>();
        var kept = new List<string>();
        RecycleUnused(files, removed, kept);
        foreach (var skin in skins)
        {
            DeleteIfEmpty(Path.Combine(Dir, "skins", skin.Id, "portraits"));
            DeleteIfEmpty(Path.Combine(Dir, "skins", skin.Id));
            RecycleBlender(BlenderProjectWriter.FolderOf(WorkspaceDir, Id, key, skin.Id), skin.Id, key, removed, kept);
        }
        RecycleBlender(BlenderProjectWriter.FolderOf(WorkspaceDir, Id, key, null), null, key, removed, kept);
        DeleteEmptyMaterialFolders(skins.Select(s => s.Materials).Append(speciesMaterials).SelectMany(MaterialFiles));
        DeleteIfEmpty(Path.Combine(Dir, PrefabOverrides.Folder, entry.Id));
        RemoveEmptyPrefabFolder();
        var localization = Path.Combine(Dir, "localization");
        foreach (var code in AnimalLocalization.Codes)
            foreach (var term in entry.Localization.Values)
                for (var dir = Path.GetDirectoryName(AnimalLocalization.FileFor(Dir, code, term)); dir != null && dir.Length > localization.Length; dir = Path.GetDirectoryName(dir))
                    if (!DeleteIfEmpty(dir)) break;
        DeleteIfEmpty(localization);
        return new RemovedFiles(removed, kept);
    }

    /// <summary>Every file this mod still names (mod.json, its species' files and language files), normalised.</summary>
    private HashSet<string> StillNamed() =>
        new(NamedFiles()
            .Concat(Manifest.Species.SelectMany(s => s.Localization.Values).SelectMany(term =>
                AnimalLocalization.Codes.Select(code => Path.GetRelativePath(Dir, AnimalLocalization.FileFor(Dir, code, term)))))
            .Select(Normal), StringComparer.OrdinalIgnoreCase);

    /// <summary>Sends each file to the Recycle Bin unless the mod still names it (or it is outside the mod, or gone).</summary>
    private void RecycleUnused(IEnumerable<string> files, List<string> removed, List<string> kept)
    {
        var stillUsed = StillNamed();
        foreach (var file in files.Select(Normal).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(Path.Combine(Dir, file));
            if (stillUsed.Contains(file) || !ModPaths.IsInside(path, Dir) || !File.Exists(path)) continue;
            var why = TryRecycle(path);
            if (why is null) removed.Add(file);
            else kept.Add($"{file}: {why}");
        }
    }

    /// <summary>A Blender project folder to the Recycle Bin, when it is this mod's project for that species (and skin).</summary>
    private void RecycleBlender(string folder, string? skinId, string species, List<string> removed, List<string> kept)
    {
        if (!Directory.Exists(folder)) return;
        if (!BlenderProjectFile.Belongs(folder, Id, skinId, species))
        {
            kept.Add($"{folder}: it is not {(skinId is null ? "this species'" : "this skin's")} Blender project, so it was left alone.");
            return;
        }
        var why = TryRecycle(folder);
        if (why is null) removed.Add(folder);
        else kept.Add($"{folder}: {why}");
    }

    /// <summary>Deletes an empty folder; true when it is gone.</summary>
    private static bool DeleteIfEmpty(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return true;
            if (Directory.EnumerateFileSystemEntries(folder).Any()) return false;
            Directory.Delete(folder);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
