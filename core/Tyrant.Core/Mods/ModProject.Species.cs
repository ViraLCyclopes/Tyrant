using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed partial class ModProject
{
    public SpeciesEntry AddSpecies(IReadOnlyList<SpeciesSkins> vanilla, string id, string template,
        string name, string genus, string epithet, int? cost = null, IReadOnlyDictionary<string, double>? stats = null,
        SpeciesTemplateStats? templateStats = null, int startingSkin = 0)
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
        if (!source.Skins.Any(s => s.Index == startingSkin))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{source.SpeciesId} has no skin number {startingSkin}. Its skins: {string.Join(", ", source.Skins.Select(s => $"{s.Index} {s.Name}"))}.");
        var copiedStats = templateStats is null ? new Dictionary<string, double>() : new Dictionary<string, double>(templateStats.Stats);
        foreach (var value in stats ?? new Dictionary<string, double>()) copiedStats[value.Key] = value.Value;
        try { SpeciesStats.Validate(copiedStats); }
        catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
        var entry = new SpeciesEntry
        {
            Id = id, Base = source.SpeciesId, Name = name.Trim(), Genus = genus.Trim(), Epithet = epithet.Trim(), Cost = cost ?? templateStats?.Cost, StartingSkin = startingSkin,
            Stats = copiedStats,
            Fields = templateStats?.Fields.Select(f => f.Copy()).ToList() ?? [],
            Localization = AnimalText.Keys.ToDictionary(k => k, k => id + "/" + k),
        };
        Manifest.Species.Add(entry);
        try { EnsureLocalizationFiles(entry); SaveSpecies(); }
        catch { Manifest.Species.Remove(entry); throw; }
        return entry;
    }

    /// <summary>Undoes a species just added: its animals.db row and the language files made for it.</summary>
    internal void RemoveSpeciesForRollback(SpeciesEntry entry)
    {
        Manifest.Species.Remove(entry);
        SaveSpecies();
        foreach (var code in AnimalLocalization.Codes)
            foreach (var term in entry.Localization.Values)
            {
                var path = AnimalLocalization.FileFor(Dir, code, term);
                if (File.Exists(path)) File.Delete(path);
                for (var dir = Path.GetDirectoryName(path); dir != null && dir.Length > Path.Combine(Dir, "localization", code).Length
                    && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any(); dir = Path.GetDirectoryName(dir))
                    Directory.Delete(dir);
            }
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
        var copiedFields = SpeciesDatabase.FillEmptyObjects(old.Fields.Count > 0 ? old.Fields : template?.Fields ?? [], template?.Fields)
            .Select(f => f.Copy()).ToList();
        foreach (var pair in fields ?? new Dictionary<string, string>())
        {
            var field = copiedFields.FirstOrDefault(f => f.Path == pair.Key && f.Editable)
                ?? throw new TyrantException(TyrantErrorCode.ModInvalid, "Unknown or read-only animal field: " + pair.Key);
            field.Value = pair.Value;
            try { field.Validate(); }
            catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
            if (SpeciesDatabase.IsWholeNumberField(field.Path)
                && !int.TryParse(field.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"{field.Path}: a group size must be a whole number of 0 or more.");
        }
        Manifest.Species[index] = new SpeciesEntry
        {
            Id = old.Id, Base = old.Base, Name = name.Trim(), Genus = genus.Trim(), Epithet = epithet.Trim(), Cost = cost,
            Stats = new Dictionary<string, double>(stats),
            Fields = copiedFields,
            Localization = new Dictionary<string, string>(localization ?? old.Localization),
            Icon = old.Icon, PaleopediaPicture = old.PaleopediaPicture,
            Prefab = old.Prefab,
            StartingSkin = old.StartingSkin, StartingSkinName = old.StartingSkinName, Swatch = old.Swatch,
            Portraits = new Dictionary<string, string>(old.Portraits, StringComparer.Ordinal),
            Texts = old.Texts.ToList(),
        };
        try { EnsureLocalizationFiles(Manifest.Species[index]); SaveSpecies(); }
        catch { Manifest.Species[index] = old; throw; }
        // The English name file is what the game shows, so it follows the Display name.
        if (Manifest.Species[index].Name != old.Name && Manifest.Species[index].Localization.TryGetValue("name", out var nameTerm))
            WriteText(AnimalLocalization.FileFor(Dir, "en-US", nameTerm), Manifest.Species[index].Name);
    }

    private void SaveSpecies()
    {
        try { AnimalDatabaseFile.Save(DatabasePath, Manifest.Species, _databaseSnapshot); }
        catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, ex.Message); }
        _databaseSnapshot = AnimalDatabaseFile.Snapshot(Manifest.Species);
    }

    public void EnsureLocalizationFiles(SpeciesEntry entry)
    {
        // English gets starter text; other languages start empty, and empty text falls back to English in game.
        foreach (var code in AnimalLocalization.Codes)
        foreach (var term in entry.Localization)
        {
            var path = AnimalLocalization.FileFor(Dir, code, term.Value);
            if (File.Exists(path)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var text = (code, term.Key) switch
            {
                ("en-US", "name") => entry.Name,
                ("en-US", "description") => entry.Name + " is a custom species based on " + entry.Base + ".",
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

    /// <summary>
    /// The species' own Paleopedia page picture (copied to icons/&lt;id&gt;-paleopedia.png); null goes back to the template's.
    /// In game it replaces the picture of the species' own copy of the template's info sign.
    /// </summary>
    public void SetSpeciesPaleopediaPicture(string id, string? png)
    {
        var entry = Manifest.Species.FirstOrDefault(s => s.Id == id)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + id);
        var old = entry.PaleopediaPicture;
        var file = png == null ? null : $"icons/{id}-paleopedia.png";
        if (png != null) CopyPng(png, file!);
        entry.PaleopediaPicture = file;
        try { SaveSpecies(); }
        catch { entry.PaleopediaPicture = old; throw; }
    }

    /// <summary>Which template skin the species starts with, and its name in game (blank = the template skin's name).</summary>
    public void SetSpeciesStartingSkin(IReadOnlyList<SpeciesSkins> vanilla, string id, int startingSkin, string? name)
    {
        var entry = Manifest.Species.FirstOrDefault(s => s.Id == id)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + id);
        var template = vanilla.FirstOrDefault(s => s.SpeciesId == entry.Base && !s.Vivarium)
            ?? throw new TyrantException(TyrantErrorCode.DataMissing, $"'{entry.Base}' is not in the game data dump; run the data dump first.");
        if (!template.Skins.Any(s => s.Index == startingSkin))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{template.SpeciesId} has no skin number {startingSkin}. Its skins: {string.Join(", ", template.Skins.Select(s => $"{s.Index} {s.Name}"))}.");
        var (oldSkin, oldName) = (entry.StartingSkin, entry.StartingSkinName);
        entry.StartingSkin = startingSkin;
        entry.StartingSkinName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        try { SaveSpecies(); }
        catch { (entry.StartingSkin, entry.StartingSkinName) = (oldSkin, oldName); throw; }
    }

    /// <summary>The starting skin's swatch PNG (copied to icons/&lt;id&gt;.swatch.png); null goes back to the template's.</summary>
    public void SetSpeciesSwatch(string id, string? png)
    {
        var entry = Manifest.Species.FirstOrDefault(s => s.Id == id)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + id);
        var old = entry.Swatch;
        var file = png == null ? null : $"icons/{id}.swatch.png";
        if (png != null) CopyPng(png, file!);
        entry.Swatch = file;
        try { SaveSpecies(); }
        catch { entry.Swatch = old; throw; }
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

    /// <summary>
    /// The species this mod's skins and models are looked up in: <see cref="WithSpecies"/> with this mod's custom species and,
    /// when its skins use other mods' (<see cref="SpeciesUse"/>), those from the workspace and the game's installed mods.
    /// </summary>
    public IReadOnlyList<SpeciesSkins> SpeciesFor(Workspace ws, GameInstall install, IReadOnlyList<SpeciesSkins> dump) =>
        WithSpecies(dump, Manifest, SpeciesUse.Of(Manifest).Count > 0 ? OtherModSpecies.Load(ws, new GameMods().List(install), Id).Species : null);

    /// <summary>
    /// The game's species plus custom ones (this mod's, then other mods'), each a copy of its template's row with only its
    /// starting skin, renumbered 0: the one skin the species has in game before mods add theirs.
    /// </summary>
    public static IReadOnlyList<SpeciesSkins> WithSpecies(IReadOnlyList<SpeciesSkins> vanilla, ModManifest manifest, IEnumerable<OtherSpecies>? others = null)
    {
        var result = vanilla.ToList();
        void Add(string key, SpeciesEntry entry)
        {
            var template = vanilla.FirstOrDefault(s => s.SpeciesId == entry.Base && !s.Vivarium);
            var start = template?.Skins.FirstOrDefault(s => s.Index == entry.StartingSkin);
            if (template is null || start is null || result.Any(s => s.SpeciesId == key)) return;
            result.Add(template with { SpeciesId = key, Skins = [start with { Index = 0, Name = entry.StartingSkinName ?? start.Name }] });
        }
        foreach (var entry in manifest.Species) Add(entry.Key(manifest.Id), entry);
        foreach (var other in others ?? []) Add(other.Key, other.Entry);
        return result;
    }
}
