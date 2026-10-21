using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Size settings: a mod's size changes per species (with its genetic size ranges and size limit), one sizes/&lt;file&gt;.json each.</summary>
public sealed partial class ModProject
{
    private string SizeFolder => Path.Combine(Dir, SizeSettings.Folder);

    /// <summary>The species (or custom species keys) this mod sets sizes for.</summary>
    public IReadOnlyList<string> SizeTargets => SizeSettings.ReadFolder(Dir, null).Select(t => t.Sizes.Target).ToList();

    /// <summary>The sizes/ files by name, as they are on disk (for the revision and sharing).</summary>
    public IReadOnlyDictionary<string, string> SizeFiles() =>
        Directory.Exists(SizeFolder)
            ? Directory.GetFiles(SizeFolder, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetFileName(p), File.ReadAllText)
            : new Dictionary<string, string>();

    /// <summary>The sizes/ file a target's sizes are in, or the one they would go in (Check names it in its messages about them).</summary>
    public string SizeFileOf(string target) =>
        SizeSettings.ReadFolder(Dir, null).FirstOrDefault(t => string.Equals(t.Sizes.Target, target, StringComparison.OrdinalIgnoreCase)).File
        ?? SizeSettings.FileName(target);

    public SizeSettings? Sizes(string target) =>
        SizeSettings.ReadFolder(Dir, null).FirstOrDefault(t => string.Equals(t.Sizes.Target, target, StringComparison.OrdinalIgnoreCase)).Sizes;

    /// <summary>
    /// Writes a target's sizes as <see cref="SetSizes(string, SizeSettings?)"/> does and, unless this save sets the limit itself
    /// (<paramref name="setsLimit"/>: then the user's limit stands and Check reports any problem), raises the species' size
    /// limit in the same write when the sizes would pass it (<see cref="SpeciesSizes.RaisedLimit"/>). Returns what it raised,
    /// to tell the user; nothing without the game's data (<paramref name="facts"/> null).
    /// </summary>
    public IReadOnlyList<string> SetSizes(string target, SizeSettings? sizes, SizeFacts? facts, bool setsLimit)
    {
        if (setsLimit || sizes is null || facts is null || Raised(target, sizes, facts) is not { } raised)
        {
            SetSizes(target, sizes);
            return [];
        }
        SetSizes(target, raised.Sizes);
        return [raised.Note];
    }

    /// <summary>Raises the species' size limit to fit the sizes it has now (set in the app, say), as a save of them would; writes only when it raises.</summary>
    public IReadOnlyList<string> FitSizeLimit(string target, SizeFacts facts)
    {
        if (Raised(target, Sizes(target) ?? new SizeSettings { Target = target }, facts) is not { } raised) return [];
        SetSizes(target, raised.Sizes);
        return [raised.Note];
    }

    /// <summary>The sizes with the species' limit raised to fit them, and the note; null when they fit (or the species is not in the data).</summary>
    private static (SizeSettings Sizes, string Note)? Raised(string target, SizeSettings sizes, SizeFacts facts)
    {
        if (facts.Original(target) is not { } original || SpeciesSizes.RaisedLimit(original, sizes, facts) is not { } raised) return null;
        return (new SizeSettings
        {
            Target = target, Adult = sizes.Adult, Baby = sizes.Baby, Male = sizes.Male, Female = sizes.Female,
            Nursery = sizes.Nursery, Breeding = sizes.Breeding, Limit = raised.Limit,
        }, raised.Note);
    }

    /// <summary>Writes a target's sizes (its existing file name if it has one); null, or every value unset, deletes its file.</summary>
    public void SetSizes(string target, SizeSettings? sizes)
    {
        var existing = SizeSettings.ReadFolder(Dir, null).FirstOrDefault(t => string.Equals(t.Sizes.Target, target, StringComparison.OrdinalIgnoreCase)).File;
        var name = existing ?? SizeSettings.FileName(target);
        var path = Path.Combine(SizeFolder, name);
        if (existing is null && File.Exists(path)) // an unreadable file where this target's would go is never overwritten or deleted
        {
            string why;
            try { SizeSettings.Parse(File.ReadAllText(path)); why = "it duplicates another sizes file"; }
            catch (Exception ex) when (ex is ManifestException or IOException or UnauthorizedAccessException) { why = ex.Message; }
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"sizes/{name} cannot be read ({why}); fix or delete it first, then try again.");
        }
        if (sizes is null || sizes.IsEmpty)
        {
            if (File.Exists(path)) File.Delete(path);
            RemoveEmptySizeFolder();
            return;
        }
        var written = new SizeSettings
        {
            Target = target, Adult = sizes.Adult, Baby = sizes.Baby, Male = sizes.Male, Female = sizes.Female,
            Nursery = sizes.Nursery, Breeding = sizes.Breeding, Limit = sizes.Limit,
        };
        var json = written.ToJson();
        try
        {
            SizeSettings.Parse(json); // the same limits as reading it back
        }
        catch (ManifestException ex)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{target}: {ex.Message}");
        }
        Directory.CreateDirectory(SizeFolder);
        WriteSafely(path, json);
    }

    /// <summary>The old record edits of the growth curve and sex sizes on this mod's species (by its key); none for a game species.</summary>
    public IReadOnlyList<string> SizeRecordEdits(string target) =>
        Manifest.Species.FirstOrDefault(s => s.Key(Id) == target) is { } entry ? SpeciesSizes.RecordEdits(entry) : [];

    /// <summary>
    /// Removes the size fields (growth curve, sex multipliers) from this mod's species' record (by its key), so its size is the
    /// template's and Size's alone. Returns the edits that changed its size.
    /// </summary>
    public IReadOnlyList<string> RemoveSizeRecordEdits(string target)
    {
        var entry = Manifest.Species.FirstOrDefault(s => s.Key(Id) == target)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"{target} is not a species of '{Id}': only a mod's own species has record edits.");
        var edits = SpeciesSizes.RecordEdits(entry);
        var old = entry.Fields;
        if (!old.Any(f => SpeciesSizes.IsSizePath(f.Path))) return edits;
        entry.Fields = old.Where(f => !SpeciesSizes.IsSizePath(f.Path)).ToList();
        try { SaveSpecies(); }
        catch { entry.Fields = old; throw; }
        return edits;
    }

    /// <summary>Puts back exactly these sizes/ files (by name) and removes the others, even a file that cannot be read.</summary>
    internal void RestoreSizeFiles(IReadOnlyDictionary<string, string> sizes)
    {
        if (Directory.Exists(SizeFolder))
            foreach (var file in Directory.GetFiles(SizeFolder, "*.json"))
                if (!sizes.ContainsKey(Path.GetFileName(file))) File.Delete(file);
        if (sizes.Count > 0) Directory.CreateDirectory(SizeFolder);
        foreach (var (name, text) in sizes) WriteSafely(Path.Combine(SizeFolder, name), text);
        RemoveEmptySizeFolder();
    }

    private void RemoveEmptySizeFolder()
    {
        if (Directory.Exists(SizeFolder) && !Directory.EnumerateFileSystemEntries(SizeFolder).Any()) Directory.Delete(SizeFolder);
    }
}
