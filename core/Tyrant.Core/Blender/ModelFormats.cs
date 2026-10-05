using Tyrant.Core.Errors;

namespace Tyrant.Core.Blender;

/// <summary>The file format models are exported in: glb (Tyrant's own), FBX (converted by Blender), or both.</summary>
public enum ModelFormat { Glb, Fbx, Both }

public static class ModelFormats
{
    public static ModelFormat Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" or "glb" => ModelFormat.Glb,
        "fbx" => ModelFormat.Fbx,
        "both" => ModelFormat.Both,
        _ => throw new TyrantException(TyrantErrorCode.ModInvalid, $"Format must be glb, fbx or both (got '{text}')."),
    };

    /// <summary>
    /// Converts every .glb in files to FBX in one Blender run. Fbx drops the converted glb files (unless keepGlb: species
    /// packs keep theirs for targets.json); a glb that failed stays, with a note.
    /// </summary>
    public static (IReadOnlyList<string> Files, IReadOnlyList<string> Notes) Apply(IModelConverter converter, IReadOnlyList<string> files,
        ModelFormat format, bool keepGlb = false)
    {
        if (format == ModelFormat.Glb) return (files, []);
        var glbs = files.Where(IsGlb).ToList();
        var failures = converter.GlbToFbx([.. glbs.Select(g => (g, Path.ChangeExtension(g, ".fbx")))]);
        var result = new List<string>();
        var notes = new List<string>();
        foreach (var file in files)
        {
            if (!IsGlb(file))
            {
                result.Add(file);
                continue;
            }
            if (failures.TryGetValue(file, out var why))
            {
                notes.Add($"{Path.GetFileName(file)} could not be converted to FBX: {why}");
                result.Add(file);
                continue;
            }
            if (format == ModelFormat.Both || keepGlb) result.Add(file);
            else File.Delete(file);
            result.Add(Path.ChangeExtension(file, ".fbx"));
        }
        return (result, notes);
    }

    private static bool IsGlb(string file) => file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
}
