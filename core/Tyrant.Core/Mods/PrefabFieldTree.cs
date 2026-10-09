using System.Text.Json;
using Tyrant.Core.Models;

namespace Tyrant.Core.Mods;

/// <summary>One field of a prefab component as prefab settings shows it: where, what kind, the game's value, whether it can change.</summary>
public sealed record PrefabFieldInfo(string Object, string Component, string Field, string Kind, string Value, bool Editable, IReadOnlyList<string>? EnumNames = null, IReadOnlyList<long>? EnumValues = null);

/// <summary>One enum member: its name and real numeric value.</summary>
public sealed record PrefabEnumValue(string Name, long Value);

/// <summary>Flattens a prefab's components into the fields prefab settings lists, and checks a new value against a field.</summary>
public static class PrefabFieldTree
{
    private static readonly string[] Header = ["m_GameObject", "m_Enabled", "m_Script", "m_Name", "m_EditorHideFlags", "m_EditorClassIdentifier"];
    private static readonly PrefabEnumValue[] CullingModes = [new("AlwaysAnimate", 0), new("CullUpdateTransforms", 1), new("CullCompletely", 2)];

    public static IReadOnlyList<PrefabFieldInfo> Build(PrefabComponents prefab, Func<PrefabComponent, string, IReadOnlyList<PrefabEnumValue>?> enumNames)
    {
        var result = new List<PrefabFieldInfo>();
        foreach (var obj in prefab.Objects)
            foreach (var component in obj.Components)
            {
                var name = component.Index == 0 ? component.Type : $"{component.Type}#{component.Index}";
                var first = result.Count;
                using var doc = JsonDocument.Parse(component.FieldsJson);
                var root = doc.RootElement;
                if (component.Type == "LODGroup")
                {
                    if (root.TryGetProperty("m_LODs", out var lods) && lods.ValueKind == JsonValueKind.Array)
                        for (var i = 0; i < lods.GetArrayLength(); i++)
                            if (lods[i].ValueKind == JsonValueKind.Object && lods[i].TryGetProperty("screenRelativeHeight", out var height))
                                result.Add(new(obj.Path, name, $"lods[{i}].screenRelativeTransitionHeight", "number", Compact(height), true));
                    MarkDuplicate(result, first, component);
                    continue;
                }
                if (component.Type == "Animator")
                {
                    if (root.TryGetProperty("m_ApplyRootMotion", out var motion))
                        result.Add(new(obj.Path, name, "applyRootMotion", "bool", (motion.ValueKind == JsonValueKind.True || (motion.ValueKind == JsonValueKind.Number && motion.TryGetDouble(out var m) && m != 0)) ? "true" : "false", true));
                    if (root.TryGetProperty("m_CullingMode", out var culling))
                        result.Add(new(obj.Path, name, "cullingMode", "enum", Compact(culling), true, CullingModes.Select(c => c.Name).ToList(), CullingModes.Select(c => c.Value).ToList()));
                    MarkDuplicate(result, first, component);
                    continue;
                }
                foreach (var property in root.EnumerateObject())
                    if (!Header.Contains(property.Name))
                        Flatten(obj.Path, name, property.Name, property.Value, result, path => enumNames(component, path));
                MarkDuplicate(result, first, component);
            }
        return result;
    }

    /// <summary>A component whose object shares its path with an earlier one cannot be addressed by the game; its fields are shown but not editable.</summary>
    private static void MarkDuplicate(List<PrefabFieldInfo> fields, int first, PrefabComponent component)
    {
        if (!component.DuplicatePath) return;
        for (var i = first; i < fields.Count; i++) fields[i] = fields[i] with { Kind = "duplicate", Editable = false, EnumNames = null, EnumValues = null };
    }

