using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// The vanilla species a look can start from or take a ramp or mask from (<see cref="RampSource.Species"/>), read from the
    /// game at runtime. The lists come from the animal database (PKPersistentData.Animals) without loading a prefab: a species
    /// whose skins shimmer (defaultVariationData.iridescence) or have fur (adultFurMap.RuntimeKeyIsValid()). A species' material
    /// is its prefab's (AnimalData.animalRef), loaded in the background through the game's own Addressables path
    /// (VAssetReferenceExt.LoadVAddressableAs, as AnimalStatueResolver loads one), never instantiated: its numbers are read, its
    /// ramp and mask copied into textures of Tyrant's own (in memory only: a texture borrowed from another species is unloaded
    /// when that species leaves), then the prefab is released. The copies are kept for the session. A species that cannot be
    /// read logs one warning, and its references show the game's own values.
    /// </summary>
    internal static class SpeciesLooks
    {
        private const string RampProperty = "_IridescenceTexture";
        private const string MaskProperty = "_IridescenceMask";
        private const float LoadTimeout = 60f; // seconds before a load that never finishes counts as failed
        private static readonly string[] Shaping = { "_IridescenceScale", "_IridescencePower", "_IridescenceBias" };

        private sealed class Species
        {
            public Species(string id, string name, object? reference, List<PresetSkin> skins)
            {
                Id = id;
                Name = name;
                Reference = reference;
                Skins = skins;
            }

            public string Id { get; }
            public string Name { get; }
            public object? Reference { get; } // BaseAnimalData.animalRef (an AssetReference)
            public List<PresetSkin> Skins { get; }
            public SpeciesPreset? Preset; // once the material was read
            public Texture2D? Ramp, Mask; // Tyrant's copies (null: the material has none)
            public object? User; // the boxed VAddressable<GameObject>.User while the prefab loads
            public float Started;
            public bool Failed;
        }

        private static readonly Dictionary<string, Species> All = new Dictionary<string, Species>(StringComparer.Ordinal);
        private static IReadOnlyList<(string Id, string Name)> _shimmering = Array.Empty<(string, string)>();
        private static IReadOnlyList<(string Id, string Name)> _furred = Array.Empty<(string, string)>();
        private static bool _listed;
        private static readonly List<Species> Loading = new List<Species>();
        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, MethodInfo?> KeyValid = new Dictionary<Type, MethodInfo?>();
        private static MethodInfo? _load;
        private static bool _loadRead;
        private static PropertyInfo? _animals; // PKPersistentData.Animals (the AnimalDatabase)
        private static bool _databaseRead;

        /// <summary>A species' load finished (its copies and numbers are ready, or it failed): raised from <see cref="Tick"/>.</summary>
        public static event Action<string>? Loaded;

        /// <summary>The preset values as <see cref="LookEditing.ApplyPreset(LookDraft, PresetKind, ISpeciesPresets, string)"/> reads them.</summary>
        public static ISpeciesPresets Source { get; } = new GameSource();

        private sealed class GameSource : ISpeciesPresets
        {
            public SpeciesPreset? Preset(string species) => Values(species);
        }

        /// <summary>The species offered for a kind of preset, by name; empty until the animal database exists.</summary>
        public static IReadOnlyList<(string Id, string Name)> List(PresetKind kind)
        {
            Read();
            return kind == PresetKind.Fur ? _furred : _shimmering;
        }

        /// <summary>The name the game shows for a species id (the id itself when the database does not have it).</summary>
        public static string NameOf(string species)
        {
            Read();
            return All.TryGetValue(species, out var s) ? s.Name : species;
        }

        /// <summary>The species' preset once its material was read; null while it loads (the load starts here) or when it cannot be read.</summary>
        public static SpeciesPreset? Values(string species)
        {
            var s = Find(species);
            if (s == null || s.Failed) return null;
            if (s.Preset == null) StartLoad(s);
            return s.Preset;
        }

        /// <summary>True when the species is unknown or its material could not be read: its references show the game's own values.</summary>
        public static bool Unavailable(string species)
        {
            var s = Find(species);
            return s == null || s.Failed;
        }

        /// <summary>Tyrant's copy of the species' ramp; null while it loads (the load starts here), or when it has none or failed.</summary>
        public static Texture? Ramp(string species) => Copies(species)?.Ramp;

        /// <summary>Tyrant's copy of the species' iridescence mask, as <see cref="Ramp"/>.</summary>
        public static Texture? Mask(string species) => Copies(species)?.Mask;

        private static Species? Copies(string species)
        {
            var s = Find(species);
            if (s == null)
            {
                if (_listed && Warned.Add("unknown|" + species))
                    FrameworkMod.Log.Warning($"\"{species}\" is not a species of the game; its ramp and mask references show the game's own.");
                return null;
            }
            if (s.Failed) return null;
            if (s.Preset == null)
            {
                StartLoad(s);
                return null;
            }
            return s;
        }

        /// <summary>Once a frame: finishes the loads that completed; returns the species that became ready (or failed) this frame.</summary>
        public static IReadOnlyList<string> Tick()
        {
            if (Loading.Count == 0) return Array.Empty<string>(); // nothing allocated on the frames without a load
            var done = new List<string>();
            for (var i = Loading.Count - 1; i >= 0; i--)
            {
                var s = Loading[i];
                bool finished;
                try
                {
                    finished = Poll(s);
                }
                catch (Exception ex)
                {
                    Fail(s, ex.GetBaseException().Message);
                    finished = true;
                }
                if (!finished) continue;
                Loading.RemoveAt(i);
                Release(s);
                done.Add(s.Id);
            }
            foreach (var id in done)
            {
                try
                {
                    Loaded?.Invoke(id);
                }
                catch (Exception ex)
                {
                    FrameworkMod.Log.Warning($"{id}: its look was loaded, but showing it failed: {ex.GetBaseException().Message}");
                }
            }
            return done;
        }

        // ---- the animal database ----

        private static Species? Find(string species)
        {
            Read();
            return All.TryGetValue(species, out var s) ? s : null;
        }

        /// <summary>PKPersistentData.Animals.AllAnimalDataAssets, once it can be read (the main menu has it).</summary>
        private static void Read()
        {
            if (_listed) return;
            if (!_databaseRead)
            {
                _databaseRead = true; // looked up once: until the database exists this runs whenever a preset or reference is asked for
                var persistent = AccessTools.TypeByName("PrehistoricKingdom.PKPersistentData");
                _animals = persistent == null ? null : AccessTools.Property(persistent, "Animals");
                if (_animals == null) FrameworkMod.Log.Warning("PKPersistentData.Animals was not found (game updated?); there are no species presets.");
            }
            if (_animals == null) return;
            IList? assets;
            try
            {
                var database = _animals.GetValue(null, null);
                assets = database == null ? null : AccessTools.Property(database.GetType(), "AllAnimalDataAssets")?.GetValue(database, null) as IList;
            }
            catch (Exception)
            {
                return; // not ready yet: its getter throws until the reference is set
            }
            if (assets == null || assets.Count == 0) return;
            _listed = true;
            try
            {
                foreach (var data in assets)
                {
                    if (data == null || !(Field(data, "speciesID") is string id) || id.Length == 0) continue;
                    if (id.StartsWith("tyrant.", StringComparison.Ordinal) || All.ContainsKey(id)) continue; // presets are vanilla species
                    var name = AccessTools.Property(data.GetType(), "DisplayName")?.GetValue(data, null) as string;
                    var skins = new List<PresetSkin>();
                    if (Field(data, "skinsData") is IList list)
                        foreach (var skin in list)
                        {
                            if (skin == null) continue;
                            foreach (var sex in new[] { "maleVariationData", "femaleVariationData" })
                                if (Field(skin, sex) is object asset && SkinOf(asset) is PresetSkin read) skins.Add(read);
                        }
                    All[id] = new Species(id, string.IsNullOrEmpty(name) ? id : name!, Field(data, "animalRef"), skins);
                }
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"The game's animal data could not be read for species presets ({ex.GetBaseException().Message}); the species listed are the ones read so far.");
            }
            var presets = All.Values.Select(s => (s, Preset: SpeciesPreset.Of(s.Id, s.Name, s.Skins, null, false, false))).ToList();
            _shimmering = SpeciesPreset.Sorted(presets.Where(p => p.Preset.Shimmers).Select(p => (p.s.Id, p.s.Name)));
            _furred = SpeciesPreset.Sorted(presets.Where(p => p.Preset.Furry).Select(p => (p.s.Id, p.s.Name)));
        }

        /// <summary>
        /// An AnimalSkinVariationAsset as a preset reads it: defaultVariationData.iridescence and iridescenceStrength (a
        /// mutation's own where its genetic data differs, as AnimalSkinVariation.CreateRandomSkinVariation gives it), whether
        /// adultFurMap is a valid key (AnimalMaterials.CalculateKeywords' test), and adultFuzzDroopiness.
        /// </summary>
        private static PresetSkin? SkinOf(object asset)
        {
            var data = Field(asset, "defaultVariationData");
            var skin = new PresetSkin { Shimmers = data != null && Field(data, "iridescence") is true };
            if (data != null && Field(data, "iridescenceStrength") is float normal)
            {
                float? Own(string mutation) => Field(asset, mutation) is object genetic && Field(genetic, "iridescenceStrength") is float m && m != normal ? m : (float?)null;
                skin.Strength = new VariantNumbers { Normal = normal, Albino = Own("albinoVariationData"), Melanistic = Own("melanisticVariationData"), Leucistic = Own("leucisticVariationData") };
            }
            skin.HasFur = Field(asset, "adultFurMap") is object map && KeyIsValid(map);
            if (Field(asset, "adultFuzzDroopiness") is Vector3 droop) skin.Droop = new[] { droop.x, droop.y, droop.z };
            return skin;
        }

        private static bool KeyIsValid(object reference)
        {
            var type = reference.GetType();
            if (!KeyValid.TryGetValue(type, out var valid)) KeyValid[type] = valid = AccessTools.Method(type, "RuntimeKeyIsValid", Type.EmptyTypes);
            return valid != null && valid.Invoke(reference, null) is true;
        }

        private static object? Field(object target, string name) => AccessTools.Field(target.GetType(), name)?.GetValue(target);

        // ---- loading a species' material ----

        /// <summary>VAddressables.VAssetReferenceExt.LoadVAddressableAs&lt;GameObject&gt;(reference, loadImmediately, errorOnInvalid, callback).</summary>
        private static MethodInfo? LoadMethod()
        {
            if (_loadRead) return _load;
            _loadRead = true;
            var ext = AccessTools.TypeByName("VAddressables.VAssetReferenceExt");
            var method = ext == null ? null : AccessTools.GetDeclaredMethods(ext).FirstOrDefault(m => m.Name == "LoadVAddressableAs" && m.IsGenericMethodDefinition && m.GetParameters().Length == 4);
            _load = method?.MakeGenericMethod(typeof(GameObject));
            if (_load == null) FrameworkMod.Log.Warning("VAssetReferenceExt.LoadVAddressableAs was not found (game updated?); ramps and masks taken from a species show the game's own.");
            return _load;
        }

        private static void StartLoad(Species s)
        {
            if (s.User != null || s.Failed || Loading.Contains(s)) return;
            if (s.Reference == null || !KeyIsValid(s.Reference))
            {
                Fail(s, "it has no prefab to read");
                return;
            }
            if (!(LoadMethod() is MethodInfo load))
            {
                s.Failed = true;
                return;
            }
            try
            {
                s.User = load.Invoke(null, new[] { s.Reference, false, false, null }); // async; the game shares one load per prefab
                s.Started = Time.realtimeSinceStartup;
                Loading.Add(s);
            }
            catch (Exception ex)
            {
                Fail(s, ex.GetBaseException().Message);
            }
        }

        /// <summary>True when the load is over: the copies and numbers are made, or it failed.</summary>
        private static bool Poll(Species s)
        {
            var user = s.User;
            if (user == null)
            {
                Fail(s, "its load could not start");
                return true;
            }
            var type = user.GetType();
            if (AccessTools.Property(type, "IsValid")?.GetValue(user, null) is false)
            {
                Fail(s, "its prefab could not be loaded");
                return true;
            }
            if (!(AccessTools.Method(type, "IsValidAndLoaded")?.Invoke(user, null) is true))
            {
                var status = AccessTools.Property(type, "Status")?.GetValue(user, null)?.ToString();
                if (status == "Failed") Fail(s, "its prefab could not be loaded");
                else if (Time.realtimeSinceStartup - s.Started > LoadTimeout) Fail(s, $"its prefab did not load within {LoadTimeout:0} s");
                else return false;
                return true;
            }
            if (!(AccessTools.Property(type, "Asset")?.GetValue(user, null) is GameObject prefab) || prefab == null)
            {
                Fail(s, "its prefab is empty");
                return true;
            }
            var material = MaterialOf(prefab);
            if (material == null)
            {
                Fail(s, "its prefab has no material");
                return true;
            }
            var floats = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var key in Shaping.Concat(LookEditing.PresetFurSettings))
                if (material.HasProperty(key)) floats[key] = material.GetFloat(key);
            var ramp = material.HasProperty(RampProperty) ? material.GetTexture(RampProperty) : null;
            var mask = material.HasProperty(MaskProperty) ? material.GetTexture(MaskProperty) : null;
            s.Ramp = ramp != null ? Copy(ramp, mipmaps: false, compress: false) : null;
            s.Mask = mask != null ? Copy(mask, mipmaps: true, compress: true) : null;
            s.Preset = SpeciesPreset.Of(s.Id, s.Name, s.Skins, floats, s.Ramp != null, s.Mask != null);
            FrameworkMod.Log.Msg($"{s.Id}: its look is ready for presets and references (ramp: {(s.Ramp != null ? "yes" : "none")}, mask: {(s.Mask != null ? "yes" : "none")}).");
            return true;
        }

        /// <summary>The prefab's base material as the game reads it: its LODGroup's first LOD's first renderer (VivariumAnimalSetup.GetBaseMaterial), else its first skinned renderer.</summary>
        private static Material? MaterialOf(GameObject prefab)
        {
            var group = prefab.GetComponent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                if (lods.Length > 0 && lods[0].renderers != null && lods[0].renderers.Length > 0 && lods[0].renderers[0] != null)
                    return lods[0].renderers[0].sharedMaterial;
            }
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            return renderers.Length > 0 ? renderers[0].sharedMaterial : null;
        }

        /// <summary>A copy of a game texture Tyrant owns (read back through a render texture), with the source's wrap and filter.</summary>
        private static Texture2D Copy(Texture source, bool mipmaps, bool compress)
        {
            var copy = MaterialsModule.ReadPixels(source, mipmaps);
            try
            {
                copy.name = "Tyrant copy of " + source.name;
                copy.wrapMode = source.wrapMode;
                copy.filterMode = source.filterMode;
                if (compress && copy.width % 4 == 0 && copy.height % 4 == 0) copy.Compress(true);
                copy.Apply(false, true); // no longer readable: frees the CPU copy
                return copy;
            }
            catch
            {
                UnityEngine.Object.Destroy(copy);
                throw;
            }
        }

        /// <summary>Lets the game's prefab go (the copies stay): the game unloads it once no one else uses it.</summary>
        private static void Release(Species s)
        {
            var user = s.User;
            s.User = null;
            if (user == null) return;
            try
            {
                (user as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"{s.Id}: its prefab could not be released: {ex.GetBaseException().Message}");
            }
        }

        private static void Fail(Species s, string why)
        {
            s.Failed = true;
            s.Preset = null;
            foreach (var copy in new[] { s.Ramp, s.Mask })
                if (copy != null) UnityEngine.Object.Destroy(copy); // a copy made before the failure is not used
            s.Ramp = s.Mask = null;
            if (Warned.Add("failed|" + s.Id))
                FrameworkMod.Log.Warning($"{s.Id}: its look could not be read ({why}); presets from it are off, and its ramp and mask references show the game's own.");
        }
    }
}
