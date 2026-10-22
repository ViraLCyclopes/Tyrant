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
    public static SpeciesTemplateStats? Template(Workspace ws, string id) => Find(ws, id, withFields: true);

    /// <summary>
    /// Record rows the game holds as whole numbers (Vector2Int group sizes) although the dump writes them like any number:
    /// "2.5" there would stop the species loading in game.
    /// </summary>
    public static bool IsWholeNumberField(string path) =>
        System.Text.RegularExpressions.Regex.IsMatch(path, "/social(Male|Female)CountRange/[xy]$");

    /// <summary>
    /// A species' stored record with each empty-object row ({}) that the template now has rows under replaced by copies of
    /// them. Older dumps wrote some values (Vector2Int group sizes…) as {}; species saved then keep their other edits.
    /// </summary>
    public static List<AnimalField> FillEmptyObjects(IReadOnlyList<AnimalField> stored, IReadOnlyList<AnimalField>? template)
    {
        var result = new List<AnimalField>();
        foreach (var field in stored)
        {
            var under = field.Kind == "object" && field.Value == "{}" && template != null
                ? template.Where(t => t.Path.StartsWith(field.Path + "/", StringComparison.Ordinal) && stored.All(s => s.Path != t.Path)).ToList()
                : [];
            if (under.Count == 0) result.Add(field);
            else result.AddRange(under.Select(t => t.Copy()));
        }
        return result;
    }

    /// <summary>A template's cost and basic stats only (no full record): what the new-species form shows.</summary>
    public static SpeciesTemplateStats? TemplateStats(Workspace ws, string id) => Find(ws, id, withFields: false);

    private static SpeciesTemplateStats? Find(Workspace ws, string id, bool withFields)
    {
        try
        {
            var store = DataStore.Open(ws);
            var type = store.FindType("PrehistoricKingdom.AnimalData");
            foreach (var (_, root) in store.LoadAll(type))
                if (root.TryGetProperty("speciesID", out var species) && species.GetString() == id)
                    return withFields ? Read(root) with { Fields = Capture(store, root) } : Read(root);
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound) { }
        return null;
    }

    /// <summary>
    /// Whether the game species walks on four legs (its animation table's locomotion style): the head look then measures
    /// from the first neck bone, else from the bone above it. Null when the data dump does not say.
    /// </summary>
    public static bool? IsQuadrupedal(Workspace ws, string id)
    {
        try
        {
            var store = DataStore.Open(ws);
            foreach (var (_, root) in store.LoadAll(store.FindType("PrehistoricKingdom.AnimalData")))
            {
                if (!(root.TryGetProperty("speciesID", out var species) && species.GetString() == id)) continue;
                if (!root.TryGetProperty("animationTable", out var table) || !table.TryGetProperty("$ref", out var reference)) return null;
                var data = store.Load(store.FindType("PrehistoricKingdom.AnimationV2.AnimationTableV2"), reference.GetProperty("name").GetString()!);
                return data.TryGetProperty("locomotionStyle", out var style) ? style.GetString() == "Quadrupedal" : null;
            }
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound or TyrantErrorCode.AssetAmbiguous) { }
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
            if (!aliases.Contains(path) && !SpeciesSizes.IsSizePath(path)) result.Add(new AnimalField { Path = path, Kind = kind, Value = value, TemplateValue = value, Editable = editable });
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