    private static readonly JsonSerializerOptions CompactOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The element as compact JSON (the game's fields are written indented); numbers keep their text.</summary>
    private static string Compact(JsonElement e) =>
        e.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? JsonSerializer.Serialize(e, CompactOptions) : e.GetRawText();

    private static void Flatten(string obj, string component, string path, JsonElement value, List<PrefabFieldInfo> into, Func<string, IReadOnlyList<PrefabEnumValue>?> enums)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                var names = enums(Strip(path));
                into.Add(new(obj, component, path, names is null ? "number" : "enum", Compact(value), true, names?.Select(n => n.Name).ToList(), names?.Select(n => n.Value).ToList()));
                return;
            case JsonValueKind.True or JsonValueKind.False:
                into.Add(new(obj, component, path, "bool", Compact(value), true));
                return;
            case JsonValueKind.String:
                into.Add(new(obj, component, path, "string", Compact(value), true));
                return;
            case JsonValueKind.Array:
                into.Add(new(obj, component, path, "length", value.GetArrayLength().ToString(System.Globalization.CultureInfo.InvariantCulture), false));
                var i = 0;
                foreach (var item in value.EnumerateArray())
                {
                    // a "$more" marker stands for items the reader left out; it is not a field
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("$more", out _)) continue;
                    Flatten(obj, component, $"{path}[{i++}]", item, into, enums);
                }
                return;
            case JsonValueKind.Object:
                var keys = value.EnumerateObject().Select(p => p.Name).ToHashSet();
                if (keys.SetEquals(["m_FileID", "m_PathID"])) into.Add(new(obj, component, path, "reference", Compact(value), false));
                else if (keys.Contains("$array") || keys.Contains("$bytes")) into.Add(new(obj, component, path, "length",
                    value.TryGetProperty("count", out var count) ? Compact(count) : value.TryGetProperty("$bytes", out var bytes) ? Compact(bytes) : "0", false));
                else if (Vector(keys) is { } kind) into.Add(new(obj, component, path, kind, Compact(value), true));
                else foreach (var p in value.EnumerateObject()) Flatten(obj, component, $"{path}.{p.Name}", p.Value, into, enums);
                return;
        }
    }

    private static string? Vector(HashSet<string> keys) =>
        keys.SetEquals(["x", "y"]) ? "vector2" : keys.SetEquals(["x", "y", "z"]) ? "vector3" : keys.SetEquals(["x", "y", "z", "w"]) ? "vector4"
        : keys.SetEquals(["r", "g", "b", "a"]) ? "color" : null;

    /// <summary>The path without list indices, for looking up a field's declared type.</summary>
    private static string Strip(string path) => System.Text.RegularExpressions.Regex.Replace(path, @"\[\d+\]", "");

    private static bool Finite(JsonElement number) => number.TryGetDouble(out var d) && double.IsFinite(d);

    public static string? CheckValue(PrefabFieldInfo field, JsonElement value)
    {
        if (field.Kind == "duplicate") return $"{field.Field} cannot be changed (another object has the same path, so the game cannot address this one).";
        if (!field.Editable) return $"{field.Field} cannot be changed (it is a {(field.Kind == "reference" ? "link to another object" : "list size")}).";
        string[] Parts(string kind) => kind switch { "vector2" => ["x", "y"], "vector3" => ["x", "y", "z"], "vector4" => ["x", "y", "z", "w"], _ => ["r", "g", "b", "a"] };
        switch (field.Kind)
        {
            case "number":
                return value.ValueKind == JsonValueKind.Number && Finite(value) ? null : $"{field.Field} needs a finite number.";
            case "bool":
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : $"{field.Field} needs true or false.";
            case "string":
                return value.ValueKind == JsonValueKind.String ? null : $"{field.Field} needs text.";
            case "enum":
                var names = field.EnumNames ?? [];
                var ok = value.ValueKind == JsonValueKind.String ? names.Contains(value.GetString())
                    : value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && (field.EnumValues?.Contains(n) ?? false);
                return ok ? null : $"{field.Field} needs one of {string.Join(", ", names)}.";
            default:
                var parts = Parts(field.Kind);
                if (value.ValueKind != JsonValueKind.Object) return $"{field.Field} needs {string.Join(", ", parts)}.";
                var missing = parts.Where(p => !value.TryGetProperty(p, out var v) || v.ValueKind != JsonValueKind.Number || !Finite(v)).ToList();
                return missing.Count == 0 ? null : $"{field.Field} needs a finite number for {string.Join(", ", missing)}.";
        }
    }
}
