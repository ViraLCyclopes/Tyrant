using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>An sRGB colour, 0–1 per channel, written "#rrggbb" in mod.json.</summary>
    public readonly struct Rgb : IEquatable<Rgb>
    {
        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public float R { get; }
        public float G { get; }
        public float B { get; }

        public static bool TryParse(string? text, out Rgb color)
        {
            color = default;
            if (text == null || text.Length != 7 || text[0] != '#') return false;
            if (!int.TryParse(text.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)) return false;
            color = new Rgb(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
            return true;
        }

        public override string ToString() => "#" + Byte(R) + Byte(G) + Byte(B);

        private static string Byte(float c) => ((int)Math.Round(Math.Max(0, Math.Min(1, c)) * 255)).ToString("x2", CultureInfo.InvariantCulture);

        public bool Equals(Rgb other) => ToString() == other.ToString();

        public override bool Equals(object? obj) => obj is Rgb other && Equals(other);

        public override int GetHashCode() => ToString().GetHashCode();
    }

    /// <summary>A number range; a single number in mod.json is a range with Min == Max.</summary>
    public readonly struct FloatRange
    {
        public FloatRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Min { get; }
        public float Max { get; }

        public float Sample(double t) => (float)(Min + (Max - Min) * t);
    }

    /// <summary>Random shift ranges (hue, saturation, value; -1 to 1). The game applies them only where the pattern map's red is above 0.</summary>
    public sealed class SkinTint
    {
        public FloatRange? Hue { get; set; }
        public FloatRange? Saturation { get; set; }
        public FloatRange? Value { get; set; }
    }

    /// <summary>One way of colouring a skin: normal animals' pattern colours, or one mutation. Every part is optional.</summary>
    public sealed class SkinColorSet
    {
        /// <summary>Colour where the pattern map's red is low.</summary>
        public List<Rgb>? A { get; set; }

        /// <summary>Colour where the pattern map's red is high.</summary>
        public List<Rgb>? B { get; set; }

        /// <summary>Colour where the pattern map's green is set.</summary>
        public List<Rgb>? Secondary { get; set; }

        /// <summary>Eye colour (where the extra map's red is above 0.9).</summary>
        public List<Rgb>? Eye { get; set; }

        /// <summary>How much the pattern colour replaces the texture (0–1).</summary>
        public FloatRange? Strength { get; set; }

        /// <summary>How soft the edge between colours A and B is (above 0).</summary>
        public FloatRange? Softness { get; set; }

        /// <summary>Mutations only: their own random shift ranges.</summary>
        public SkinTint? Tint { get; set; }
    }

    /// <summary>A skin's "colors" section in mod.json.</summary>
    public sealed class SkinColors
    {
        public const int MaxStops = 8;

        public SkinTint? Tint { get; set; }

        /// <summary>Pattern colours for normal animals (vanilla only colours patterns for mutations).</summary>
        public SkinColorSet? Pattern { get; set; }

        public SkinColorSet? Albino { get; set; }
        public SkinColorSet? Melanistic { get; set; }
        public SkinColorSet? Leucistic { get; set; }

        public static SkinColors Parse(object? value, string skinId)
        {
            var map = Map(value, skinId, "colors");
            Known(map, skinId, "colors", "tint", "pattern", "albino", "melanistic", "leucistic");
            return new SkinColors
            {
                Tint = map.TryGetValue("tint", out var tint) ? ParseTint(tint, skinId, "colors.tint") : null,
                Pattern = map.TryGetValue("pattern", out var pattern) ? ParseSet(pattern, skinId, "colors.pattern", allowTint: false, needsColour: true) : null,
                Albino = map.TryGetValue("albino", out var albino) ? ParseSet(albino, skinId, "colors.albino", allowTint: true, needsColour: false) : null,
                Melanistic = map.TryGetValue("melanistic", out var melanistic) ? ParseSet(melanistic, skinId, "colors.melanistic", allowTint: true, needsColour: false) : null,
                Leucistic = map.TryGetValue("leucistic", out var leucistic) ? ParseSet(leucistic, skinId, "colors.leucistic", allowTint: true, needsColour: false) : null,
            };
        }

        public Dictionary<string, object?> ToJson()
        {
            var map = new Dictionary<string, object?>();
            if (Tint != null) map["tint"] = TintJson(Tint);
            if (Pattern != null) map["pattern"] = SetJson(Pattern);
            if (Albino != null) map["albino"] = SetJson(Albino);
            if (Melanistic != null) map["melanistic"] = SetJson(Melanistic);
            if (Leucistic != null) map["leucistic"] = SetJson(Leucistic);
            return map;
        }

        private static SkinTint ParseTint(object? value, string skinId, string path)
        {
            var map = Map(value, skinId, path);
            Known(map, skinId, path, "hue", "saturation", "value");
            return new SkinTint
            {
                Hue = Range(map, "hue", skinId, path, -1, 1),
                Saturation = Range(map, "saturation", skinId, path, -1, 1),
                Value = Range(map, "value", skinId, path, -1, 1),
            };
        }

        private static SkinColorSet ParseSet(object? value, string skinId, string path, bool allowTint, bool needsColour)
        {
            var map = Map(value, skinId, path);
            if (!allowTint && map.ContainsKey("tint"))
                throw new ManifestException($"Skin \"{skinId}\": \"{path}\" has no \"tint\"; normal animals' shift ranges go in \"colors.tint\".");
            Known(map, skinId, path, allowTint ? new[] { "a", "b", "secondary", "eye", "strength", "softness", "tint" } : new[] { "a", "b", "secondary", "eye", "strength", "softness" });
            var set = new SkinColorSet
            {
                A = Colours(map, "a", skinId, path),
                B = Colours(map, "b", skinId, path),
                Secondary = Colours(map, "secondary", skinId, path),
                Eye = Colours(map, "eye", skinId, path),
                Strength = Range(map, "strength", skinId, path, 0, 1),
                Softness = Range(map, "softness", skinId, path, 0, float.MaxValue),
                Tint = allowTint && map.TryGetValue("tint", out var tint) ? ParseTint(tint, skinId, path + ".tint") : null,
            };
            if (set.Softness is FloatRange softness && softness.Min <= 0)
                throw new ManifestException($"Skin \"{skinId}\": \"{path}.softness\" must be greater than 0.");
            if (needsColour && set.A == null && set.B == null)
                throw new ManifestException($"Skin \"{skinId}\": \"{path}\" needs \"a\" or \"b\" (the pattern colours).");
            return set;
        }

        private static List<Rgb>? Colours(Dictionary<string, object?> map, string key, string skinId, string path)
        {
            if (!map.TryGetValue(key, out var value) || value == null) return null;
            var items = value is string single ? new List<object?> { single } : value as List<object?>;
            if (items == null || items.Count == 0)
                throw new ManifestException($"Skin \"{skinId}\": \"{path}.{key}\" must be a colour like \"#3060ff\" or a list of them.");
            if (items.Count > MaxStops)
                throw new ManifestException($"Skin \"{skinId}\": \"{path}.{key}\" has {items.Count} colours; use at most {MaxStops}.");
            var colours = new List<Rgb>();
            foreach (var item in items)
            {
                if (!Rgb.TryParse(item as string, out var colour))
                    throw new ManifestException($"Skin \"{skinId}\": \"{path}.{key}\" has \"{item}\", which is not a colour like \"#3060ff\".");
                colours.Add(colour);
            }
            return colours;
        }

        private static FloatRange? Range(Dictionary<string, object?> map, string key, string skinId, string path, float min, float max)
        {
            if (!map.TryGetValue(key, out var value) || value == null) return null;
            float low, high;
            if (value is double d) low = high = (float)d;
            else if (value is List<object?> pair && pair.Count == 2 && pair[0] is double a && pair[1] is double b) (low, high) = ((float)a, (float)b);
            else throw new ManifestException($"Skin \"{skinId}\": \"{path}.{key}\" must be a number or [min, max].");
            if (low > high) throw new ManifestException($"Skin \"{skinId}\": \"{path}.{key}\" has min {low} above max {high}.");
            if (low < min || high > max)
                throw new ManifestException(max == float.MaxValue
                    ? $"Skin \"{skinId}\": \"{path}.{key}\" must be greater than {min}."
                    : $"Skin \"{skinId}\": \"{path}.{key}\" must be from {min} to {max}.");
            return new FloatRange(low, high);
        }

        private static Dictionary<string, object?> Map(object? value, string skinId, string path) =>
            value as Dictionary<string, object?> ?? throw new ManifestException($"Skin \"{skinId}\": \"{path}\" must be an object.");

        private static void Known(Dictionary<string, object?> map, string skinId, string path, params string[] keys)
        {
            foreach (var key in map.Keys)
                if (!keys.Contains(key))
                    throw new ManifestException($"Skin \"{skinId}\" has unknown key \"{path}.{key}\" (use {string.Join(", ", keys)}).");
        }

        private static Dictionary<string, object?> TintJson(SkinTint tint)
        {
            var map = new Dictionary<string, object?>();
            if (tint.Hue is FloatRange hue) map["hue"] = RangeJson(hue);
            if (tint.Saturation is FloatRange saturation) map["saturation"] = RangeJson(saturation);
            if (tint.Value is FloatRange value) map["value"] = RangeJson(value);
            return map;
        }

        private static Dictionary<string, object?> SetJson(SkinColorSet set)
        {
            var map = new Dictionary<string, object?>();
            if (set.A != null) map["a"] = ColoursJson(set.A);
            if (set.B != null) map["b"] = ColoursJson(set.B);
            if (set.Secondary != null) map["secondary"] = ColoursJson(set.Secondary);
            if (set.Eye != null) map["eye"] = ColoursJson(set.Eye);
            if (set.Strength is FloatRange strength) map["strength"] = RangeJson(strength);
            if (set.Softness is FloatRange softness) map["softness"] = RangeJson(softness);
            if (set.Tint != null) map["tint"] = TintJson(set.Tint);
            return map;
        }

        private static List<object?> ColoursJson(List<Rgb> colours) => colours.Select(c => (object?)c.ToString()).ToList();

        private static List<object?> RangeJson(FloatRange range) => new List<object?> { (double)range.Min, (double)range.Max };
    }

    /// <summary>Evenly spaced colour stops, sampled like a Unity gradient (linear between neighbours).</summary>
    public static class ColorRamp
    {
        public static Rgb Sample(IReadOnlyList<Rgb> stops, double t)
        {
            if (stops.Count == 1) return stops[0];
            var x = Math.Max(0, Math.Min(1, t)) * (stops.Count - 1);
            var i = Math.Min((int)Math.Floor(x), stops.Count - 2);
            var f = (float)(x - i);
            var a = stops[i];
            var b = stops[i + 1];
            return new Rgb(a.R + (b.R - a.R) * f, a.G + (b.G - a.G) * f, a.B + (b.B - a.B) * f);
        }
    }
}
