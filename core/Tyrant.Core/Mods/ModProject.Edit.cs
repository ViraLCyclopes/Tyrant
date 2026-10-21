using System.Security.Cryptography;
using Tyrant.Core.Blender;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>What a removal sent to the Recycle Bin, and what stayed with why ("&lt;path&gt;: &lt;reason&gt;").</summary>
public sealed record RemovedFiles(IReadOnlyList<string> Recycled, IReadOnlyList<string> Kept);

/// <summary>Editing a mod in place (the app's mod editor and the matching 'tyrant mod …' commands).</summary>
public sealed partial class ModProject
{
    /// <summary>Revision of the manifest, committed SQLite species data (including external WAL edits), language, prefab and material files.</summary>
    public string Revision() => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
        File.ReadAllText(Path.Combine(Dir, ModManifest.FileName)) + "\n" + AnimalDatabaseFile.Snapshot(AnimalDatabaseFile.Read(DatabasePath)) +
        string.Concat(AnimalLocalization.Codes.SelectMany(code => new[] { code + ".txt", code + ".json" }).Select(file =>
        {
            var path = Path.Combine(Dir, "localization", file);
            return "\n" + file + "\n" + (File.Exists(path) ? File.ReadAllText(path) : "");
        })) + string.Concat(Manifest.Species.SelectMany(s => s.Localization.Values).Distinct().OrderBy(term => term, StringComparer.Ordinal)
            .SelectMany(term => AnimalLocalization.Codes.Select(code =>
            {
                var path = AnimalLocalization.FileFor(Dir, code, term);
                return "\n" + code + "/" + term + "\n" + (File.Exists(path) ? File.ReadAllText(path) : "");
            }))) + string.Concat(PrefabFiles().Select(p => "\nprefabs/" + p.Key + "\n" + p.Value))
            + string.Concat(SizeFiles().Select(p => "\nsizes/" + p.Key + "\n" + p.Value))
        + string.Concat(MaterialFileTexts().Select(p => "\n" + p.Key + "\n" + p.Value)))));

    /// <summary>The three kinds of material folder under materials/ (<see cref="MaterialFolders"/>).</summary>
    private static readonly string[] MaterialKinds = ["skins", "species", "vanilla-skins"];

    /// <summary>
    /// Every materials/&lt;kind&gt;/&lt;folder&gt;/materials.json on disk, mod-relative path to its text, sorted: the editor's undo
    /// keeps them (not their images, as part A's undo did not).
    /// </summary>
    public IReadOnlyDictionary<string, string> MaterialFileTexts()
    {
        var texts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var kind in MaterialKinds)
        {
            var root = Path.Combine(Dir, MaterialFolders.Root, kind);
            if (!Directory.Exists(root)) continue;
            foreach (var folder in Directory.GetDirectories(root))
            {
                var file = Path.Combine(folder, MaterialFolders.FileName);
                if (File.Exists(file)) texts[$"{MaterialFolders.Root}/{kind}/{Path.GetFileName(folder)}/{MaterialFolders.FileName}"] = File.ReadAllText(file);
            }
        }
        return texts;
    }

    /// <summary>Whether a name is one MaterialFileTexts gives: materials/&lt;kind&gt;/&lt;one plain folder name&gt;/materials.json.</summary>
    private static bool IsMaterialFileName(string name)
    {
        var parts = name.Split('/');
        return parts.Length == 4 && parts[0] == MaterialFolders.Root && MaterialKinds.Contains(parts[1]) && parts[3] == MaterialFolders.FileName
               && parts[2].Length > 0 && MaterialFolders.Sanitize(parts[2]) == parts[2];
    }

    /// <summary>Opens the mod, refusing when its manifest or animal database changed since the editor's revision.</summary>
    public static ModProject Open(Workspace ws, string id, string? expectedRevision)
    {
        var mod = Open(ws, id);
        if (expectedRevision is not null && !string.Equals(mod.Revision(), expectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new TyrantException(TyrantErrorCode.ModChanged,
                $"'{id}' changed outside this editor (mod.json, animals.db, prefab or material settings files were edited elsewhere). It was reloaded; make your change again.");
        return mod;
    }

    /// <summary>Writes a whole mod.json (the editor's undo and redo) after checking it as strictly as Check does.</summary>
    public static ModProject SaveManifest(Workspace ws, string id, string json, string expectedRevision)
    {
        var mod = Open(ws, id, expectedRevision);
        ModManifest manifest;
        try
        {
            manifest = ModManifest.Parse(json);
        }
        catch (ManifestException ex)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"That version of '{id}' cannot be saved: {ex.Message}");
        }
        if (manifest.Id != id)
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"That mod.json belongs to '{manifest.Id}', not '{id}'.");
        manifest.Species.AddRange(mod.Manifest.Species);
        try
        {
            LoadMaterials(manifest, mod.Dir); // mod.json does not hold them: the folders stay as they are
        }
        catch (ManifestException ex)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"Mod '{id}' cannot be read: {ex.Message}");
        }
        var saved = new ModProject(mod.Dir, manifest);
        saved.Save();
        return saved;
    }

    /// <summary>
    /// The editor's undo and redo: mod.json (checked like SaveManifest), and the prefabs/ files and the materials.json files
    /// (<see cref="MaterialFileTexts"/>) exactly as they were, by name. Null <paramref name="materials"/> leaves the material
    /// files as they are.
    /// </summary>
    public static ModProject RestoreSnapshot(Workspace ws, string id, string manifestJson, IReadOnlyDictionary<string, string> prefabs, string expectedRevision,
        IReadOnlyDictionary<string, string>? materials)
    {
        // Only the names are checked: undo puts back exactly what was there, even a file that cannot be parsed.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in prefabs.Keys)
        {
            if (name.Length == 0 || Path.GetFileName(name) != name || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !seen.Add(name))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{name}' is not a prefab settings file name.");
        }
        seen.Clear();
        foreach (var name in materials?.Keys ?? Enumerable.Empty<string>())
            if (!IsMaterialFileName(name) || !seen.Add(name))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{name}' is not a material settings file (materials/<skins, species or vanilla-skins>/<folder>/materials.json).");
        var saved = SaveManifest(ws, id, manifestJson, expectedRevision);
        var folder = Path.Combine(saved.Dir, PrefabOverrides.Folder);
        if (Directory.Exists(folder))
            foreach (var file in Directory.GetFiles(folder, "*.json"))
                if (!prefabs.ContainsKey(Path.GetFileName(file))) File.Delete(file);
        if (prefabs.Count > 0) Directory.CreateDirectory(folder);
        foreach (var (name, text) in prefabs) WriteSafely(Path.Combine(folder, name), text);
        saved.RemoveEmptyPrefabFolder();
        if (materials is not null) saved.RestoreMaterialFiles(materials);
        return saved;
    }

    /// <summary>Puts back exactly these materials.json files (by mod-relative name) and removes the others; images stay.</summary>
    private void RestoreMaterialFiles(IReadOnlyDictionary<string, string> materials)
    {
        var wanted = new HashSet<string>(materials.Keys, StringComparer.OrdinalIgnoreCase);
        var gone = MaterialFileTexts().Keys.Where(name => !wanted.Contains(name)).ToList();
        foreach (var name in gone) File.Delete(Path.Combine(Dir, name));
        foreach (var (name, text) in materials) WriteSafely(Path.Combine(Dir, name), text);
        DeleteEmptyMaterialFolders(gone);
        // Exactly what was there, even a file that cannot be read: opening the mod reports it.
        MaterialFolders.Load(Manifest, Dir, []);
    }

    public void SetDetails(string name, string version, string? author, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new TyrantException(TyrantErrorCode.ModInvalid, "A mod needs a name.");
        if (string.IsNullOrWhiteSpace(version)) throw new TyrantException(TyrantErrorCode.ModInvalid, "A mod needs a version, e.g. 1.0.0.");
        Manifest.Name = name.Trim();
        Manifest.Version = version.Trim();
        Manifest.Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim();
        Manifest.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Save();
    }

    /// <summary>Removes a texture replacement; its PNG stays in textures/ (it may be the only copy).</summary>
    public void RemoveReplacement(string texture)
    {
        if (Manifest.Replace.RemoveAll(r => string.Equals(r.Texture, texture, StringComparison.OrdinalIgnoreCase)) == 0)
            throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{Id}' does not replace '{texture}'.");
        Save();
    }

    public SkinEntry Skin(string skinId) =>
        Manifest.Skins.FirstOrDefault(s => s.Id == skinId)
        ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{Id}' has no skin '{skinId}'. Its skins: {string.Join(", ", Manifest.Skins.Select(s => s.Id))}.");

    /// <summary>Changes a skin's shown name; its id (used by saved animals and skin numbers) never changes.</summary>
    public void RenameSkin(string skinId, string name)
    {
        var skin = Skin(skinId);
        if (string.IsNullOrWhiteSpace(name)) throw new TyrantException(TyrantErrorCode.ModInvalid, "A skin needs a name.");
        var trimmed = name.Trim();
        if (Manifest.Skins.Any(s => s != skin && s.Species == skin.Species && string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"Another {skin.Species} skin in '{Id}' is already called '{trimmed}'; choose another name.");
        skin.Name = trimmed;
        Save();
    }

    /// <summary>
    /// Removes a skin. With <paramref name="deleteFiles"/>, its files go to the Recycle Bin too: its PNGs, its own model (with its
    /// LODs and report), its material folder (materials/skins/&lt;id&gt;/, whatever is in it) and its Blender project, except
    /// files another skin, model or texture replacement still uses. Its materials.json goes with the skin either way. Its
    /// number in the game stays reserved, so other skins keep theirs. Returns what went to the bin and what stayed (with why).
    /// </summary>
    public RemovedFiles RemoveSkin(string skinId, bool deleteFiles)
    {
        var skin = Skin(skinId);
        Manifest.Skins.Remove(skin);
        Save();
        if (!deleteFiles) return new RemovedFiles([], []);
        var removed = new List<string>();
        var kept = new List<string>();
        var materials = MaterialFolders.FolderOf(new MaterialOwner(MaterialOwnerKind.Skin, skin.Id, null, null));
        var inFolder = FilesIn(materials).Append(materials + "/" + MaterialFolders.FileName).ToList();
        RecycleUnused(FilesOf(skin).Concat(ModSharing.ModelFilesOf(this, skin.Model)).Concat(inFolder), removed, kept);
        DeleteIfEmpty(Path.Combine(Dir, "skins", skinId, "portraits"));
        DeleteIfEmpty(Path.Combine(Dir, "skins", skinId));
        DeleteEmptyMaterialFolders(MaterialFiles(skin.Materials).Concat(inFolder));
        RecycleBlender(BlenderProjectWriter.FolderOf(WorkspaceDir, Id, skin.Species, skin.Id), skin.Id, skin.Species, removed, kept);
        return new RemovedFiles(removed, kept);
    }

    /// <summary>The workspace holding this mod: &lt;workspace&gt;/mods/&lt;id&gt; (see RootOf).</summary>
    private string WorkspaceDir => Path.GetDirectoryName(Path.GetDirectoryName(Dir)!)!;

    /// <summary>To the Recycle Bin; returns why it stayed (in use, or no bin), null when it went: the edit itself is already saved.</summary>
    private static string? TryRecycle(string path)
    {
        try
        {
            RecycleBin.Current.Recycle(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>Sets a skin's colours from the "colors" JSON (null or {} removes them), validated like Check does.</summary>
    public void SetColors(string skinId, string? colorsJson)
    {
        var skin = Skin(skinId);
        SkinColors? colors = null;
        if (!string.IsNullOrWhiteSpace(colorsJson))
        {
            try
            {
                colors = SkinColors.Parse(Json.Parse(colorsJson), skinId);
            }
            catch (Exception ex) when (ex is ManifestException or FormatException)
            {
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"The colours of '{skinId}' cannot be saved: {ex.Message}");
            }
            if (colors.ToJson().Count == 0) colors = null;
        }
        skin.Colors = colors;
        Save();
    }

    /// <summary>
    /// Copies <paramref name="png"/> into skins/&lt;skin&gt;/ for one sex and slot, or (null) clears the slot so the base
    /// skin's texture is used. Returns the file written, or null when cleared.
    /// </summary>
    public string? SetSkinFile(string skinId, string sex, string slot, string? png)
    {
        var skin = Skin(skinId);
        if (sex is not ("male" or "female")) throw new TyrantException(TyrantErrorCode.ModInvalid, $"Sex must be male or female (got '{sex}').");
        var canonical = SkinSlotNames.Canonical(slot)
            ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{slot}' is not a skin slot (use {string.Join(", ", SkinSlotNames.All)}).");
        var files = sex == "male" ? skin.Male : skin.Female;
        if (png is null)
        {
            if (files is null || !files.ContainsKey(canonical)) return null;
            var other = sex == "male" ? skin.Female : skin.Male;
            if (files.Count == 1 && (other is null || other.Count == 0))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{skin.Name}' needs at least one file of its own; remove the skin instead.");
            files.Remove(canonical);
            if (files.Count == 0)
            {
                if (sex == "male") skin.Male = null;
                else skin.Female = null;
            }
            Save();
            return null;
        }
        var file = $"skins/{skinId}/{SkinFileName(sex, canonical)}";
        CopyPng(png, file);
        if (files is null)
        {
            files = new Dictionary<string, string>(StringComparer.Ordinal);
            if (sex == "male") skin.Male = files;
            else skin.Female = files;
        }
        files[canonical] = file;
        Save();
        return file;
    }

    /// <summary>Copies a PNG as the skin's swatch, or (null) removes it so the game cuts one from the diffuse.</summary>
    public string? SetThumbnail(string skinId, string? png)
    {
        var skin = Skin(skinId);
        if (png is null)
        {
            skin.Thumbnail = null;
            Save();
            return null;
        }
        var file = $"skins/{skinId}/thumbnail.png";
        CopyPng(png, file);
        skin.Thumbnail = file;
        Save();
        return file;
    }

    /// <summary>The file name a slot's PNG gets, matching the templates Add skin writes (male_D.png, female_N.png, …).</summary>
    public static string SkinFileName(string sex, string slot)
    {
        var infant = slot.StartsWith("infant", StringComparison.Ordinal);
        var part = infant ? slot["infant".Length..].ToLowerInvariant() : slot;
        var suffix = part switch { "diffuse" => "D", "normal" => "N", _ => part };
        return $"{sex}_{(infant ? "infant_" : "")}{suffix}.png";
    }

    private void CopyPng(string source, string file)
    {
        if (!File.Exists(source)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{source}' does not exist.");
        if (!IsPng(source)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{source}' is not a PNG image (the game reads PNG only).");
        var destination = Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, destination, overwrite: true);
            File.SetLastWriteTimeUtc(destination, DateTime.UtcNow); // a copy keeps its source's date: previews keyed by it would not notice
        }
    }

    /// <summary>
    /// Changes when any file mod.json names changes (size or write time), even under the same name: the editor reloads its
    /// thumbnails and 3D view on it (the revision only follows mod.json).
    /// </summary>
    public string FilesStamp()
    {
        var files = NamedFiles().Select(Normal).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal);
        var text = new System.Text.StringBuilder();
        foreach (var file in files)
        {
            var info = new FileInfo(Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar)));
            text.Append(file).Append('|').Append(info.Exists ? info.Length : -1).Append('|').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>
    /// Every file mod.json and the material folders name, as written and whether or not it exists, except mod.json,
    /// animals.db, prefab settings and language files: texture replacements, each skin's files and model build, each models
    /// entry's build, each materials.json with the ramps and masks it names, sounds, and each species' icon, swatch, portraits
    /// and prefab bundle. Sharing, the files stamp and removals all use it.
    /// </summary>
    internal IEnumerable<string> NamedFiles() =>
        Manifest.Replace.Select(r => r.File)
            .Concat(Manifest.Skins.SelectMany(s => FilesOf(s).Concat(ModSharing.ModelFilesOf(this, s.Model))))
            .Concat(Manifest.Models.SelectMany(EntryFiles))
            .Concat(MaterialFolders.Files(Manifest))
            .Concat(Manifest.Sounds.SelectMany(s => s.Files))
            .Concat(Manifest.Species.SelectMany(s => new[] { s.Icon, s.Swatch, s.Prefab?.Bundle }.OfType<string>().Concat(s.Portraits.Values)))
            .Where(f => !string.IsNullOrWhiteSpace(f));

    /// <summary>A skin's own files: its PNGs, swatch, portraits and material images (not its model build).</summary>
    private static IEnumerable<string> FilesOf(SkinEntry skin) =>
        (skin.Male?.Values ?? Enumerable.Empty<string>()).Concat(skin.Female?.Values ?? Enumerable.Empty<string>())
        .Concat(skin.Thumbnail is null ? [] : [skin.Thumbnail]).Concat(skin.Portraits?.Values ?? Enumerable.Empty<string>())
        .Concat(MaterialFiles(skin.Materials));

    /// <summary>A models entry's files: its model build (none for a rig-only entry).</summary>
    private IEnumerable<string> EntryFiles(ModelReplacement entry) => ModSharing.ModelFilesOf(this, entry.File);

    /// <summary>The iridescence ramp and mask and the fur mask images an owner's materials name.</summary>
    internal static IEnumerable<string> MaterialFiles(IReadOnlyDictionary<string, MaterialOverride>? materials) =>
        materials is null ? []
            : materials.Values.SelectMany(m => new[] { m.Iridescence?.Ramp?.File, m.Iridescence?.Mask?.File, m.Fur?.Mask?.File, m.Fur?.InfantMask?.File })
                .OfType<string>();

    /// <summary>Every file under a mod-relative folder, as mod-relative '/' paths; none when it does not exist.</summary>
    private IEnumerable<string> FilesIn(string folder)
    {
        var path = Path.Combine(Dir, folder.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(path)
            ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Select(f => Normal(Path.GetRelativePath(Dir, f)))
            : [];
    }

    /// <summary>After a removal: the folders of these material images that are left empty, up to (not including) the mod folder.</summary>
    private void DeleteEmptyMaterialFolders(IEnumerable<string> files)
    {
        var root = Path.GetFullPath(Dir).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var file in files)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Dir, file)));
            for (; dir != null && dir.Length > root.Length && ModPaths.IsInside(dir, Dir); dir = Path.GetDirectoryName(dir))
                if (!DeleteIfEmpty(dir)) break;
        }
    }

    private static string Normal(string file) => file.Replace('\\', '/');
}
