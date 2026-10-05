using System.Text.Json;

namespace Tyrant.Core.Blender;

/// <summary>
/// Unity's AnimationCurve as the data dump writes it (keys with time, value, in/out tangents), evaluated with Unity's
/// cubic Hermite between keys, clamped outside them (ClampForever, the dumped growth curves' mode). Weighted tangents
/// are treated as unweighted (the growth curves use weightedMode None). An infinite tangent holds the left key (a step).
/// </summary>
public static class UnityCurve
{
    private readonly record struct Key(float Time, float Value, float In, float Out);

    public static float Evaluate(JsonElement curve, float t)
    {
        var keys = Keys(curve);
        if (keys.Count == 0) return t;
        if (t <= keys[0].Time) return keys[0].Value;
        if (t >= keys[^1].Time) return keys[^1].Value;
        var i = 0;
        while (t > keys[i + 1].Time) i++;
        var (a, b) = (keys[i], keys[i + 1]);
        if (float.IsInfinity(a.Out) || float.IsInfinity(b.In)) return a.Value;
        var dt = b.Time - a.Time;
        var s = (t - a.Time) / dt;
        var s2 = s * s;
        var s3 = s2 * s;
        return (2 * s3 - 3 * s2 + 1) * a.Value + (s3 - 2 * s2 + s) * dt * a.Out + (-2 * s3 + 3 * s2) * b.Value + (s3 - s2) * dt * b.In;
    }

    /// <summary>count values over 0–1; without a curve, the straight line (maturity as it is).</summary>
    public static float[] Sample(JsonElement? curve, int count = 101)
    {
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var t = i / (float)(count - 1);
            samples[i] = curve is { } c ? Evaluate(c, t) : t;
        }
        return samples;
    }

    private static List<Key> Keys(JsonElement curve)
    {
        var keys = new List<Key>();
        if (curve.ValueKind != JsonValueKind.Object || !curve.TryGetProperty("keys", out var list) || list.ValueKind != JsonValueKind.Array) return keys;
        foreach (var k in list.EnumerateArray())
            keys.Add(new Key(Num(k, "time"), Num(k, "value"), Num(k, "inTangent"), Num(k, "outTangent")));
        keys.Sort((x, y) => x.Time.CompareTo(y.Time));
        return keys;
    }

    /// <summary>A number, or the dump's "Infinity"/"-Infinity"/"NaN" strings.</summary>
    private static float Num(JsonElement key, string name)
    {
        if (!key.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetSingle();
        return v.ValueKind == JsonValueKind.String ? v.GetString() switch
        {
            "Infinity" => float.PositiveInfinity,
            "-Infinity" => float.NegativeInfinity,
            _ => 0,
        } : 0;
    }
}
