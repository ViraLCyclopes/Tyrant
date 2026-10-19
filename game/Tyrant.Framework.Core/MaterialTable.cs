using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>An enabled mod whose species material settings a later mod in the load order overrides.</summary>
    public sealed class MaterialClash
    {
        public MaterialClash(string species, string material, string loser, string winner)
        {
            Species = species;
            Material = material;
            Loser = loser;
            Winner = winner;
        }

        public string Species { get; }
        public string Material { get; }
        public string Loser { get; }
        public string Winner { get; }
        public string Message => $"{Species}: its {Material} material settings are overridden by '{Winner}' (later in the load order).";
    }

    /// <summary>
    /// The material changes an animal wears: its skin's, else its base skin's, else its species' (per field; the species' entry
    /// for a material is the later mod's whole entry). Files are full paths inside the mod that names them.
    /// </summary>
    public sealed class MaterialTable
    {
        private readonly Dictionary<string, Dictionary<string, MaterialOverride>> _species = new Dictionary<string, Dictionary<string, MaterialOverride>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, MaterialOverride>> _skins = new Dictionary<string, Dictionary<string, MaterialOverride>>(StringComparer.Ordinal);

        public bool HasAny => _species.Count > 0 || _skins.Count > 0;

        /// <param name="messages">Overridden species materials and files outside their mod, for the log.</param>
        public static MaterialTable Build(IReadOnlyList<LoadedMod> mods, out List<string> messages)
        {
            var table = new MaterialTable();
            messages = new List<string>();
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var mod in mods)
            {
                var id = mod.Manifest.Id;
                foreach (var model in mod.Manifest.Models)
                {
                    if (model.Materials == null) continue;
                    var copies = Copy(model.Materials, mod, messages);
                    if (copies.Count == 0) continue;
                    if (!table._species.TryGetValue(model.Target, out var materials))
                        table._species[model.Target] = materials = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
                    foreach (var pair in copies)
                    {
                        var key = model.Target + "\n" + pair.Key;
                        if (owner.TryGetValue(key, out var earlier) && earlier != id)
                            messages.Add($"{model.Target}: '{id}' sets its {pair.Key} material settings, overriding '{earlier}' (the later mod in the load order wins).");
                        owner[key] = id;
                        materials[pair.Key] = pair.Value;
                    }
                }
                foreach (var skin in mod.Manifest.Skins)
                {
                    if (skin.Materials == null) continue;
                    var copies = Copy(skin.Materials, mod, messages);
                    if (copies.Count > 0) table._skins[skin.Key(id)] = copies;
                }
            }
            return table;
        }

        /// <summary>Enabled mods (in load order) whose species material settings a later one overrides, for the Mods tab.</summary>
        public static IReadOnlyList<MaterialClash> Clashes(IReadOnlyList<(string ModId, ModManifest Manifest)> enabledInLoadOrder)
        {
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            var clashes = new List<MaterialClash>();
            foreach (var (id, manifest) in enabledInLoadOrder)
                foreach (var model in manifest.Models.Where(m => m.Materials != null))
                    foreach (var material in model.Materials!.Where(m => !m.Value.IsEmpty).Select(m => m.Key).Distinct(StringComparer.Ordinal))
                    {
                        var key = model.Target + "\n" + material;
                        if (owner.TryGetValue(key, out var earlier) && earlier != id) clashes.Add(new MaterialClash(model.Target, material, earlier, id));
                        owner[key] = id;
                    }
            return clashes;
        }

        /// <summary>The changes to one material: skin over base skin over species, field by field; null when none apply.</summary>
        public MaterialOverride? For(string? speciesId, string? skinKey, string? inheritedKey, string materialName) =>
            Merge(Find(_skins, skinKey, materialName), Find(_skins, inheritedKey, materialName), Find(_species, speciesId, materialName));

        /// <summary>
        /// One override from several, highest priority first: each iridescence field comes from the first that sets it, and each
        /// setting from the first that has it. The inputs are not changed; null when nothing is set.
        /// </summary>
        public static MaterialOverride? Merge(params MaterialOverride?[] highestFirst)
        {
            var result = new MaterialOverride();
            IridescenceSettings? iridescence = null;
            foreach (var layer in highestFirst)
            {
                if (layer == null) continue;
                foreach (var setting in layer.Settings)
                    if (!result.Settings.ContainsKey(setting.Key)) result.Settings[setting.Key] = setting.Value;
                var i = layer.Iridescence;
                if (i == null) continue;
                iridescence ??= new IridescenceSettings();
                iridescence.Strength ??= i.Strength;
                iridescence.Variation ??= i.Variation;
                iridescence.Ramp ??= i.Ramp;
                iridescence.Mask ??= i.Mask;
                iridescence.Tiling ??= i.Tiling;
                iridescence.Offset ??= i.Offset;
                iridescence.Scale ??= i.Scale;
                iridescence.Power ??= i.Power;
                iridescence.Bias ??= i.Bias;
            }
            result.Iridescence = iridescence;
            return result.IsEmpty ? null : result;
        }

        /// <summary>The variation of the strength between animals: as set, else a little when a strength is set, else none.</summary>
        public static float EffectiveVariation(IridescenceSettings i) =>
            i.Variation ?? (i.Strength != null ? IridescenceSettings.DefaultVariation : 0);

        private static MaterialOverride? Find(Dictionary<string, Dictionary<string, MaterialOverride>> map, string? key, string material) =>
            key != null && map.TryGetValue(key, out var materials) && materials.TryGetValue(material, out var found) ? found : null;

        /// <summary>Copies with file paths made absolute; a path outside its mod is dropped with a message. The manifest is not changed.</summary>
        private static Dictionary<string, MaterialOverride> Copy(Dictionary<string, MaterialOverride> source, LoadedMod mod, List<string> messages)
        {
            var copies = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
            foreach (var pair in source)
            {
                var copy = new MaterialOverride();
                foreach (var setting in pair.Value.Settings) copy.Settings[setting.Key] = setting.Value;
                if (pair.Value.Iridescence is IridescenceSettings i)
                {
                    var iridescence = new IridescenceSettings
                    {
                        Strength = i.Strength == null ? null : new VariantNumbers { Normal = i.Strength.Normal, Albino = i.Strength.Albino, Melanistic = i.Strength.Melanistic, Leucistic = i.Strength.Leucistic },
                        Variation = i.Variation,
                        Ramp = CopyRamp(i.Ramp, mod, messages),
                        Mask = CopyMask(i.Mask, mod, messages),
                        Tiling = (float[]?)i.Tiling?.Clone(),
                        Offset = (float[]?)i.Offset?.Clone(),
                        Scale = i.Scale,
                        Power = i.Power,
                        Bias = i.Bias,
                    };
                    if (!iridescence.IsEmpty) copy.Iridescence = iridescence;
                }
                if (!copy.IsEmpty) copies[pair.Key] = copy;
            }
            return copies;
        }

        private static RampSource? CopyRamp(RampSource? ramp, LoadedMod mod, List<string> messages)
        {
            if (ramp == null) return null;
            if (ramp.File == null)
                return new RampSource
                {
                    Stops = ramp.Stops == null ? null : new List<Rgb>(ramp.Stops),
                    ThinFilm = ramp.ThinFilm == null ? null : new ThinFilmSettings { Thickness = ramp.ThinFilm.Thickness, Index = ramp.ThinFilm.Index },
                };
            var path = Resolve(ramp.File, mod, messages);
            return path == null ? null : new RampSource { File = path };
        }

        private static MaskSource? CopyMask(MaskSource? mask, LoadedMod mod, List<string> messages)
        {
            if (mask == null) return null;
            var path = mask.File == null ? null : Resolve(mask.File, mod, messages);
            if (mask.File != null && path == null) return null;
            return new MaskSource { Everywhere = mask.Everywhere, File = path, From = mask.From, Channel = mask.Channel, Invert = mask.Invert };
        }

        private static string? Resolve(string file, LoadedMod mod, List<string> messages)
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(mod.Directory, file));
                if (ModPaths.IsInside(path, mod.Directory)) return path;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
            }
            messages.Add($"{mod.Manifest.Id}: {file} points outside the mod folder; it is ignored.");
            return null;
        }
    }
}
