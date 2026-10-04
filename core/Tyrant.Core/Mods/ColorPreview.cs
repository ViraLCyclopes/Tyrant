using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>A skin's maps at preview size: RGBA bytes, all the same size. Pattern and extra may be missing.</summary>
public sealed record PreviewMaps(int Width, int Height, byte[] Diffuse, byte[]? Pattern, byte[]? Extra);

/// <summary>
/// An approximate 2D picture of one animal with a skin's colours, following the decoded animal shader (no lighting, normal
/// or AO): pattern red 0 keeps the texture; higher red blends colour A towards B (softened by softness); strength sets how
/// much the colour replaces the texture; pattern green takes the secondary colour; extra red above 0.9 is the eyes; the
/// tint (hue, saturation, value) shifts colours only where pattern red is above 0. Unset fields: no A/B colour means no
/// recolouring, no tint means exact colours, strength 1, softness 0.25.
/// </summary>
public static class ColorPreview
{
    private const float EyeThreshold = 0.9f;

    public static (SkinColorSet? Set, SkinTint? Tint) For(SkinColors? colors, string variant) => variant switch
    {
        "albino" => (colors?.Albino, colors?.Albino?.Tint),
        "melanistic" => (colors?.Melanistic, colors?.Melanistic?.Tint),
        "leucistic" => (colors?.Leucistic, colors?.Leucistic?.Tint),
        _ => (colors?.Pattern, colors?.Tint),
    };

    public static byte[] Render(PreviewMaps maps, SkinColorSet? set, SkinTint? tint, Random random)
    {
        var a = Pick(set?.A, random);
        var b = Pick(set?.B, random) ?? a;
        a ??= b;
        var secondary = Pick(set?.Secondary, random);
        var eye = Pick(set?.Eye, random);
        var strength = set?.Strength?.Sample(random.NextDouble()) ?? 1f;
        var softness = Math.Max(0.01f, set?.Softness?.Sample(random.NextDouble()) ?? 0.25f);
        var hue = tint?.Hue?.Sample(random.NextDouble()) ?? 0f;
        var saturation = tint?.Saturation?.Sample(random.NextDouble()) ?? 0f;
        var value = tint?.Value?.Sample(random.NextDouble()) ?? 0f;

        var output = (byte[])maps.Diffuse.Clone();
        for (var i = 0; i < maps.Width * maps.Height; i++)
        {
            var o = i * 4;
            var red = maps.Pattern is null ? 0f : maps.Pattern[o] / 255f;
            var green = maps.Pattern is null ? 0f : maps.Pattern[o + 1] / 255f;
            var isEye = maps.Extra is not null && maps.Extra[o] / 255f > EyeThreshold;
            if (red <= 0 && !isEye) continue;
            var pixel = (R: output[o] / 255f, G: output[o + 1] / 255f, B: output[o + 2] / 255f);
            if (red > 0)
            {
                pixel = Shift(pixel, hue, saturation, value);
                if (a is { } ca && b is { } cb)
                {
                    var t = Math.Clamp(red / softness, 0f, 1f);
                    pixel = Lerp(pixel, Lerp((ca.R, ca.G, ca.B), (cb.R, cb.G, cb.B), t), strength);
                }
                if (secondary is { } s && green > 0) pixel = Lerp(pixel, (s.R, s.G, s.B), green * strength);
            }
            if (isEye && eye is { } e) pixel = Lerp(pixel, (e.R, e.G, e.B), strength);
            output[o] = ToByte(pixel.R);
            output[o + 1] = ToByte(pixel.G);
            output[o + 2] = ToByte(pixel.B);
        }
        return output;
    }

    private static Rgb? Pick(IReadOnlyList<Rgb>? stops, Random random) => stops is { Count: > 0 } ? ColorRamp.Sample(stops, random.NextDouble()) : null;

    private static (float R, float G, float B) Lerp((float R, float G, float B) x, (float R, float G, float B) y, float t) =>
        (x.R + (y.R - x.R) * t, x.G + (y.G - x.G) * t, x.B + (y.B - x.B) * t);

    private static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255f), 0, 255);

    /// <summary>Hue turns by <paramref name="hue"/> of a full circle; saturation and value move by the given amount.</summary>
    private static (float R, float G, float B) Shift((float R, float G, float B) c, float hue, float saturation, float value)
    {
        if (hue == 0 && saturation == 0 && value == 0) return c;
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        var delta = max - min;
        var h = delta == 0 ? 0 : max == c.R ? ((c.G - c.B) / delta % 6 + 6) % 6 : max == c.G ? (c.B - c.R) / delta + 2 : (c.R - c.G) / delta + 4;
        h = ((h / 6f + hue) % 1f + 1f) % 1f * 6f;
        var s = Math.Clamp((max == 0 ? 0 : delta / max) + saturation, 0f, 1f);
        var v = Math.Clamp(max + value, 0f, 1f);
        var chroma = v * s;
        var x = chroma * (1 - Math.Abs(h % 2 - 1));
        var m = v - chroma;
        var (r, g, b) = h switch
        {
            < 1 => (chroma, x, 0f),
            < 2 => (x, chroma, 0f),
            < 3 => (0f, chroma, x),
            < 4 => (0f, x, chroma),
            < 5 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };
        return (r + m, g + m, b + m);
    }
}
