using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed record SpeciesTemplateStats(int? Cost, IReadOnlyDictionary<string, double> Stats)
{
    public IReadOnlyList<AnimalField> Fields { get; init; } = [];
}

/// <summary>Reads the inherited side of Tyrant's custom animal database from the player's game dump.</summary>
public static class SpeciesDatabase
{
    public static SpeciesTemplateStats? Template(Workspace ws, string id)
    {
        try
        {
            var store = DataStore.Open(ws);
            var type = store.FindType("PrehistoricKingdom.AnimalData");
            foreach (var (_, root) in store.LoadAll(type))
                if (root.TryGetProperty("speciesID", out var species) && species.GetString() == id)
                    return Read(root) with { Fields = Capture(store, root) };
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound) { }
        return null;
    }

    public static SpeciesTemplateStats Read(JsonElement root)
    {
        var result = new Dictionary<string, double>();
        foreach (var field in SpeciesStats.Fields)
        {
            if (!root.TryGetProperty(field.Field, out var value)) continue;
            if (field.Component != null && (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(field.Component, out value))) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) result[field.Id] = number;
        }
        int? cost = root.TryGetProperty("cost", out var c) && c.TryGetInt32(out var n) ? n : null;
        return new SpeciesTemplateStats(cost, result);
    }

    /// <summary>Complete serialized record, expanding linked dumped assets while stopping cycles and world/build databases.</summary>
    public static IReadOnlyList<AnimalField> Capture(DataStore store, JsonElement root)
    {
        var result = new List<AnimalField>();
        var visiting = new HashSet<string>();
        if (root.TryGetProperty("$type", out var rt) && root.TryGetProperty("$name", out var rn)) visiting.Add(rt.GetString() + ":" + rn.GetString());
        var aliases = SpeciesStats.Fields.Select(f => "/" + f.Field + (f.Component == null ? "" : "/" + f.Component)).ToHashSet();
        aliases.Add("/cost");
        void Add(string path, string kind, string value, bool editable)
        {
            if (!aliases.Contains(path)) result.Add(new AnimalField { Path = path, Kind = kind, Value = value, TemplateValue = value, Editable = editable });
        }
        void Walk(JsonElement value, string path, bool editable, int depth, int references = 0)
        {
            if (depth > 96) throw new TyrantException(TyrantErrorCode.ModInvalid, "Animal asset graph exceeds 96 levels: " + path);
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    if (value.TryGetProperty("$ref", out var reference))
                    {
                        Add(path, "reference", reference.GetRawText(), false);
                        var type = reference.GetProperty("type").GetString()!;
                        var name = reference.GetProperty("name").GetString()!;
                        var key = type + ":" + name;
                        // Three asset links cover brain, its module configurations and their profiles.
                        // Disease/research/world graphs beyond that are shared game databases, not species records.
                        if (references >= 3 || type.StartsWith("UnityEngine.") || type.StartsWith("PrehistoricKingdom.DBEntry") || type.Contains("Excavation") || !visiting.Add(key)) return;
                        try
                        {
                            var linked = store.Load(store.FindType(type), name);
                            Walk(linked, path, editable, depth + 1, references + 1);
                        }
                        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.AssetNotFound or TyrantErrorCode.AssetAmbiguous) { }
                        finally { visiting.Remove(key); }
                        return;
                    }
                    if (!value.EnumerateObject().Any()) Add(path, "object", "{}", false);
                    foreach (var pair in value.EnumerateObject())
                    {
                        if (pair.Name.StartsWith('$')) continue;
                        var child = path + "/" + pair.Name.Replace("~", "~0").Replace("/", "~1");
                        var canEdit = editable && pair.Name != "Parent" && pair.Name != "mainData" && pair.Name != "animalData";
                        if (path == "" && pair.Name is "speciesID" or "shortSpeciesID" or "displayName" or "genusName" or "speciesName") canEdit = false;
                        if (path == "" && pair.Name is "paleopediaDescription" or "paleopediaCharacterNotes" or "paleopediaSocialInfo") canEdit = false;
                        Walk(pair.Value, child, canEdit, depth + 1, references);
                    }
                    break;
                case JsonValueKind.Array:
                    Add(path, "array", value.GetArrayLength().ToString(System.Globalization.CultureInfo.InvariantCulture), false);
                    int index = 0;
                    foreach (var item in value.EnumerateArray()) Walk(item, path + "/" + index++, editable, depth + 1, references);
                    break;
                case JsonValueKind.Number: Add(path, "number", value.GetRawText(), editable); break;
                case JsonValueKind.True: case JsonValueKind.False: Add(path, "boolean", value.GetRawText(), editable); break;
                case JsonValueKind.String:
                    var text = value.GetString()!;
                    Add(path, "text", text, editable && !text.StartsWith("$")); break;
                case JsonValueKind.Null: Add(path, "null", "null", false); break;
            }
        }
        Walk(root, "", true, 0);
        return result;
    }

    public static void ValidateEffective(IReadOnlyDictionary<string, double> overrides, IReadOnlyDictionary<string, double> inherited)
    {
        SpeciesStats.Validate(overrides);
        foreach (var group in SpeciesStats.Fields.Where(f => f.Component != null).GroupBy(f => f.Field))
        {
            if (!group.Any(f => overrides.ContainsKey(f.Id))) continue;
            var min = group.Single(f => f.Component == "x").Id;
            var max = group.Single(f => f.Component == "y").Id;
            double? Value(string key) => overrides.TryGetValue(key, out var own) ? own : inherited.TryGetValue(key, out var basis) ? basis : null;
            if (Value(min) is double low && Value(max) is double high && low > high)
                throw new ManifestException($"{min} must not exceed {max}, including inherited template values.");
        }
    }
}
