using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed partial class ModProject
{
    /// <summary>A species' text files, language code → field (name, description, characterNotes, socialInfo) → text; missing files read as empty.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SpeciesTexts(string id)
    {
        var entry = Manifest.Species.FirstOrDefault(s => s.Id == id)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"No species '{id}' in this mod.");
        return AnimalLocalization.Codes.ToDictionary(code => code, code => (IReadOnlyDictionary<string, string>)AnimalText.Keys.ToDictionary(slot => slot, slot =>
        {
            if (!entry.Localization.TryGetValue(slot, out var term)) return "";
            var path = AnimalLocalization.FileFor(Dir, code, term);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }));
    }

    /// <summary>
    /// Writes the given texts (language code → field → text). An empty translation falls back to English in game. The English
    /// name is the Display name (<see cref="SetSpecies"/>). A field without a key gets the default key &lt;species-id&gt;/&lt;field&gt;.
    /// </summary>
    public void SetSpeciesTexts(string id, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts)
    {
        var index = Manifest.Species.FindIndex(s => s.Id == id);
        if (index < 0) throw new TyrantException(TyrantErrorCode.TargetNotFound, $"No species '{id}' in this mod.");
        foreach (var (code, fields) in texts)
        {
            if (!AnimalLocalization.Codes.Contains(code))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"Unknown language '{code}'. Use one of {string.Join(", ", AnimalLocalization.Codes)}.");
            foreach (var slot in fields.Keys)
            {
                if (!AnimalText.Keys.Contains(slot))
                    throw new TyrantException(TyrantErrorCode.ModInvalid, $"Unknown text '{slot}'. Use one of {string.Join(", ", AnimalText.Keys)}.");
                if (code == "en-US" && slot == "name")
                    throw new TyrantException(TyrantErrorCode.ModInvalid, "The English name is the species' Display name: change that instead.");
            }
        }
        var entry = Manifest.Species[index];
        var missing = texts.Values.SelectMany(f => f.Keys).Distinct().Where(slot => !entry.Localization.ContainsKey(slot)).ToList();
        if (missing.Count > 0)
        {
            var keys = new Dictionary<string, string>(entry.Localization);
            foreach (var slot in missing) keys[slot] = id + "/" + slot;
            SetSpecies(id, entry.Name, entry.Genus, entry.Epithet, entry.Cost, entry.Stats, localization: keys);
            entry = Manifest.Species[index];
        }
        foreach (var (code, fields) in texts)
            foreach (var (slot, text) in fields)
                WriteText(AnimalLocalization.FileFor(Dir, code, entry.Localization[slot]), text);
    }

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text); File.Move(temp, path, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
