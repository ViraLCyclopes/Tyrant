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

    /// <summary>The mod whose block for a material currently wins, and its folder.</summary>
    public sealed record MaterialBlockOwner(string ModId, string ModDirectory);

    /// <summary>
    /// The material changes an animal wears: its skin's (a mod skin's or a vanilla skin's), else its base skin's, else its
    /// species' (per field; a species' or vanilla skin's entry for a material is the later mod's whole entry). Files are full
    /// paths inside the mod that names them.
    /// </summary>
    public sealed class MaterialTable
    {
        private readonly Dictionary<string, Dictionary<string, MaterialOverride>> _species = new Dictionary<string, Dictionary<string, MaterialOverride>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, MaterialOverride>> _skins = new Dictionary<string, Dictionary<string, MaterialOverride>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, MaterialOverride>> _vanilla = new Dictionary<string, Dictionary<string, MaterialOverride>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MaterialBlockOwner> _owners = new Dictionary<string, MaterialBlockOwner>(StringComparer.Ordinal);
        private readonly HashSet<(string ModId, string SkinId)> _skinIds = new HashSet<(string, string)>();
        private readonly HashSet<string> _vanillaSpecies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasAny => _species.Count > 0 || _skins.Count > 0 || _vanilla.Count > 0;

        /// <summary>True when a mod sets materials for this vanilla skin ("&lt;species&gt;/&lt;skin name&gt;").</summary>
        public bool CoversVanillaSkin(string key) => _vanilla.ContainsKey(key);

        /// <summary>True when a mod sets materials for any vanilla skin of this species; cheap, before a vanilla key is built.</summary>
        public bool CoversVanillaSkinsOf(string? speciesId) => speciesId != null && _vanillaSpecies.Contains(speciesId);

        /// <summary>One owner's own entry for a material (no merging); null when it has none.</summary>
        public MaterialOverride? Block(MaterialOwnerKind kind, string? key, string materialName) =>
            Find(kind == MaterialOwnerKind.Skin ? _skins : kind == MaterialOwnerKind.Species ? _species : _vanilla, key, materialName);

        /// <summary>Every entry the table holds (the table's own copies), e.g. to tell which ramps and masks are still in use.</summary>
        public IEnumerable<MaterialOverride> Blocks =>
            _species.Values.Concat(_skins.Values).Concat(_vanilla.Values).SelectMany(materials => materials.Values);

        /// <summary>The mod whose block for this material currently wins (key: a skin's key, a species id or "&lt;species&gt;/&lt;skin name&gt;"); null when none.</summary>
        public MaterialBlockOwner? OwnerOf(MaterialOwnerKind kind, string key, string materialName) =>
            _owners.TryGetValue(OwnerKey(kind, key, materialName), out var owner) ? owner : null;

        private static string OwnerKey(MaterialOwnerKind kind, string key, string material) =>
            (int)kind + "\n" + (kind == MaterialOwnerKind.VanillaSkin ? key.ToUpperInvariant() : key) + "\n" + material; // vanilla keys match in any case, like _vanilla

        /// <summary>True when a mod sets materials for this species (any material); cheap, for animals no mod covers.</summary>
        public bool CoversSpecies(string? speciesId) => speciesId != null && _species.ContainsKey(speciesId);

        /// <summary>True when this mod's skin sets materials; without building its key, for animals no mod covers.</summary>
        public bool CoversSkin(string modId, string skinId) => _skinIds.Contains((modId, skinId));

        /// <param name="messages">Overridden species materials and files outside their mod, for the log.</param>
        public static MaterialTable Build(IReadOnlyList<LoadedMod> mods, out List<string> messages)
        {
            var table = new MaterialTable();
            messages = new List<string>();
            foreach (var mod in mods)
            {
                var id = mod.Manifest.Id;
                table.AddBlocks(table._species, MaterialOwnerKind.Species, mod.Manifest.SpeciesMaterials, mod, messages);
                table.AddBlocks(table._vanilla, MaterialOwnerKind.VanillaSkin, mod.Manifest.VanillaSkinMaterials, mod, messages);
                foreach (var skin in mod.Manifest.Skins)
                {
                    if (skin.Materials == null) continue;
                    var copies = Copy(skin.Materials, mod, messages);
                    if (copies.Count == 0) continue;
                    var key = skin.Key(id);
                    table._skins[key] = copies;
                    table._skinIds.Add((id, skin.Id));
                    foreach (var material in copies.Keys) table._owners[OwnerKey(MaterialOwnerKind.Skin, key, material)] = new MaterialBlockOwner(id, mod.Directory);
                }
            }
            foreach (var key in table._vanilla.Keys)
            {
                var slash = key.IndexOf('/');
                if (slash > 0) table._vanillaSpecies.Add(key.Substring(0, slash));
            }
            return table;
        }

        /// <summary>Adds a mod's species or vanilla skin blocks: per material the later mod's whole entry wins, and overriding is a message.</summary>
        private void AddBlocks(Dictionary<string, Dictionary<string, MaterialOverride>> into, MaterialOwnerKind kind,
            Dictionary<string, Dictionary<string, MaterialOverride>> blocks, LoadedMod mod, List<string> messages)
        {
            var id = mod.Manifest.Id;
            foreach (var block in blocks)
            {
                var copies = Copy(block.Value, mod, messages);
                if (copies.Count == 0) continue;
                if (!into.TryGetValue(block.Key, out var materials))
                    into[block.Key] = materials = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
                foreach (var pair in copies)
                {
                    var key = OwnerKey(kind, block.Key, pair.Key);
                    if (_owners.TryGetValue(key, out var earlier) && earlier.ModId != id)
                        messages.Add($"{block.Key}: '{id}' sets its {pair.Key} material settings, overriding '{earlier.ModId}' (the later mod in the load order wins).");
                    _owners[key] = new MaterialBlockOwner(id, mod.Directory);
                    materials[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>Enabled mods (in load order) whose species material settings a later one overrides, for the Mods tab.</summary>
        public static IReadOnlyList<MaterialClash> Clashes(IReadOnlyList<(string ModId, ModManifest Manifest)> enabledInLoadOrder)
        {
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            var clashes = new List<MaterialClash>();
            foreach (var (id, manifest) in enabledInLoadOrder)
            {
                Collect(MaterialOwnerKind.Species, manifest.SpeciesMaterials, id, owner, clashes);
                Collect(MaterialOwnerKind.VanillaSkin, manifest.VanillaSkinMaterials, id, owner, clashes);
            }
            return clashes;
        }

        private static void Collect(MaterialOwnerKind kind, Dictionary<string, Dictionary<string, MaterialOverride>> blocks, string id,
            Dictionary<string, string> owner, List<MaterialClash> clashes)
        {
            foreach (var block in blocks)
                foreach (var material in block.Value.Where(m => !m.Value.IsEmpty).Select(m => m.Key).Distinct(StringComparer.Ordinal))
                {
                    var key = OwnerKey(kind, block.Key, material);
                    if (owner.TryGetValue(key, out var earlier) && earlier != id) clashes.Add(new MaterialClash(block.Key, material, earlier, id));
                    owner[key] = id;
                }
        }

        /// <summary>
        /// The changes to one material: skin (a mod skin's, or a vanilla skin's) over base skin over species, field by field;
        /// null when none apply. A mod skin passes <paramref name="skinKey"/>, a vanilla skin <paramref name="vanillaSkinKey"/>.
        /// </summary>
        public MaterialOverride? For(string? speciesId, string? skinKey, string? inheritedKey, string? vanillaSkinKey, string materialName)
        {
            var skin = Find(_skins, skinKey, materialName);
            var vanilla = Find(_vanilla, vanillaSkinKey, materialName);
            var inherited = Find(_skins, inheritedKey, materialName);
            var species = Find(_species, speciesId, materialName);
            return skin == null && vanilla == null && inherited == null && species == null ? null : Merge(skin, vanilla, inherited, species);
        }

        /// <summary>
        /// One override from several, highest priority first: each iridescence field comes from the first that sets it, and each
        /// setting from the first that has it. The inputs are not changed; null when nothing is set.
        /// </summary>
        public static MaterialOverride? Merge(params MaterialOverride?[] highestFirst)
        {
            if (Array.TrueForAll(highestFirst, layer => layer == null)) return null; // before allocating: the common case, every tick
            var result = new MaterialOverride();
            IridescenceSettings? iridescence = null;
            FurMasks? fur = null;
            foreach (var layer in highestFirst)
            {
                if (layer == null) continue;
                foreach (var setting in layer.Settings)
                    if (!result.Settings.ContainsKey(setting.Key)) result.Settings[setting.Key] = setting.Value;
                if (layer.Fur is FurMasks f)
                {
                    fur ??= new FurMasks();
                    fur.Mask ??= f.Mask;
                    fur.InfantMask ??= f.InfantMask;
                    fur.Grow ??= f.Grow; // a higher false beats a lower true: that layer does not force fur
                }
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
            result.Fur = fur;
            return result.IsEmpty ? null : result;
        }

        /// <summary>
        /// The changes an animal wears, highest first: its live edit, its saved look, the live edit of its skin (a mod or vanilla
        /// skin), the table's skin, base skin, the live edit of its species, the table's species. The game's own values lie below
        /// all of them. Null when none is set; nothing is allocated then.
        /// </summary>
        public static MaterialOverride? MergeLayers(MaterialOverride? liveAnimal, MaterialOverride? savedAnimal, MaterialOverride? liveSkin,
            MaterialOverride? tableSkin, MaterialOverride? baseSkin, MaterialOverride? liveSpecies, MaterialOverride? tableSpecies)
        {
            if (liveAnimal == null && savedAnimal == null && liveSkin == null && tableSkin == null && baseSkin == null && liveSpecies == null && tableSpecies == null)
                return null;
            return Merge(liveAnimal, savedAnimal, liveSkin, tableSkin, baseSkin, liveSpecies, tableSpecies);
        }

        /// <summary>
        /// What lies under one scope's own block, in <see cref="MergeLayers"/> order: for an animal, its skin's and species' layers
        /// (live and table); for a skin, its base skin and species; for a species, nothing (only the game). The scope's own layers
        /// and everything above them are left out, so the in-game editor never shows or copies a higher layer's values.
        /// </summary>
        public static MaterialOverride? MergeBelow(LookScope scope, MaterialOverride? liveAnimal, MaterialOverride? savedAnimal, MaterialOverride? liveSkin,
            MaterialOverride? tableSkin, MaterialOverride? baseSkin, MaterialOverride? liveSpecies, MaterialOverride? tableSpecies) => scope switch
        {
            LookScope.Animal => MergeLayers(null, null, liveSkin, tableSkin, baseSkin, liveSpecies, tableSpecies),
            LookScope.Skin => MergeLayers(null, null, null, null, baseSkin, liveSpecies, tableSpecies),
            _ => null,
        };

        /// <summary>
        /// A copy whose relative file paths are made full against <paramref name="folder"/> (full paths stay); a path that is not
        /// a path drops that ramp or mask. The input is not changed.
        /// </summary>
        public static MaterialOverride WithFullPaths(MaterialOverride block, string folder) =>
            CopyOne(block, file =>
            {
                try
                {
                    return Path.GetFullPath(Path.Combine(folder, file));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    return null;
                }
            });

        /// <summary>A deep copy with each file path passed through <paramref name="map"/> (null drops that ramp or mask). The input is not changed.</summary>
        public static MaterialOverride WithPaths(MaterialOverride block, Func<string, string?> map) => CopyOne(block, map);

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
                var copy = CopyOne(pair.Value, file => Resolve(file, mod, messages));
                if (!copy.IsEmpty) copies[pair.Key] = copy;
            }
            return copies;
        }

        /// <summary>A deep copy with each file path passed through <paramref name="resolve"/>; a null path drops that ramp or mask.</summary>
        private static MaterialOverride CopyOne(MaterialOverride source, Func<string, string?> resolve)
        {
            var copy = new MaterialOverride();
            foreach (var setting in source.Settings) copy.Settings[setting.Key] = setting.Value;
            if (source.Iridescence is IridescenceSettings i)
            {
                var iridescence = new IridescenceSettings
                {
                    Strength = i.Strength == null ? null : new VariantNumbers { Normal = i.Strength.Normal, Albino = i.Strength.Albino, Melanistic = i.Strength.Melanistic, Leucistic = i.Strength.Leucistic },
                    Variation = i.Variation,
                    Ramp = CopyRamp(i.Ramp, resolve),
                    Mask = CopyMask(i.Mask, resolve),
                    Tiling = (float[]?)i.Tiling?.Clone(),
                    Offset = (float[]?)i.Offset?.Clone(),
                    Scale = i.Scale,
                    Power = i.Power,
                    Bias = i.Bias,
                };
                if (!iridescence.IsEmpty) copy.Iridescence = iridescence;
            }
            if (source.Fur is FurMasks fur)
            {
                var masks = new FurMasks { Mask = CopyMask(fur.Mask, resolve), InfantMask = CopyMask(fur.InfantMask, resolve), Grow = fur.Grow };
                if (!masks.IsEmpty) copy.Fur = masks;
            }
            return copy;
        }

        private static RampSource? CopyRamp(RampSource? ramp, Func<string, string?> resolve)
        {
            if (ramp == null) return null;
            if (ramp.File == null)
                return new RampSource
                {
                    Stops = ramp.Stops == null ? null : new List<Rgb>(ramp.Stops),
                    ThinFilm = ramp.ThinFilm == null ? null : new ThinFilmSettings { Thickness = ramp.ThinFilm.Thickness, Index = ramp.ThinFilm.Index },
                };
            var path = resolve(ramp.File);
            return path == null ? null : new RampSource { File = path };
        }

        private static MaskSource? CopyMask(MaskSource? mask, Func<string, string?> resolve)
        {
            if (mask == null) return null;
            var path = mask.File == null ? null : resolve(mask.File);
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
