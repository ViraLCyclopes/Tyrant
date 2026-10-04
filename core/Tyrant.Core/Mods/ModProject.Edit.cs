using System.Security.Cryptography;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Editing a mod in place (the app's mod editor and the matching 'tyrant mod …' commands).</summary>
public sealed partial class ModProject
{
    /// <summary>SHA-256 of mod.json as it is on disk: an editor sends back the revision it started from.</summary>
    public string Revision() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Dir, ModManifest.FileName))));

    /// <summary>Opens the mod, refusing when mod.json changed since <paramref name="expectedRevision"/> was read.</summary>
    public static ModProject Open(Workspace ws, string id, string? expectedRevision)
    {
        var mod = Open(ws, id);
        if (expectedRevision is not null && !string.Equals(mod.Revision(), expectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new TyrantException(TyrantErrorCode.ModChanged,
                $"'{id}' changed outside this editor (mod.json was edited elsewhere). It was reloaded; make your change again.");
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
    /// Removes a skin. With <paramref name="deleteFiles"/>, its files are deleted too, except those another skin or a texture
    /// replacement still uses. Its number in the game stays reserved, so other skins keep theirs.
    /// </summary>
    public IReadOnlyList<string> RemoveSkin(string skinId, bool deleteFiles)
    {
        var skin = Skin(skinId);
        Manifest.Skins.Remove(skin);
        Save();
        if (!deleteFiles) return [];
        var stillUsed = new HashSet<string>(Manifest.Skins.SelectMany(FilesOf).Concat(Manifest.Replace.Select(r => r.File)).Select(Normal), StringComparer.OrdinalIgnoreCase);
        var deleted = new List<string>();
        foreach (var file in FilesOf(skin).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(Path.Combine(Dir, file));
            if (stillUsed.Contains(Normal(file)) || !ModPaths.IsInside(path, Dir) || !File.Exists(path)) continue;
            try
            {
                File.Delete(path);
                deleted.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a locked file stays; the skin is already gone from mod.json
            }
        }
        var folder = Path.Combine(Dir, "skins", skinId);
        try { if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); } catch (IOException) { }
        return deleted;
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
            File.Copy(source, destination, overwrite: true);
    }

    private static IEnumerable<string> FilesOf(SkinEntry skin) =>
        (skin.Male?.Values ?? Enumerable.Empty<string>()).Concat(skin.Female?.Values ?? Enumerable.Empty<string>())
        .Concat(skin.Thumbnail is null ? [] : [skin.Thumbnail]);

    private static string Normal(string file) => file.Replace('\\', '/');
}
