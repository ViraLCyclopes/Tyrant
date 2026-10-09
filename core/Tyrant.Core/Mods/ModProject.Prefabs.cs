using System.Text.Json;
using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Prefab settings: a mod's overrides of prefab component fields, one prefabs/&lt;file&gt;.json per species.</summary>
public sealed partial class ModProject
{
    private string PrefabFolder => Path.Combine(Dir, PrefabOverrides.Folder);

    public IReadOnlyList<(string File, PrefabOverrides Overrides)> PrefabTargets() => PrefabOverrides.ReadFolder(Dir, null);

    public IReadOnlyDictionary<string, string> PrefabFiles() =>
        Directory.Exists(PrefabFolder)
            ? Directory.GetFiles(PrefabFolder, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetFileName(p), File.ReadAllText)
            : new Dictionary<string, string>();

    public void SetPrefabField(string target, string obj, string component, string field, JsonElement value, IReadOnlyList<PrefabFieldInfo> tree)
    {
        var info = tree.FirstOrDefault(f => f.Object == obj && f.Component == component && f.Field == field)
            ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"{target}'s prefab has no field {component}.{field}{(obj.Length == 0 ? "" : " on " + obj)}; pick one from the list.");
        if (PrefabFieldTree.CheckValue(info, value) is { } problem) throw new TyrantException(TyrantErrorCode.ModInvalid, problem);
        var overrides = WritableOverrides(target) ?? new PrefabOverrides { Target = target };
        var entry = new PrefabField { Object = obj, Component = component, Field = field, Value = Json.Parse(value.GetRawText()) };
        var at = overrides.Fields.FindIndex(f => f.Object == obj && f.Component == component && f.Field == field);
        if (at >= 0) overrides.Fields[at] = entry; else overrides.Fields.Add(entry);
        WritePrefab(overrides);
    }

    public void ResetPrefabField(string target, string obj, string component, string field)
    {
        var overrides = WritableOverrides(target) ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{Id}' has no prefab settings for {target}.");
        overrides.Fields.RemoveAll(f => f.Object == obj && f.Component == component && f.Field == field);
        WritePrefab(overrides);
    }

    public void RemovePrefabTarget(string target)
    {
        var (file, _) = PrefabTargets().FirstOrDefault(t => string.Equals(t.Overrides.Target, target, StringComparison.OrdinalIgnoreCase));
        if (file is null) RefuseUnreadableFile(target);
        if (file is null) throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{Id}' has no prefab settings for {target}.");
        File.Delete(Path.Combine(PrefabFolder, file));
        RemoveEmptyPrefabFolder();
    }

    /// <summary>The target's overrides for a change; refuses when its file is on disk but cannot be read (it would be overwritten).</summary>
    private PrefabOverrides? WritableOverrides(string target)
    {
        var found = Overrides(target);
        if (found is null) RefuseUnreadableFile(target);
        return found;
    }

    /// <summary>A file where the target's settings would go that is not among the readable ones is never overwritten or deleted.</summary>
    private void RefuseUnreadableFile(string target)
    {
        var name = PrefabOverrides.FileName(target);
        var path = Path.Combine(PrefabFolder, name);
        if (!File.Exists(path) || PrefabTargets().Any(t => string.Equals(t.File, name, StringComparison.OrdinalIgnoreCase))) return;
        string why;
        try { PrefabOverrides.Parse(File.ReadAllText(path)); why = "it duplicates another prefab settings file"; }
        catch (Exception ex) when (ex is ManifestException or IOException or UnauthorizedAccessException) { why = ex.Message; }
        throw new TyrantException(TyrantErrorCode.ModInvalid, $"prefabs/{name} cannot be read ({why}); fix or delete it first, then try again.");
    }

    private PrefabOverrides? Overrides(string target) =>
        PrefabTargets().FirstOrDefault(t => string.Equals(t.Overrides.Target, target, StringComparison.OrdinalIgnoreCase)).Overrides;

    /// <summary>Writes a target's file (its existing file name if it has one); a target with no fields loses its file.</summary>
    private void WritePrefab(PrefabOverrides overrides)
    {
        var existing = PrefabTargets().FirstOrDefault(t => string.Equals(t.Overrides.Target, overrides.Target, StringComparison.OrdinalIgnoreCase)).File;
        var path = Path.Combine(PrefabFolder, existing ?? PrefabOverrides.FileName(overrides.Target));
        if (overrides.Fields.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            RemoveEmptyPrefabFolder();
            return;
        }
        Directory.CreateDirectory(PrefabFolder);
        WriteSafely(path, overrides.ToJson());
    }

    private void RemoveEmptyPrefabFolder()
    {
        if (Directory.Exists(PrefabFolder) && !Directory.EnumerateFileSystemEntries(PrefabFolder).Any()) Directory.Delete(PrefabFolder);
    }
}
