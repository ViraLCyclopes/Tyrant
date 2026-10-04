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
}
