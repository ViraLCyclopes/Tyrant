using System.Text.Json;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>
/// The colours of a vanilla skin's mutations (albino, melanistic, leucistic), read from its variation asset in the data dump, in
/// mod.json's terms. A skin's own mutation colours replace only the fields they set (SkinColorsModule); the rest stay the base
/// skin's, so previews fill the gaps the same way.
/// </summary>
public static class MorphColors
{
    public static readonly string[] Variants = ["albino", "melanistic", "leucistic"];

    /// <summary>The base skin's colours for a mutation; infants use the male asset (where their textures are). Null for "normal".</summary>
    public static SkinColorSet? Of(VanillaSkin? based, string sex, string variant) =>
        based is not null && (sex == "female" ? based.FemaleMorphs : based.MaleMorphs).TryGetValue(variant, out var set) ? set : null;

    /// <summary>Each field (and tint part) the mod sets, else the base skin's.</summary>
    public static SkinColorSet? Fill(SkinColorSet? own, SkinColorSet? based)
    {
        if (own is null || based is null) return own ?? based;
        return new SkinColorSet
        {
            A = own.A ?? based.A,
            B = own.B ?? based.B,
            Secondary = own.Secondary ?? based.Secondary,
            Eye = own.Eye ?? based.Eye,
            Strength = own.Strength ?? based.Strength,
            Softness = own.Softness ?? based.Softness,
            Tint = Fill(own.Tint, based.Tint),
        };
    }

    private static SkinTint? Fill(SkinTint? own, SkinTint? based) =>
        own is null || based is null ? own ?? based
        : new SkinTint { Hue = own.Hue ?? based.Hue, Saturation = own.Saturation ?? based.Saturation, Value = own.Value ?? based.Value };

    /// <summary>Mutation name to its colours, from a variation asset's albino/melanistic/leucisticVariationData.</summary>
    internal static IReadOnlyDictionary<string, SkinColorSet> Read(JsonElement variation)
    {
        var result = new Dictionary<string, SkinColorSet>(StringComparer.Ordinal);
        foreach (var variant in Variants)
            if (variation.TryGetProperty(variant + "VariationData", out var data) && data.ValueKind == JsonValueKind.Object)
                result[variant] = ReadSet(data);
        return result;
    }

    private static SkinColorSet ReadSet(JsonElement data)
    {
        var hue = Range(data, "minMaxHue");
        var saturation = Range(data, "minMaxSat");
        var value = Range(data, "minMaxVal");
        return new SkinColorSet
        {
            A = Gradient(data, "patternAGradient"),
            B = Gradient(data, "patternBGradient"),
            Secondary = Gradient(data, "secondaryGradient"),
            Eye = Gradient(data, "eyeGradient"),
            Strength = Range(data, "minMaxOverrideBase"),
            Softness = Range(data, "minMaxPatternFeather"),
            Tint = hue is null && saturation is null && value is null ? null : new SkinTint { Hue = hue, Saturation = saturation, Value = value },
        };
    }

    private static List<Rgb>? Gradient(JsonElement data, string field) =>
        data.TryGetProperty(field, out var gradient) && gradient.ValueKind == JsonValueKind.Object ? Ramp(gradient) : null;

    private static FloatRange? Range(JsonElement data, string field) =>
        data.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Object
        && v.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.Number && v.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number
        && x.TryGetSingle(out var min) && y.TryGetSingle(out var max)
            ? new FloatRange(Math.Min(min, max), Math.Max(min, max))
            : null;

    /// <summary>
    /// A Unity gradient as mod.json colour stops (evenly spaced): its keys when they already are, else the gradient sampled at
    /// <see cref="SkinColors.MaxStops"/> even steps (Blend mixes neighbouring keys; Fixed takes the key at or after each time).
    /// </summary>
    internal static List<Rgb>? Ramp(JsonElement gradient)
    {
        if (!gradient.TryGetProperty("colorKeys", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        var keys = new List<(float Time, Rgb Colour)>();
        foreach (var key in list.EnumerateArray().Where(k => k.ValueKind == JsonValueKind.Object))
            keys.Add((Number(key, "time"), new Rgb(Number(key, "r"), Number(key, "g"), Number(key, "b"))));
        if (keys.Count == 0) return null;
        keys = keys.OrderBy(k => k.Time).ToList();
        if (keys.Count == 1) return [keys[0].Colour];
        var even = keys.Count <= SkinColors.MaxStops && keys.Select((k, i) => Math.Abs(k.Time - i / (float)(keys.Count - 1))).All(d => d < 0.001f);
        if (even) return keys.Select(k => k.Colour).ToList();
        var isFixed = gradient.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String && mode.GetString() == "Fixed";
        return Enumerable.Range(0, SkinColors.MaxStops).Select(i => Evaluate(keys, i / (float)(SkinColors.MaxStops - 1), isFixed)).ToList();
    }

    private static Rgb Evaluate(List<(float Time, Rgb Colour)> keys, float t, bool isFixed)
    {
        var after = keys.FindIndex(k => k.Time >= t);
        if (after < 0) return keys[^1].Colour;
        if (after == 0 || isFixed) return keys[after].Colour;
        var (t0, c0) = keys[after - 1];
        var (t1, c1) = keys[after];
        var f = t1 > t0 ? (t - t0) / (t1 - t0) : 1f;
        return new Rgb(c0.R + (c1.R - c0.R) * f, c0.G + (c1.G - c0.G) * f, c0.B + (c1.B - c0.B) * f);
    }

    private static float Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f) ? f : 0f;
}
