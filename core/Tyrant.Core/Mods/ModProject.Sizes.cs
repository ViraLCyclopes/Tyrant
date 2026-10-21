using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Size settings: a mod's size changes per species, one sizes/&lt;file&gt;.json each, and its genetic size ranges in mod.json.</summary>
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

    public SizeSettings? Sizes(string target) =>
        SizeSettings.ReadFolder(Dir, null).FirstOrDefault(t => string.Equals(t.Sizes.Target, target, StringComparison.OrdinalIgnoreCase)).Sizes;

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
        var written = new SizeSettings { Target = target, Adult = sizes.Adult, Baby = sizes.Baby, Male = sizes.Male, Female = sizes.Female };
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

    /// <summary>Sets the genetic size ranges in mod.json; null (or both unset) removes them.</summary>
    public void SetSizeRanges(SizeRanges? ranges)
    {
        foreach (var (name, range) in new[] { ("nursery", ranges?.Nursery), ("breeding", ranges?.Breeding) })
            if (range is not null && !(range.Length == 2 && range[0] > SizeRanges.Min && range[0] <= range[1] && range[1] <= SizeRanges.Max))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"sizeRanges.{name} must be [min, max] with 0 < min <= max <= {SizeRanges.Max}.");
        var old = Manifest.SizeRanges;
        Manifest.SizeRanges = ranges is null || ranges.IsEmpty ? null : new SizeRanges { Nursery = ranges.Nursery, Breeding = ranges.Breeding };
        try { Save(); }
        catch { Manifest.SizeRanges = old; throw; }
    }

    private void RemoveEmptySizeFolder()
    {
        if (Directory.Exists(SizeFolder) && !Directory.EnumerateFileSystemEntries(SizeFolder).Any()) Directory.Delete(SizeFolder);
    }
}
