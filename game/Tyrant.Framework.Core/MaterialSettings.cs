using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tyrant.Framework.Core
{
    public enum MaterialValueKind { Number, Color, Vector }

    /// <summary>One animal-shader property a mod may set in "materials.&lt;name&gt;.settings".</summary>
    public sealed class MaterialProperty
    {
        public MaterialProperty(string key, string group, string label, MaterialValueKind kind, float min, float max)
        {
            Key = key;
            Group = group;
            Label = label;
            Kind = kind;
            Min = min;
            Max = max;
        }

        public string Key { get; }
        public string Group { get; }
        public string Label { get; }
        public MaterialValueKind Kind { get; }

        /// <summary>The smallest accepted number (each component of a vector); unused for colours.</summary>
        public float Min { get; }

        public float Max { get; }

        /// <summary>How many numbers a vector holds, 0 for the other kinds; 2 lets 2 to 4 through, 3 means exactly 3.</summary>
        public int Components { get; init; }
    }

    /// <summary>The shader properties Tyrant lets a mod change (types and ranges from the decoded animal shader).</summary>
    public static class MaterialProperties
    {
        public static readonly IReadOnlyList<MaterialProperty> All = new[]
        {
            Number("_Smoothness", "Surface", "Smoothness", 0, 1),
            Number("_Specularity", "Surface", "Specularity", 0, 1),
            Number("_OcclusionStrength", "Surface", "Ambient occlusion", 0, 10),
            Number("_AdultNormalIntensity", "Surface", "Adult normal strength", -10, 10),
            Number("_InfantNormalIntensity", "Surface", "Baby normal strength", -10, 10),
            new MaterialProperty("_DetailScale", "Detail", "Detail tiling", MaterialValueKind.Vector, -100, 100) { Components = 2 },
            Number("_MicroBumpStr", "Detail", "Micro bump strength", 0, 1),
            Number("_MicroBumpMaskedScale", "Detail", "Micro bump (masked) scale", 0, 1),
            Number("_RimIntensity", "Rim / fresnel", "Rim light", 0, 10),
            new MaterialProperty("_FresnelColor", "Rim / fresnel", "Rim colour", MaterialValueKind.Color, 0, 0),
            Number("_FresnelPower", "Rim / fresnel", "Rim falloff", 0, 10),
            Number("_EyeEmission", "Eyes", "Eye glow", 0, 1),
            Number("_EyeSmoothness", "Eyes", "Eye smoothness", 0, 1),
            Number("_EyeSpecularity", "Eyes", "Eye specularity", 0, 1),
            Number("_Length", "Fur", "Fur length", 0, 4),
            Number("_FurWindStrength", "Fur", "Fur wind strength", 0, 10),
            Number("_FurWindSpeed", "Fur", "Fur wind speed", 0, 10),
            Number("_FurWindScale", "Fur", "Fur wind scale", 0, 10),
            Number("_Droopage", "Fur", "Fur droop", 0, 2),
            new MaterialProperty("_DroopDirection", "Fur", "Droop direction", MaterialValueKind.Vector, -1, 1) { Components = 3 },
            Number("_Cutoff", "Cutout", "Cutout threshold", 0, 1),
        };

        public static MaterialProperty? Find(string key) => All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));

        private static MaterialProperty Number(string key, string group, string label, float min, float max) =>
            new MaterialProperty(key, group, label, MaterialValueKind.Number, min, max);
    }

    /// <summary>A shader value: a number, a colour or a 4-component vector.</summary>
    public readonly struct MaterialValue
    {
        private MaterialValue(MaterialValueKind kind, float number, Rgb color, float[]? vector)
        {
            Kind = kind;
            Number = number;
            Color = color;
            _vector = vector;
        }

        private readonly float[]? _vector;

        public MaterialValueKind Kind { get; }
        public float Number { get; }
        public Rgb Color { get; }

        /// <summary>Four components (x, y, z, w).</summary>
        public float[] Vector => _vector ?? new float[4];

        public static MaterialValue OfNumber(float number) => new MaterialValue(MaterialValueKind.Number, number, default, null);

        public static MaterialValue OfColor(Rgb color) => new MaterialValue(MaterialValueKind.Color, 0, color, null);

        /// <summary>At most four components; missing ones are 0.</summary>
        public static MaterialValue OfVector(params float[] components)
        {
            var four = new float[4];
            System.Array.Copy(components, four, Math.Min(4, components.Length));
            return new MaterialValue(MaterialValueKind.Vector, 0, default, four);
        }

        public object ToJson() => Kind switch
        {
            MaterialValueKind.Color => Color.ToString(),
            MaterialValueKind.Vector => Vector.Select(v => (object?)MaterialOverrides.Num(v)).ToList(),
            _ => MaterialOverrides.Num(Number),
        };
    }

    /// <summary>Iridescence strength per skin variant; a variant left out uses <see cref="Normal"/>.</summary>
    public sealed class VariantNumbers
    {
        public float Normal { get; set; }
        public float? Albino { get; set; }
        public float? Melanistic { get; set; }
        public float? Leucistic { get; set; }

        /// <param name="skinType">The game's AnimalSkinType: 0 base, 1 albino, 2 melanistic, 3 leucistic.</param>
        public float For(int skinType) => (skinType == 1 ? Albino : skinType == 2 ? Melanistic : skinType == 3 ? Leucistic : null) ?? Normal;
    }

    /// <summary>A thin-film interference ramp: the colours of a film of this thickness (nm) and refractive index.</summary>
    public sealed class ThinFilmSettings
    {
        public const double DefaultIndex = 1.56;

        public double Thickness { get; set; }
        public double Index { get; set; } = DefaultIndex;
    }

    /// <summary>Where the iridescence colour comes from; exactly one of the three is set.</summary>
    public sealed class RampSource
    {
        public string? File { get; set; }
        public List<Rgb>? Stops { get; set; }
        public ThinFilmSettings? ThinFilm { get; set; }
    }

    /// <summary>Where the iridescence shows: everywhere, a mask file, or one channel of a skin slot.</summary>
    public sealed class MaskSource
    {
        public bool Everywhere { get; set; }
        public string? File { get; set; }

        /// <summary>A skin slot ("extra", "pattern" or "fur"); skins only.</summary>
        public string? From { get; set; }

        public char Channel { get; set; } = 'r';
        public bool Invert { get; set; }
    }

    public sealed class IridescenceSettings
    {
        public const float DefaultVariation = 0.15f;
        public const int MaxRampStops = 32;

        public VariantNumbers? Strength { get; set; }
        public float? Variation { get; set; }
        public RampSource? Ramp { get; set; }
        public MaskSource? Mask { get; set; }
        public float[]? Tiling { get; set; }
        public float[]? Offset { get; set; }
        public float? Scale { get; set; }
        public float? Power { get; set; }
        public float? Bias { get; set; }

        public bool IsEmpty => Strength == null && Variation == null && Ramp == null && Mask == null && Tiling == null && Offset == null
            && Scale == null && Power == null && Bias == null;
    }

    /// <summary>Fur masks a material takes from files (the fur slots of a skin, replaced for one material); only files, never "everywhere" or "from".</summary>
    public sealed class FurMasks
    {
        public MaskSource? Mask { get; set; }
        public MaskSource? InfantMask { get; set; }

        public bool IsEmpty => Mask == null && InfantMask == null;
    }

    /// <summary>One material's changes (materials.json "materials.&lt;name&gt;"): iridescence, shader settings and/or fur masks.</summary>
    public sealed class MaterialOverride
    {
        public IridescenceSettings? Iridescence { get; set; }

        public FurMasks? Fur { get; set; }

        public Dictionary<string, MaterialValue> Settings { get; } = new Dictionary<string, MaterialValue>(StringComparer.Ordinal);

        public bool IsEmpty => Iridescence == null && Fur == null && Settings.Count == 0;
    }

    /// <summary>The "materials" map of a materials.json (a skin's, a species' or a vanilla skin's).</summary>
    public static class MaterialOverrides
    {
        private static readonly string[] StrengthKeys = { "normal", "albino", "melanistic", "leucistic" };
        private static readonly string[] MaskSlots = { "extra", "pattern", "fur" };

        /// <summary>Null when nothing is set. <paramref name="owner"/> starts every message; <paramref name="skinLevel"/> allows mask.from.</summary>
        public static Dictionary<string, MaterialOverride>? Parse(object? value, string owner, bool skinLevel)
        {
            var map = Map(value, owner, "materials");
            var result = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                var path = "materials." + pair.Key;
                var material = ParseMaterial(pair.Value, owner, path, skinLevel);
                if (!material.IsEmpty) result[pair.Key] = material;
            }
            return result.Count == 0 ? null : result;
        }

        public static Dictionary<string, object?> ToJson(IReadOnlyDictionary<string, MaterialOverride> materials)
        {
            var map = new Dictionary<string, object?>();
            foreach (var pair in materials)
            {
                if (pair.Value.IsEmpty) continue;
                var entry = new Dictionary<string, object?>();
                if (pair.Value.Iridescence != null) entry["iridescence"] = IridescenceJson(pair.Value.Iridescence);
                if (pair.Value.Fur != null)
                {
                    var fur = new Dictionary<string, object?>();
                    if (pair.Value.Fur.Mask?.File != null) fur["mask"] = pair.Value.Fur.Mask.File;
                    if (pair.Value.Fur.InfantMask?.File != null) fur["infantMask"] = pair.Value.Fur.InfantMask.File;
                    entry["fur"] = fur;
                }
                if (pair.Value.Settings.Count > 0)
                    entry["settings"] = pair.Value.Settings.ToDictionary(s => s.Key, s => (object?)s.Value.ToJson(), StringComparer.Ordinal);
                map[pair.Key] = entry;
            }
            return map;
        }

        internal static double Num(float value) => double.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        private static MaterialOverride ParseMaterial(object? value, string owner, string path, bool skinLevel)
        {
            var map = Map(value, owner, path);
            Known(map, owner, path, "iridescence", "settings", "fur");
            var material = new MaterialOverride();
            if (map.TryGetValue("iridescence", out var iridescence) && iridescence != null)
            {
                var parsed = ParseIridescence(iridescence, owner, path + ".iridescence", skinLevel);
                if (!parsed.IsEmpty) material.Iridescence = parsed;
            }
            if (map.TryGetValue("settings", out var settings) && settings != null)
                ParseSettings(settings, owner, path + ".settings", material.Settings);
            if (map.TryGetValue("fur", out var fur) && fur != null)
            {
                var parsed = ParseFur(fur, owner, path + ".fur");
                if (!parsed.IsEmpty) material.Fur = parsed;
            }
            return material;
        }

        private static FurMasks ParseFur(object value, string owner, string path)
        {
            var map = Map(value, owner, path);
            Known(map, owner, path, "mask", "infantMask");
            MaskSource? File(string key) =>
                !map.TryGetValue(key, out var v) || v == null ? null
                : v is string file && file.Length > 0 && file != "everywhere" ? new MaskSource { File = file }
                : throw new ManifestException($"{owner}: \"{path}.{key}\" must be an image file path.");
            return new FurMasks { Mask = File("mask"), InfantMask = File("infantMask") };
        }

        private static void ParseSettings(object value, string owner, string path, Dictionary<string, MaterialValue> into)
        {
            foreach (var pair in Map(value, owner, path))
            {
                var property = MaterialProperties.Find(pair.Key)
                    ?? throw new ManifestException($"{owner}: \"{pair.Key}\" is not a setting Tyrant can change (see docs/mod-format.md, Skin materials).");
                var at = path + "." + pair.Key;
                switch (property.Kind)
                {
                    case MaterialValueKind.Color:
                        if (!(pair.Value is string text) || !Rgb.TryParse(text, out var colour))
                            throw new ManifestException($"{owner}: \"{at}\" must be a colour like \"#3060ff\".");
                        into[pair.Key] = MaterialValue.OfColor(colour);
                        break;
                    case MaterialValueKind.Vector:
                        into[pair.Key] = MaterialValue.OfVector(Components(pair.Value, owner, at, property.Min, property.Max, property.Components));
                        break;
                    default:
                        into[pair.Key] = MaterialValue.OfNumber(Number(pair.Value, owner, at, property.Min, property.Max));
                        break;
                }
            }
        }

        private static IridescenceSettings ParseIridescence(object value, string owner, string path, bool skinLevel)
        {
            var map = Map(value, owner, path);
            Known(map, owner, path, "strength", "variation", "ramp", "mask", "tiling", "offset", "scale", "power", "bias");
            return new IridescenceSettings
            {
                Strength = map.TryGetValue("strength", out var strength) && strength != null ? ParseStrength(strength, owner, path + ".strength") : null,
                Variation = Optional(map, "variation", owner, path, 0, 1),
                Ramp = map.TryGetValue("ramp", out var ramp) && ramp != null ? ParseRamp(ramp, owner, path + ".ramp") : null,
                Mask = map.TryGetValue("mask", out var mask) && mask != null ? ParseMask(mask, owner, path + ".mask", skinLevel) : null,
                Tiling = Pair(map, "tiling", owner, path),
                Offset = Pair(map, "offset", owner, path),
                Scale = Optional(map, "scale", owner, path, -10, 10),
                Power = Optional(map, "power", owner, path, -10, 10),
                Bias = Optional(map, "bias", owner, path, -10, 10),
            };
        }

        private static VariantNumbers ParseStrength(object value, string owner, string path)
        {
            if (!(value is Dictionary<string, object?> map)) return new VariantNumbers { Normal = Number(value, owner, path, 0, 1) };
            Known(map, owner, path, StrengthKeys);
            if (!map.ContainsKey("normal")) throw new ManifestException($"{owner}: \"{path}\" needs \"normal\" (the strength of normal animals, used by any variant left out).");
            return new VariantNumbers
            {
                Normal = Optional(map, "normal", owner, path, 0, 1) ?? 0,
                Albino = Optional(map, "albino", owner, path, 0, 1),
                Melanistic = Optional(map, "melanistic", owner, path, 0, 1),
                Leucistic = Optional(map, "leucistic", owner, path, 0, 1),
            };
        }

        private static RampSource ParseRamp(object value, string owner, string path)
        {
            if (value is string file)
            {
                if (file.Length == 0) throw new ManifestException($"{owner}: \"{path}\" must be a file path, or an object with \"stops\" or \"thinFilm\".");
                return new RampSource { File = file };
            }
            var map = Map(value, owner, path);
            Known(map, owner, path, "stops", "thinFilm");
            var hasStops = map.TryGetValue("stops", out var stops) && stops != null;
            var hasFilm = map.TryGetValue("thinFilm", out var film) && film != null;
            if (hasStops == hasFilm) throw new ManifestException($"{owner}: \"{path}\" needs exactly one of \"stops\" or \"thinFilm\".");
            if (hasStops)
            {
                var items = stops as List<object?>;
                if (items == null || items.Count < 1 || items.Count > IridescenceSettings.MaxRampStops)
                    throw new ManifestException($"{owner}: \"{path}.stops\" must list 1 to {IridescenceSettings.MaxRampStops} colours like \"#3060ff\".");
                var colours = new List<Rgb>();
                foreach (var item in items)
                {
                    if (!Rgb.TryParse(item as string, out var colour))
                        throw new ManifestException($"{owner}: \"{path}.stops\" has \"{item}\", which is not a colour like \"#3060ff\".");
                    colours.Add(colour);
                }
                return new RampSource { Stops = colours };
            }
            var filmPath = path + ".thinFilm";
            var filmMap = Map(film, owner, filmPath);
            Known(filmMap, owner, filmPath, "thickness", "index");
            if (!filmMap.ContainsKey("thickness")) throw new ManifestException($"{owner}: \"{filmPath}\" needs \"thickness\" (nanometres).");
            return new RampSource
            {
                ThinFilm = new ThinFilmSettings
                {
                    Thickness = Number(filmMap["thickness"], owner, filmPath + ".thickness", 0, 2000),
                    Index = filmMap.TryGetValue("index", out var index) && index != null ? Number(index, owner, filmPath + ".index", 1, 3) : ThinFilmSettings.DefaultIndex,
                },
            };
        }

        private static MaskSource ParseMask(object value, string owner, string path, bool skinLevel)
        {
            if (value is string text)
            {
                if (text == "everywhere") return new MaskSource { Everywhere = true };
                if (text.Length == 0) throw new ManifestException($"{owner}: \"{path}\" must be \"everywhere\", a file path, or an object with \"from\".");
                return new MaskSource { File = text };
            }
            var map = Map(value, owner, path);
            Known(map, owner, path, "from", "channel", "invert");
            if (!map.TryGetValue("from", out var from) || from == null) throw new ManifestException($"{owner}: \"{path}\" needs \"from\" (extra, pattern or fur).");
            if (!skinLevel) throw new ManifestException($"{owner}: \"{path}.from\" works only on a skin (a species has no mask from a slot).");
            if (!(from is string slot) || !MaskSlots.Contains(slot)) throw new ManifestException($"{owner}: \"{path}.from\" must be extra, pattern or fur.");
            var channel = 'r';
            if (map.TryGetValue("channel", out var c) && c != null)
            {
                if (!(c is string ch) || ch.Length != 1 || "rgba".IndexOf(char.ToLowerInvariant(ch[0])) < 0)
                    throw new ManifestException($"{owner}: \"{path}.channel\" must be r, g, b or a.");
                channel = char.ToLowerInvariant(ch[0]);
            }
            var invert = false;
            if (map.TryGetValue("invert", out var inv) && inv != null)
                invert = inv as bool? ?? throw new ManifestException($"{owner}: \"{path}.invert\" must be true or false.");
            return new MaskSource { From = slot, Channel = channel, Invert = invert };
        }

        private static float Number(object? value, string owner, string path, float min, float max)
        {
            if (!(value is double d)) throw new ManifestException($"{owner}: \"{path}\" must be a number from {Format(min)} to {Format(max)}.");
            return Checked((float)d, owner, path, min, max);
        }

        private static float Checked(float value, string owner, string path, float min, float max)
        {
            if (value < min || value > max) throw new ManifestException($"{owner}: \"{path}\" must be from {Format(min)} to {Format(max)} (got {Format(value)}).");
            return value;
        }

        private static float? Optional(Dictionary<string, object?> map, string key, string owner, string path, float min, float max) =>
            map.TryGetValue(key, out var value) && value != null ? Number(value, owner, path + "." + key, min, max) : (float?)null;

        private static float[] Components(object? value, string owner, string path, float min, float max, int count)
        {
            var exact = count == 3;
            if (!(value is List<object?> items) || (exact ? items.Count != 3 : items.Count < 2 || items.Count > 4) || items.Any(i => !(i is double)))
                throw new ManifestException($"{owner}: \"{path}\" must be a list of {(exact ? "3" : "2 to 4")} numbers.");
            return items.Select(i => Checked((float)(double)i!, owner, path, min, max)).ToArray();
        }

        private static float[]? Pair(Dictionary<string, object?> map, string key, string owner, string path)
        {
            if (!map.TryGetValue(key, out var value) || value == null) return null;
            if (!(value is List<object?> items) || items.Count != 2 || items.Any(i => !(i is double)))
                throw new ManifestException($"{owner}: \"{path}.{key}\" must be a list of 2 numbers.");
            return items.Select(i => (float)(double)i!).ToArray();
        }

        private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static Dictionary<string, object?> Map(object? value, string owner, string path) =>
            value as Dictionary<string, object?> ?? throw new ManifestException($"{owner}: \"{path}\" must be an object.");

        private static void Known(Dictionary<string, object?> map, string owner, string path, params string[] keys)
        {
            foreach (var key in map.Keys)
                if (!keys.Contains(key))
                    throw new ManifestException($"{owner} has unknown key \"{path}.{key}\" (use {string.Join(", ", keys)}).");
        }

        private static Dictionary<string, object?> IridescenceJson(IridescenceSettings i)
        {
            var map = new Dictionary<string, object?>();
            if (i.Strength is VariantNumbers s)
            {
                if (s.Albino == null && s.Melanistic == null && s.Leucistic == null) map["strength"] = Num(s.Normal);
                else
                {
                    var strength = new Dictionary<string, object?> { ["normal"] = Num(s.Normal) };
                    if (s.Albino is float a) strength["albino"] = Num(a);
                    if (s.Melanistic is float m) strength["melanistic"] = Num(m);
                    if (s.Leucistic is float l) strength["leucistic"] = Num(l);
                    map["strength"] = strength;
                }
            }
            if (i.Variation is float variation) map["variation"] = Num(variation);
            if (i.Ramp is RampSource ramp)
            {
                if (ramp.File != null) map["ramp"] = ramp.File;
                else if (ramp.Stops != null) map["ramp"] = new Dictionary<string, object?> { ["stops"] = ramp.Stops.Select(c => (object?)c.ToString()).ToList() };
                else if (ramp.ThinFilm != null)
                {
                    var film = new Dictionary<string, object?> { ["thickness"] = ramp.ThinFilm.Thickness };
                    if (ramp.ThinFilm.Index != ThinFilmSettings.DefaultIndex) film["index"] = ramp.ThinFilm.Index;
                    map["ramp"] = new Dictionary<string, object?> { ["thinFilm"] = film };
                }
            }
            if (i.Mask is MaskSource mask)
            {
                if (mask.Everywhere) map["mask"] = "everywhere";
                else if (mask.File != null) map["mask"] = mask.File;
                else if (mask.From != null)
                {
                    var from = new Dictionary<string, object?> { ["from"] = mask.From };
                    if (mask.Channel != 'r') from["channel"] = mask.Channel.ToString();
                    if (mask.Invert) from["invert"] = true;
                    map["mask"] = from;
                }
            }
            if (i.Tiling != null) map["tiling"] = i.Tiling.Select(v => (object?)Num(v)).ToList();
            if (i.Offset != null) map["offset"] = i.Offset.Select(v => (object?)Num(v)).ToList();
            if (i.Scale is float scale) map["scale"] = Num(scale);
            if (i.Power is float power) map["power"] = Num(power);
            if (i.Bias is float bias) map["bias"] = Num(bias);
            return map;
        }
    }
}
