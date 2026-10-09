using System.Security.Cryptography;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Editing a mod in place (the app's mod editor and the matching 'tyrant mod …' commands).</summary>
public sealed partial class ModProject
{
    /// <summary>Revision of the manifest and committed SQLite species data, including external WAL edits.</summary>
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
            }))))));

    /// <summary>Opens the mod, refusing when its manifest or animal database changed since the editor's revision.</summary>
    public static ModProject Open(Workspace ws, string id, string? expectedRevision)
    {
        var mod = Open(ws, id);
        if (expectedRevision is not null && !string.Equals(mod.Revision(), expectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new TyrantException(TyrantErrorCode.ModChanged,
                $"'{id}' changed outside this editor (mod.json or animals.db was edited elsewhere). It was reloaded; make your change again.");
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
        var saved = new ModProject(mod.Dir, manifest);
        saved.Save();
        return saved;
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
    /// LODs and report) and its Blender project, except files another skin, model or texture replacement still uses. Its number in
    /// the game stays reserved, so other skins keep theirs. Returns what went to the bin.
    /// </summary>
    public IReadOnlyList<string> RemoveSkin(string skinId, bool deleteFiles)
    {
        var skin = Skin(skinId);
        Manifest.Skins.Remove(skin);
        Save();
        if (!deleteFiles) return [];
        var stillUsed = new HashSet<string>(Manifest.Skins.SelectMany(FilesOf)
            .Concat(Manifest.Replace.Select(r => r.File))
            .Concat(Manifest.Skins.SelectMany(s => ModSharing.ModelFilesOf(this, s.Model)))
            .Concat(Manifest.Models.SelectMany(m => ModSharing.ModelFilesOf(this, m.File)))
            .Select(Normal), StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();
        foreach (var file in FilesOf(skin).Concat(ModSharing.ModelFilesOf(this, skin.Model)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(Path.Combine(Dir, file));
            if (stillUsed.Contains(Normal(file)) || !ModPaths.IsInside(path, Dir) || !File.Exists(path)) continue;
            if (TryRecycle(path)) removed.Add(Normal(file));
        }
        var folder = Path.Combine(Dir, "skins", skinId);
        try { if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); } catch (IOException) { }
        var blender = Tyrant.Core.Blender.BlenderProjectWriter.FolderOf(WorkspaceDir, Id, skin.Species, skin.Id);
        if (Directory.Exists(blender) && TryRecycle(blender)) removed.Add(Normal(Path.GetRelativePath(WorkspaceDir, blender)) + "/");
        return removed;
    }

    /// <summary>The workspace holding this mod: &lt;workspace&gt;/mods/&lt;id&gt; (see RootOf).</summary>
    private string WorkspaceDir => Path.GetDirectoryName(Path.GetDirectoryName(Dir)!)!;

    /// <summary>To the Recycle Bin; false when it stays (in use, or no bin): the edit itself is already saved.</summary>
    private static bool TryRecycle(string path)
    {
        try
        {
            RecycleBin.Current.Recycle(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
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
        var files = Manifest.Replace.Select(r => r.File).Concat(Manifest.Skins.SelectMany(FilesOf)).Concat(Manifest.Skins.Select(s => s.Model))
            .Concat(Manifest.Models.Select(m => m.File)).Concat(Manifest.Sounds.SelectMany(s => s.Files)).Concat(Manifest.Species.Select(s => s.Icon)).OfType<string>()
            .Select(Normal).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal);
        var text = new System.Text.StringBuilder();
        foreach (var file in files)
        {
            var info = new FileInfo(Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar)));
            text.Append(file).Append('|').Append(info.Exists ? info.Length : -1).Append('|').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private static IEnumerable<string> FilesOf(SkinEntry skin) =>
        (skin.Male?.Values ?? Enumerable.Empty<string>()).Concat(skin.Female?.Values ?? Enumerable.Empty<string>())
        .Concat(skin.Thumbnail is null ? [] : [skin.Thumbnail]);

    private static string Normal(string file) => file.Replace('\\', '/');
}
