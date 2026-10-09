using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed partial class ModProject
{
    public SpeciesEntry AddSpecies(IReadOnlyList<SpeciesSkins> vanilla, string id, string template,
        string name, string genus, string epithet, int? cost = null, IReadOnlyDictionary<string, double>? stats = null,
        SpeciesTemplateStats? templateStats = null)
    {
        if (!ModId.IsValid(id)) throw new TyrantException(TyrantErrorCode.ModInvalid, "Species id must be 3–64 lowercase letters, digits or '-'.");
        if (Manifest.Species.Any(s => s.Id == id)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"Species '{id}' already exists in this mod.");
        if (new[] { name, genus, epithet }.Any(string.IsNullOrWhiteSpace))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Give the species a display name, genus and epithet.");
        if (cost < 0) throw new TyrantException(TyrantErrorCode.ModInvalid, "Cost must be zero or greater.");
        try { SpeciesStats.Validate(stats ?? new Dictionary<string, double>()); }
        catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
        var source = vanilla.FirstOrDefault(s => string.Equals(s.SpeciesId, template, StringComparison.OrdinalIgnoreCase));
        if (source == null || source.Vivarium || source.SpeciesId.StartsWith("tyrant.", StringComparison.Ordinal))
            throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{template}' must be a vanilla regular animal from the data dump.");
        if (source.Skins.Count == 0) throw new TyrantException(TyrantErrorCode.ModInvalid, "The template has no skins.");
        var copiedStats = templateStats is null ? new Dictionary<string, double>() : new Dictionary<string, double>(templateStats.Stats);
        foreach (var value in stats ?? new Dictionary<string, double>()) copiedStats[value.Key] = value.Value;
        try { SpeciesStats.Validate(copiedStats); }
        catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
        var entry = new SpeciesEntry
        {
            Id = id, Base = source.SpeciesId, Name = name.Trim(), Genus = genus.Trim(), Epithet = epithet.Trim(), Cost = cost ?? templateStats?.Cost,
            Stats = copiedStats,
            Fields = templateStats?.Fields.Select(f => f.Copy()).ToList() ?? [],
            Localization = AnimalText.Keys.ToDictionary(k => k, k => id + "/" + k),
        };
        Manifest.Species.Add(entry);
        try { EnsureLocalizationFiles(entry); SaveSpecies(); }
        catch { Manifest.Species.Remove(entry); throw; }
        return entry;
    }

    public void SetSpecies(string id, string name, string genus, string epithet, int? cost, IReadOnlyDictionary<string, double> stats,
        IReadOnlyDictionary<string, string>? fields = null, SpeciesTemplateStats? template = null, IReadOnlyDictionary<string, string>? localization = null)
    {
        var index = Manifest.Species.FindIndex(s => s.Id == id);
        if (index < 0) throw new TyrantException(TyrantErrorCode.TargetNotFound, $"No species '{id}' in this mod.");
        if (new[] { name, genus, epithet }.Any(string.IsNullOrWhiteSpace) || cost < 0)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Species needs names and a nonnegative cost.");
        try { SpeciesStats.Validate(stats); }
        catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
        var old = Manifest.Species[index];
        var copiedFields = (old.Fields.Count > 0 ? old.Fields : template?.Fields ?? []).Select(f => f.Copy()).ToList();
        foreach (var pair in fields ?? new Dictionary<string, string>())
        {
            var field = copiedFields.FirstOrDefault(f => f.Path == pair.Key && f.Editable)
                ?? throw new TyrantException(TyrantErrorCode.ModInvalid, "Unknown or read-only animal field: " + pair.Key);
            field.Value = pair.Value;
            try { field.Validate(); }
            catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
        }
        Manifest.Species[index] = new SpeciesEntry
        {
            Id = old.Id, Base = old.Base, Name = name.Trim(), Genus = genus.Trim(), Epithet = epithet.Trim(), Cost = cost,
            Stats = new Dictionary<string, double>(stats),
            Fields = copiedFields,
            Localization = new Dictionary<string, string>(localization ?? old.Localization),
            Icon = old.Icon,
            Prefab = old.Prefab,
        };
        try { EnsureLocalizationFiles(Manifest.Species[index]); SaveSpecies(); }
        catch { Manifest.Species[index] = old; throw; }
    }

    private void SaveSpecies()
    {
        try { AnimalDatabaseFile.Save(DatabasePath, Manifest.Species, _databaseSnapshot); }
        catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
        _databaseSnapshot = AnimalDatabaseFile.Snapshot(Manifest.Species);
    }

    public void EnsureLocalizationFiles(SpeciesEntry entry)
    {
        foreach (var term in entry.Localization)
        {
            var path = AnimalLocalization.FileFor(Dir, "en-US", term.Value);
            if (File.Exists(path)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var text = term.Key switch
            {
                "name" => entry.Name,
                "description" => entry.Name + " is a custom species based on " + entry.Base + ".",
                _ => "",
            };
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, text); File.Move(temp, path, overwrite: false); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public void SetSpeciesIcon(string id, string? png)
    {
        var entry = Manifest.Species.FirstOrDefault(s => s.Id == id)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + id);
        var old = entry.Icon;
        var file = png == null ? null : $"icons/{id}.png";
        if (png != null) CopyPng(png, file!);
        entry.Icon = file;
        try { SaveSpecies(); }
        catch { entry.Icon = old; throw; }
    }

    public void SetSpeciesPrefab(string id, string? bundle, string? asset)
    {
        var entry = Manifest.Species.FirstOrDefault(s => s.Id == id)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + id);
        SpeciesPrefab? reference = null;
        if (bundle != null)
        {
            reference = new SpeciesPrefab { Bundle = $"prefabs/{id}/{Guid.NewGuid():N}.bundle", Asset = asset?.Trim().ToLowerInvariant() ?? "" };
            try { reference.Validate(); }
            catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
            using var input = File.OpenRead(bundle);
            var header = new byte[8];
            if (input.Read(header, 0, header.Length) != 8 || System.Text.Encoding.ASCII.GetString(header) != "UnityFS\0")
                throw new TyrantException(TyrantErrorCode.ModInvalid, "Choose the .bundle produced by Unity's Tyrant exporter.");
            var destination = Path.Combine(Dir, reference.Bundle);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            UnityPrefabBundle.CopyForGame(bundle, destination);
        }
        var previous = entry.Prefab;
        entry.Prefab = reference;
        try { SaveSpecies(); }
        catch
        {
            entry.Prefab = previous;
            if (reference != null) File.Delete(Path.Combine(Dir, reference.Bundle));
            throw;
        }
    }

    /// <summary>Use the template's prefab and maps while retaining the new species' runtime identity.</summary>
    public static IReadOnlyList<SpeciesSkins> WithSpecies(IReadOnlyList<SpeciesSkins> vanilla, ModManifest manifest)
    {
        var result = vanilla.ToList();
        foreach (var entry in manifest.Species)
        {
            var template = vanilla.FirstOrDefault(s => s.SpeciesId == entry.Base && !s.Vivarium);
            var id = entry.Key(manifest.Id);
            if (template != null && !result.Any(s => s.SpeciesId == id)) result.Add(template with { SpeciesId = id });
        }
        return result;
    }
}
