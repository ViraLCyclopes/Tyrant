using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>A species' size as the game data has it: the growth curve keys, each skin's sex size multipliers and the model's raw length, height and width.</summary>
public sealed record OriginalSize((float Time, float Value)[] Curve, IReadOnlyList<(double Male, double Female)> Skins, float[]? LengthHeightWidth);

/// <summary>What Check needs to judge sizes: every species' original size and the vanilla genetic size ranges.</summary>
public sealed record SizeFacts(Func<string, OriginalSize?> Original, (double Min, double Max) Incubation, (double Min, double Max) Breeding);

/// <summary>Original animal sizes from the game data dump, and which animal record paths belong to Size rather than the field editor.</summary>
public static partial class SpeciesSizes
{
    private const string AnimalType = "PrehistoricKingdom.AnimalData";
    private const string ParametersType = "PrehistoricKingdom.AnimalsV2.Data.GlobalAnimalParameters";

    /// <summary>The game's random range when hatching (GlobalAnimalParameters.incubationGeneticSizeMinMax), used if the dump has none.</summary>
    public static readonly (double Min, double Max) DefaultIncubation = (0.925, 1.075);
    public static readonly (double Min, double Max) DefaultBreeding = (0.85, 1.15);

    [GeneratedRegex(@"^/skinsData/\d+/(male|female)SizeMultiplier$")]
    private static partial Regex SkinSizePath();

    /// <summary>The record paths Size sets (the growth curve and the skins' sex multipliers); the species field editor hides them.</summary>
    public static bool IsSizePath(string path) =>
        path == "/scaleGrowthCurve" || path.StartsWith("/scaleGrowthCurve/", StringComparison.Ordinal) || SkinSizePath().IsMatch(path);

    /// <summary>
    /// A species' original size: a game species straight from the dump; one of the mod's own species (named by its key) from its
    /// template plus the mod's edits of the record's size fields. Null when the dump has no such species.
    /// </summary>
    public static OriginalSize? Original(DataStore store, string species, ModProject? mod)
    {
        var own = mod?.Manifest.Species.FirstOrDefault(s => s.Key(mod.Id) == species);
        var id = own?.Base ?? species;
        foreach (var (_, root) in store.LoadAll(store.FindType(AnimalType)))
            if (root.TryGetProperty("speciesID", out var found) && found.GetString() == id) return Read(root, own);
        return null;
    }

    /// <summary>The size facts of the dump (and so of one mod's custom species); null when there is no dump.</summary>
    public static SizeFacts? Facts(Workspace ws, ModProject mod)
    {
        var roots = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var incubation = DefaultIncubation;
        var breeding = DefaultBreeding;
        try
        {
            var store = DataStore.Open(ws);
            foreach (var (_, root) in store.LoadAll(store.FindType(AnimalType)))
                if (root.TryGetProperty("speciesID", out var id) && id.GetString() is { } name) roots[name] = root;
            if (store.Types().Any(t => t.FullName == ParametersType))
                foreach (var (_, root) in store.LoadAll(store.FindType(ParametersType)))
                {
                    incubation = Range(root, "incubationGeneticSizeMinMax") ?? incubation;
                    breeding = Range(root, "breedingGeneticSizeMinMax") ?? breeding;
                }
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound)
        {
            return null;
        }
        return new SizeFacts(species =>
        {
            var own = mod.Manifest.Species.FirstOrDefault(s => s.Key(mod.Id) == species);
            return roots.TryGetValue(own?.Base ?? species, out var root) ? Read(root, own) : null;
        }, incubation, breeding);
    }

    private static (double, double)? Range(JsonElement root, string name) =>
        root.TryGetProperty(name, out var range) && range.ValueKind == JsonValueKind.Object
            && range.TryGetProperty("x", out var x) && x.TryGetDouble(out var min) && range.TryGetProperty("y", out var y) && y.TryGetDouble(out var max)
            && min > 0 && min <= max ? (min, max) : null;

    private static OriginalSize Read(JsonElement root, SpeciesEntry? own)
    {
        // A custom species may carry edits of these fields from before Size existed (animals.db); they still decide its size.
        var edits = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var field in own?.Fields ?? [])
            if ((IsSizePath(field.Path) || field.Path.StartsWith("/animalLengthHeightWidthRaw/", StringComparison.Ordinal))
                && double.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) edits[field.Path] = number;
        double Value(string path, JsonElement element) => edits.TryGetValue(path, out var edited) ? edited : element.TryGetDouble(out var n) ? n : 0;

        var curve = new List<(float, float)>();
        if (root.TryGetProperty("scaleGrowthCurve", out var growth) && growth.ValueKind == JsonValueKind.Object
            && growth.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var key in keys.EnumerateArray())
            {
                var time = key.TryGetProperty("time", out var t) ? Value($"/scaleGrowthCurve/keys/{i}/time", t) : 0;
                var value = key.TryGetProperty("value", out var v) ? Value($"/scaleGrowthCurve/keys/{i}/value", v) : 0;
                curve.Add(((float)time, (float)value));
                i++;
            }
        }
        var skins = new List<(double, double)>();
        if (root.TryGetProperty("skinsData", out var skinsData) && skinsData.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var skin in skinsData.EnumerateArray())
            {
                var at = i;
                double Multiplier(string name) => skin.ValueKind == JsonValueKind.Object && skin.TryGetProperty(name, out var m) ? Value($"/skinsData/{at}/{name}", m) : 1;
                skins.Add((Multiplier("maleSizeMultiplier"), Multiplier("femaleSizeMultiplier")));
                i++;
            }
        }
        float[]? size = null;
        if (root.TryGetProperty("animalLengthHeightWidthRaw", out var raw) && raw.ValueKind == JsonValueKind.Object)
            size = new[] { "x", "y", "z" }.Select(axis => raw.TryGetProperty(axis, out var a) ? (float)Value($"/animalLengthHeightWidthRaw/{axis}", a) : 0f).ToArray();
        return new OriginalSize(curve.ToArray(), skins, size);
    }
}
