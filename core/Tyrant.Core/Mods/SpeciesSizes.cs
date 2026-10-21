using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tyrant.Core.Blender;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>
/// A species' size as the game data has it: the growth curve as the game reads it (sampled, <see cref="SizeMath.Sample"/>), each
/// skin's sex size multipliers and the model's raw length, height and width. Name: the species as the game shows it (a mod's
/// own species by its display name); BaseName: the animal its sizes are relative to (a custom species' template's display name).
/// </summary>
public sealed record OriginalSize((float Time, float Value)[] Curve, IReadOnlyList<(double Male, double Female)> Skins, float[]? LengthHeightWidth)
{
    public string Name { get; init; } = "";
    public string BaseName { get; init; } = "";
}

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

    /// <summary>A mod species' record edits that change its size outside Size (made before Size existed; the field editor hides them).</summary>
    public static IReadOnlyList<string> RecordEdits(SpeciesEntry entry) =>
        entry.Fields.Where(f => IsSizePath(f.Path) && f.Value != f.TemplateValue).Select(f => f.Path).ToList();

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
        (double Min, double Max) incubation, breeding;
        try
        {
            var store = DataStore.Open(ws);
            foreach (var (_, root) in store.LoadAll(store.FindType(AnimalType)))
                if (root.TryGetProperty("speciesID", out var id) && id.GetString() is { } name) roots[name] = root;
            (incubation, breeding) = GameRanges(store);
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

    /// <summary>The game's genetic size ranges (random eggs', which the Nursery slider also uses, and breeding's); the vanilla ones without a dump.</summary>
    public static ((double Min, double Max) Incubation, (double Min, double Max) Breeding) GameRanges(Workspace ws)
    {
        try
        {
            return GameRanges(DataStore.Open(ws));
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound)
        {
            return (DefaultIncubation, DefaultBreeding);
        }
    }

    private static ((double Min, double Max), (double Min, double Max)) GameRanges(DataStore store)
    {
        var (incubation, breeding) = (DefaultIncubation, DefaultBreeding);
        if (store.Types().Any(t => t.FullName == ParametersType))
            foreach (var (_, root) in store.LoadAll(store.FindType(ParametersType)))
            {
                incubation = Range(root, "incubationGeneticSizeMinMax") ?? incubation;
                breeding = Range(root, "breedingGeneticSizeMinMax") ?? breeding;
            }
        return (incubation, breeding);
    }

    /// <summary>
    /// The smallest and largest scale a species reaches with these sizes, as Check counts it: every skin's sexes and the genetic
    /// ranges in effect for it, as the game has them (the random eggs', its Nursery slider's and its breeding range: its own,
    /// else the game's); null when its data has no growth curve.
    /// </summary>
    public static (float Min, float Max)? Extremes(OriginalSize original, SizeSettings sizes, SizeFacts facts)
    {
        if (original.Curve.Length == 0) return null;
        var curve = SizeMath.Rebuild(original.Curve, sizes.Adult ?? 1, sizes.Baby);
        var skins = original.Skins.Count == 0 ? [(1.0, 1.0)] : original.Skins;
        var sexes = skins.SelectMany(k => { var (male, female) = SizeMath.Sex(k.Male, k.Female, sizes); return new[] { male, female }; });
        return SizeMath.Extremes(curve, sexes, SizePlan.InEffect(sizes, facts.Incubation, facts.Breeding));
    }

    /// <summary>The species' size limit with these sizes: automatic, the one its extremes need (<see cref="SizeLimit.Fitting"/>); null without a curve.</summary>
    public static SizeLimit? LimitOf(OriginalSize original, SizeSettings sizes, SizeFacts facts) =>
        Extremes(original, sizes, facts) is { } e ? SizeLimit.Fitting(e.Min, e.Max) : null;

    /// <summary>
    /// Check's error when the sizes take the species past Tyrant's own bounds (over 100 or under 0.01, <see cref="SizeLimit.PastBounds"/>):
    /// how big (or small) it gets, relative to a normal animal of its base species, and what makes it so; null within them. The
    /// extreme is found as <see cref="Extremes"/> finds it (the same samples and float maths), so the number is Check's own, and the
    /// sex named is the one that gives it (the skin's own sex size times the mod's multiplier). A grown adult is a plain product of
    /// the factors named; anything younger is not (the game fades the genetic size in), so its inputs are listed instead.
    /// </summary>
    public static string? PastBoundsProblem(OriginalSize original, SizeSettings sizes, SizeFacts facts)
    {
        if (original.Curve.Length == 0) return null;
        var adult = sizes.Adult ?? 1;
        var curve = SizeMath.Rebuild(original.Curve, adult, sizes.Baby);
        var skins = original.Skins.Count == 0 ? [(1.0, 1.0)] : original.Skins;
        var sexes = skins.SelectMany(k => new[] { (Name: "male", Size: k.Male * (sizes.Male ?? 1), Final: k.Male * adult * (sizes.Male ?? 1)),
            (Name: "female", Size: k.Female * (sizes.Female ?? 1), Final: k.Female * adult * (sizes.Female ?? 1)) }).ToList();
        var genetics = new[] { (Site: SizeSite.RandomEgg, Label: "random eggs"), (Site: SizeSite.NurserySlider, Label: "Nursery slider"), (Site: SizeSite.Breeding, Label: "breeding") }
            .Select(x => (Range: SizePlan.RangeFor(x.Site, sizes, facts.Incubation, facts.Breeding), x.Label))
            .SelectMany(x => new[] { (x.Label, G: x.Range.Min), (x.Label, G: x.Range.Max) }).ToList();
        // The same samples as SizeMath.Extremes; the first of equal sizes is kept.
        (float Size, int Step, int Sex, int Gene) low = (float.MaxValue, 0, 0, 0), high = (float.MinValue, 0, 0, 0);
        for (var i = 0; i < 101; i++)
        {
            var m = i / 100f;
            var at = SizeMath.Evaluate(curve, m);
            var fade = SizeMath.Fade(m);
            for (var x = 0; x < sexes.Count; x++)
                for (var y = 0; y < genetics.Count; y++)
                {
                    var size = (float)(at * (1 + (sexes[x].Final * genetics[y].G - 1) * fade));
                    if (size < low.Size) low = (size, i, x, y);
                    if (size > high.Size) high = (size, i, x, y);
                }
        }
        if (!SizeLimit.PastBounds(low.Size, high.Size)) return null;
        if (high.Size > SizeLimit.HighestMax)
        {
            var (sex, gene) = (sexes[high.Sex], genetics[high.Gene]);
            var grown = SizeMath.Evaluate(curve, 1f);
            var why = high.Step == 100
                ? (Math.Abs(grown - 1) > 1e-4 ? $"grown size {Text(grown)} × " : "") + $"adult {Text(adult)} × {sex.Name} {Text(sex.Size)} × {gene.Label} up to {Text(gene.G)}"
                : $"at {high.Step}% grown, with adult {Text(adult)}, {sex.Name} {Text(sex.Size)} and {gene.Label} up to {Text(gene.G)}";
            return $"The biggest {original.Name} can reach {high.Size.ToString("0.##", CultureInfo.InvariantCulture)}× a normal {original.BaseName} ({why}), "
                + $"more than Tyrant's {Text(SizeLimit.HighestMax)}× limit; lower the adult size or the size ranges.";
        }
        {
            var (sex, gene) = (sexes[low.Sex], genetics[low.Gene]);
            var c1 = SizeMath.Evaluate(original.Curve, 1f);
            var baby = sizes.Baby ?? (c1 == 0f ? 1 : SizeMath.Evaluate(original.Curve, 0f) / (double)c1);
            var when = low.Step == 0 ? "a hatchling" : $"at {low.Step}% grown";
            return $"The smallest {original.Name} can be {low.Size.ToString("0.###", CultureInfo.InvariantCulture)}× a normal {original.BaseName} "
                + $"({when}, with adult {Text(adult)}, baby {Text(baby)}, {sex.Name} {Text(sex.Size)} and {gene.Label} down to {Text(gene.G)}), "
                + $"less than Tyrant's {Text(SizeLimit.LowestMin)}× limit; raise the adult size, the baby size or the size ranges.";
        }
    }

    private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static (double, double)? Range(JsonElement root, string name) =>
        root.TryGetProperty(name, out var range) && range.ValueKind == JsonValueKind.Object
            && range.TryGetProperty("x", out var x) && x.TryGetDouble(out var min) && range.TryGetProperty("y", out var y) && y.TryGetDouble(out var max)
            && min > 0 && min <= max ? (min, max) : null;

    private static OriginalSize Read(JsonElement root, SpeciesEntry? own)
    {
        var id = root.TryGetProperty("speciesID", out var speciesId) && speciesId.ValueKind == JsonValueKind.String ? speciesId.GetString() ?? "" : "";
        var shown = root.TryGetProperty("displayName", out var display) && display.ValueKind == JsonValueKind.String && display.GetString() is { Length: > 0 } d ? d : id;
        // A custom species may carry edits of these fields from before Size existed (animals.db); they still decide its size.
        var edits = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var field in own?.Fields ?? [])
            if ((IsSizePath(field.Path) || field.Path.StartsWith("/animalLengthHeightWidthRaw/", StringComparison.Ordinal))
                && double.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) edits[field.Path] = number;
        double Value(string path, JsonElement element) => edits.TryGetValue(path, out var edited) ? edited : element.TryGetDouble(out var n) ? n : 0;

        // The keys with their tangents, read with Unity's Hermite as the game does (lines between the keys would lose the shape).
        var curve = new List<UnityCurve.Key>();
        if (root.TryGetProperty("scaleGrowthCurve", out var growth) && growth.ValueKind == JsonValueKind.Object
            && growth.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var key in keys.EnumerateArray())
            {
                var at = i;
                float Part(string name) => edits.TryGetValue($"/scaleGrowthCurve/keys/{at}/{name}", out var edited) ? (float)edited
                    : key.ValueKind == JsonValueKind.Object && key.TryGetProperty(name, out var part) ? UnityCurve.Number(part) : 0;
                curve.Add(new UnityCurve.Key(Part("time"), Part("value"), Part("inTangent"), Part("outTangent")));
                i++;
            }
        }
        curve.Sort((x, y) => x.Time.CompareTo(y.Time));
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
        return new OriginalSize(SizeMath.Sample(curve.Select(k => k.Time), t => UnityCurve.Evaluate(curve, t)), skins, size)
        {
            Name = own is { Name.Length: > 0 } ? own.Name : shown,
            BaseName = shown,
        };
    }
}
