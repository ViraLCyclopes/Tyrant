using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>Experimental template species. Register before AI allocation and set prefab identity before activation.</summary>
    internal static class SpeciesModule
    {
        private static readonly HashSet<object> Databases = new HashSet<object>(ReferenceComparer.Instance);
        private static readonly Dictionary<object, string> Added = new Dictionary<object, string>(ReferenceComparer.Instance);
        private static readonly HashSet<string> Ids = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Each added species' template: it is unlocked when its template is.</summary>
        private static readonly Dictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Each added species' scientific name, its place in the game's alphabetical animal list (<see cref="NurseryOrder"/>).</summary>
        private static readonly Dictionary<string, string> SortNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private static MethodInfo? _isUnlocked;
        private static MethodInfo? _unlockProgress;
        private static readonly Dictionary<object, List<AnimalText>> Texts = new Dictionary<object, List<AnimalText>>(ReferenceComparer.Instance);
        private static readonly Dictionary<object, GameObject> Prefabs = new Dictionary<object, GameObject>(ReferenceComparer.Instance);
        private static readonly Dictionary<string, AssetBundle> Bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        // Recovered materials borrow the template's shader. Keep its addressable dependency
        // graph loaded for as long as the cached authored prefab can spawn more animals.
        private static readonly Dictionary<GameObject, object> TemplateAssets = new Dictionary<GameObject, object>();
        private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static List<LoadedMod> _mods = new List<LoadedMod>();
        private static Type? _persistent;
        private static Type? _animal;
        private static Type? _native;
        private static FieldInfo? _objectId;
        private static FieldInfo? _animalData;

        public static void Start(IEnumerable<LoadedMod> mods, HarmonyLib.Harmony harmony)
        {
            _mods = mods.Where(m => m.Manifest.Species.Count > 0).ToList();
            if (_mods.Count == 0) return;
            // Resolve every required hook before enabling registration. A renamed game API must not create unspawnable species.
            try
            {
                _persistent = RequireType("PrehistoricKingdom.PKPersistentData");
                _animal = RequireType("PrehistoricKingdom.Animal");
                _objectId = AccessTools.Field(_animal, "ObjectID") ?? throw new MissingFieldException("Animal.ObjectID");
                _animalData = AccessTools.Field(_animal, "_animalData") ?? throw new MissingFieldException("Animal._animalData");
                var database = RequireMethod("PrehistoricKingdom.AnimalDatabase", "Init");
                var manager = RequireMethod("PrehistoricKingdom.AnimalManager", "Awake");
                var native = RequireMethod("PrehistoricKingdom.AnimalsV2.AI.AnimalsGlobal", "InitializeNativeData");
                _native = native.DeclaringType;
                if (AccessTools.Property(_native, "IsCreated") == null) throw new MissingMemberException("AnimalsGlobal.IsCreated");
                var spawn = RequireMethod("PrehistoricKingdom.AddrAnimals", "InstantiateInternal");
                var parameters = spawn.GetParameters();
                if (parameters.Length != 3 || parameters[2].ParameterType != typeof(bool))
                    throw new MissingMethodException("AddrAnimals.InstantiateInternal signature changed.");
                if (AccessTools.Method(parameters[1].ParameterType, "InstantiateToInstance", new[] { typeof(Func<GameObject, GameObject>) }) == null)
                    throw new MissingMethodException("Addressable user InstantiateToInstance signature changed.");
                var unlocked = RequireMethod("PrehistoricKingdom.AnimalManager", "IsAnimalUnlocked");
                var progress = RequireMethod("PrehistoricKingdom.AnimalManager", "GetAnimalUnlockProgress");
                var displayName = AccessTools.PropertyGetter(RequireType("PrehistoricKingdom.BaseAnimalData"), "DisplayName")
                    ?? throw new MissingMethodException("BaseAnimalData.DisplayName");
                harmony.Patch(database, prefix: new HarmonyMethod(typeof(SpeciesModule), nameof(DatabaseInit)));
                harmony.Patch(manager, prefix: new HarmonyMethod(typeof(SpeciesModule), nameof(EnsureReady)));
                harmony.Patch(native, prefix: new HarmonyMethod(typeof(SpeciesModule), nameof(EnsureReady)));
                (_isUnlocked, _unlockProgress) = (unlocked, progress);
                harmony.Patch(unlocked, postfix: new HarmonyMethod(typeof(SpeciesModule), nameof(Unlocked)));
                harmony.Patch(progress, postfix: new HarmonyMethod(typeof(SpeciesModule), nameof(UnlockProgress)));
                // Display order only: a missing method leaves custom species at the end of the Alphabet sort.
                var rebuildAll = AccessTools.Method(RequireType("PrehistoricKingdom.AnimalDatabase"), "RebuildAllAnimalDataAssets");
                if (rebuildAll != null) harmony.Patch(rebuildAll, postfix: new HarmonyMethod(typeof(SpeciesModule), nameof(AllAnimalsRebuilt)));
                else FrameworkMod.Log.Warning("AnimalDatabase.RebuildAllAnimalDataAssets not found; new species are listed last in the Nursery's Alphabet sort.");
                harmony.Patch(displayName, postfix: new HarmonyMethod(typeof(SpeciesModule), nameof(LocalizedName)));
            }
            catch (Exception ex)
            {
                _mods.Clear();
                FrameworkMod.Log.Error("New species support is disabled: " + ex.GetBaseException().Message);
            }
        }

        private static Type RequireType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        private static MethodInfo RequireMethod(string type, string name) =>
            AccessTools.Method(RequireType(type), name) ?? throw new MissingMethodException(type, name);

        public static void Tick()
        {
            EnsureReady();
            SpeciesMaterialDiagnostics.Tick();
        }

        private static void EnsureReady()
        {
            if (_mods.Count == 0 || _persistent == null) return;
            object? database;
            try
            {
                if (StaticValue(_persistent, "Instance") == null) return;
                database = StaticValue(_persistent, "Animals");
            }
            catch { return; } // persistent references are not loaded yet
            if (database != null) Register(database);
        }

        private static void DatabaseInit(object __instance) => Register(__instance);

        private static void Register(object database)
        {
            if (_mods.Count == 0 || Databases.Contains(database)) return;
            var list = AccessTools.Field(database.GetType(), "AnimalDataAssets")?.GetValue(database) as IList;
            if (list == null || list.Count == 0) return;
            Databases.Add(database);
            if (_native != null && StaticValue(_native, "IsCreated") is true)
            {
                FrameworkMod.Log.Error("New species were not registered because the game's native AI tables already exist. Restart the game with the species mod installed.");
                return;
            }
            // Only vanilla templates: snapshot before appending, so load order cannot change inheritance.
            var originals = list.Cast<object>().ToList();
            foreach (var mod in _mods)
            foreach (var entry in mod.Manifest.Species)
            {
                var id = entry.Key(mod.Manifest.Id);
                ScriptableObject? data = null;
                try
                {
                    if (list.Cast<object>().Any(a => Field(a, "speciesID") as string == id))
                        throw new InvalidOperationException("species identity already exists");
                    var template = originals.FirstOrDefault(a => string.Equals(Field(a, "speciesID") as string, entry.Base, StringComparison.Ordinal));
                    if (!(template is ScriptableObject source)) throw new InvalidOperationException("vanilla regular-animal template not found: " + entry.Base);
                    if (list.Count >= ushort.MaxValue) throw new InvalidOperationException("native species index limit reached");
                    var prefab = entry.Prefab == null ? null : LoadPrefab(mod.Directory, entry.Prefab);
                    data = UnityEngine.Object.Instantiate(source);
                    var fieldEditor = new AnimalFieldEditor(source, data, value => value is UnityEngine.Object, value =>
                    {
                        if (value is UnityEngine.Object asset) return UnityEngine.Object.Instantiate(asset);
                        if (value is AnimationCurve curve) return new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
                        return AnimalFieldEditor.ShallowClone(value);
                    });
                    fieldEditor.Apply(data, entry.Fields);
                    data.name = id;
                    Set(data, "speciesID", id);
                    Set(data, "shortSpeciesID", id);
                    Set(data, "displayName", entry.Name);
                    Set(data, "genusName", entry.Genus);
                    Set(data, "speciesName", entry.Epithet);
                    Set(data, "showInGUI", true);
                    if (entry.Cost.HasValue) Set(data, "cost", entry.Cost.Value);
                    ApplyStats(data, entry.Stats);
                    ApplyText(data, id, entry.Texts);
                    if (entry.Icon != null) Set(data, "heroRender", LoadSprite(mod.Directory, entry.Icon, id + ".icon"));
                    var sourceSkins = Field(data, "skinsData") as IList ?? throw new MissingFieldException("skinsData");
                    if (sourceSkins.Count == 0) throw new InvalidOperationException("template has no skins");
                    if (entry.StartingSkin >= sourceSkins.Count)
                        throw new InvalidOperationException($"starting skin {entry.StartingSkin} is not in the template (it has {sourceSkins.Count})");
                    // One skin: the species is its own animal. Mods add more through skins[] entries.
                    var skins = (IList)Activator.CreateInstance(sourceSkins.GetType());
                    var skin = Clone.Invoke(sourceSkins[entry.StartingSkin], null);
                    Set(skin, "mainData", data);
                    // Variation assets remain read-only templates; per-skin colors clone them when changed.
                    if (entry.StartingSkinName != null) Set(skin, "skinName", entry.StartingSkinName);
                    if (entry.Swatch != null) Set(skin, "skinThumbnail", LoadSprite(mod.Directory, entry.Swatch, id + ".swatch"));
                    skins.Add(skin);
                    Set(data, "skinsData", skins);
                    var nativeId = AccessTools.Field(data.GetType(), "NativeSpeciesID") ?? throw new MissingFieldException("NativeSpeciesID");
                    nativeId.SetValue(data, Activator.CreateInstance(nativeId.FieldType, new object[] { (ushort)list.Count }));
                    list.Add(data);
                    Added[data] = id;
                    if (prefab != null) Prefabs[data] = prefab;
                    Texts[data] = entry.Texts;
                    Ids.Add(id);
                    Templates[id] = entry.Base;
                    SortNames[id] = entry.Genus.Trim() + " " + entry.Epithet.Trim();
                    FrameworkMod.Log.Msg($"Added species {entry.Name} ({id}), template {entry.Base}." + (prefab == null ? "" : " Using Unity prefab " + entry.Prefab!.Asset + "."));
                }
                catch (Exception ex)
                {
                    if (data != null && !Added.ContainsKey(data)) UnityEngine.Object.Destroy(data);
                    FrameworkMod.Log.Error($"{id}: species skipped: {ex.GetBaseException().Message}");
                }
            }
        }

        /// <summary>A sprite from a PNG inside the mod folder (species icon or swatch).</summary>
        private static Sprite LoadSprite(string modDirectory, string file, string name)
        {
            var path = Path.GetFullPath(Path.Combine(modDirectory, file));
            if (!ModPaths.IsInside(path, modDirectory)) throw new InvalidOperationException("PNG path escapes the mod folder: " + file);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path), false)) { UnityEngine.Object.Destroy(texture); throw new InvalidOperationException("Cannot decode PNG: " + file); }
            texture.name = name;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        /// <summary>
        /// True once new species had their chance to register (or there are none): skins for them must not be added before,
        /// or they are reported missing and lost.
        /// </summary>
        public static bool RegistrationSettled => _mods.Count == 0 || Databases.Count > 0;

        private static object? Field(object data, string name) => AccessTools.Field(data.GetType(), name)?.GetValue(data);
        private static GameObject LoadPrefab(string directory, SpeciesPrefab reference)
        {
            reference.Validate();
            var path = Path.GetFullPath(Path.Combine(directory, reference.Bundle));
            if (!ModPaths.IsInside(path, directory)) throw new InvalidOperationException("Prefab bundle path escapes the mod folder.");
            if (!Bundles.TryGetValue(path, out var bundle))
            {
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) throw new InvalidOperationException("Cannot load prefab bundle: " + reference.Bundle);
                // Keep assets alive while spawned instances reference their meshes and materials.
                Bundles.Add(path, bundle);
            }
            var prefab = bundle.LoadAsset<GameObject>(reference.Asset);
            if (prefab == null) throw new InvalidOperationException("Prefab asset is missing from the bundle: " + reference.Asset);
            if (prefab.activeSelf) throw new InvalidOperationException("Authored animal prefab root must be inactive so identity can be assigned before Awake.");
            foreach (var name in new[] { "PrehistoricKingdom.Animal", "AnimalSetup", "PrehistoricKingdom.AnimalsV2.AnimalAnimationController", "PrehistoricKingdom.AnimalGrowthManager", "PrehistoricKingdom.Audio.AnimalAudioComponent" })
                if (prefab.GetComponent(RequireType(name)) == null)
                    throw new InvalidOperationException("Authored prefab root is missing PK component " + name);
            if (prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                throw new InvalidOperationException("Authored animal prefab has no SkinnedMeshRenderer.");
            if (prefab.GetComponentInChildren<Animator>(true) == null)
                throw new InvalidOperationException("Authored animal prefab has no Animator.");
            if (prefab.GetComponentsInChildren<Component>(true).Any(c => c == null))
                throw new InvalidOperationException("Authored prefab contains a missing script.");
            return prefab;
        }
        private static object? StaticValue(Type type, string name) =>
            AccessTools.Property(type, name)?.GetValue(null, null) ?? AccessTools.Field(type, name)?.GetValue(null);
        private static void Set(object data, string name, object? value) =>
            (AccessTools.Field(data.GetType(), name) ?? throw new MissingFieldException(data.GetType().FullName, name)).SetValue(data, value);

        private static void ApplyStats(object data, IReadOnlyDictionary<string, double> stats)
        {
            SpeciesStats.Validate(stats);
            foreach (var stat in SpeciesStats.Fields.Where(s => stats.ContainsKey(s.Id)))
            {
                var value = (float)stats[stat.Id];
                if (stat.Component == null) Set(data, stat.Field, value);
                else
                {
                    var vector = Field(data, stat.Field) ?? throw new MissingFieldException(stat.Field);
                    Set(vector, stat.Component, value);
                    Set(data, stat.Field, vector);
                }
            }
            // Validate overridden ranges against the inherited endpoint too.
            foreach (var group in SpeciesStats.Fields.Where(s => s.Component != null).GroupBy(s => s.Field))
            {
                if (!group.Any(s => stats.ContainsKey(s.Id))) continue;
                var range = Field(data, group.Key)!;
                if (Convert.ToDouble(Field(range, "x")) > Convert.ToDouble(Field(range, "y")))
                    throw new InvalidOperationException(group.Key + ": minimum exceeds maximum after template inheritance");
            }
        }

        private static void LocalizedName(object __instance, ref string __result)
        {
            if (!Texts.TryGetValue(__instance, out var texts)) return;
            var language = StaticValue(RequireType("PrehistoricKingdom.PKLocalizationManager"), "CurrentLanguage")?.ToString() ?? "English";
            __result = AnimalText.Resolve(texts, "name", language) ?? __result;
        }

        private static void ApplyText(object data, string id, IReadOnlyList<AnimalText> texts)
        {
            var keys = new[] { ("description", "paleopediaDescription"), ("characterNotes", "paleopediaCharacterNotes"), ("socialInfo", "paleopediaSocialInfo") };
            foreach (var pair in keys)
            {
                if (!texts.Any(t => t.Key == pair.Item1 && !string.IsNullOrEmpty(t.Text))) continue;
                var assetType = RequireType("PrehistoricKingdom.PKLocalizationAsset");
                var asset = ScriptableObject.CreateInstance(assetType);
                asset.name = id + "." + pair.Item1;
                Set(asset, "SkipSelfValidation", true);
                var localization = Field(asset, "Localization")!;
                var elements = Field(localization, "localizationElements")!;
                var arrayField = AccessTools.Field(elements.GetType(), "elements");
                var itemType = arrayField.FieldType.GetElementType()!;
                var languages = (Array)StaticValue(RequireType("PrehistoricKingdom.PKLocalizationManager"), "PKLanguages")!;
                var array = Array.CreateInstance(itemType, languages.Length);
                for (int i = 0; i < languages.Length; i++)
                {
                    var item = Activator.CreateInstance(itemType)!;
                    Set(item, "language", languages.GetValue(i));
                    Set(item, "text", AnimalText.Resolve(texts, pair.Item1, languages.GetValue(i)!.ToString()) ?? "");
                    array.SetValue(item, i);
                }
                arrayField.SetValue(elements, array);
                Set(localization, "localizationElements", elements);
                Set(asset, "Localization", localization);
                Set(data, pair.Item2, asset);
            }
        }

        public static bool HasSpecies => Added.Count > 0 || _mods.Count > 0;

        public static bool TryCustom(object data, out string id, out GameObject? authored)
        {
            authored = null;
            if (!Added.TryGetValue(data, out id!)) return false;
            Prefabs.TryGetValue(data, out authored);
            return true;
        }

        /// <summary>An authored prefab borrows the template's assets and gets its materials rebuilt before it is cloned.</summary>
        public static void PrepareAuthored(GameObject authored, GameObject template, object user)
        {
            RetainTemplateAssets(authored, user);
            RecoveredMaterials.Restore(authored, template, message => FrameworkMod.Log.Msg(message));
        }

        /// <summary>Gives a custom species' fresh, inactive instance its identity before it wakes.</summary>
        public static void Identify(GameObject instance, object data, string id)
        {
            var component = instance.GetComponent(_animal!);
            if (component == null) throw new InvalidOperationException("template prefab has no Animal component");
            _objectId!.SetValue(component, id);
            _animalData!.SetValue(component, data);
        }

        /// <summary>Starts material diagnostics for an authored-prefab instance once it is awake.</summary>
        public static void Activated(GameObject instance, object data, string id)
        {
            if (!Prefabs.ContainsKey(data)) return;
            SpeciesMaterialDiagnostics.Track(instance, instance.GetComponent(_animal!), id);
        }

        private static void RetainTemplateAssets(GameObject authored, object user)
        {
            if (TemplateAssets.ContainsKey(authored)) return;
            var provider = AccessTools.Property(user.GetType(), "Provider")?.GetValue(user, null)
                ?? throw new MissingMemberException("VAddressable.User.Provider");
            var allocate = AccessTools.Method(provider.GetType(), "AllocateUser")
                ?? throw new MissingMethodException("VAddressable.AllocateUser");
            var retained = allocate.Invoke(provider, new object?[] { true, null })
                ?? throw new InvalidOperationException("Cannot retain the template's addressable assets.");
            TemplateAssets.Add(authored, retained);
            FrameworkMod.Log.Msg("Retained native template assets for Unity prefab '" + authored.name + "'.");
        }

        public static void ReleaseTemplateAssets()
        {
            foreach (var user in TemplateAssets.Values)
                try { (user as IDisposable)?.Dispose(); }
                catch (Exception ex) { FrameworkMod.Log.Warning("Template asset release: " + ex.GetBaseException().Message); }
            TemplateAssets.Clear();
        }

        // An added species is unlocked, and as far researched, as its template: the Nursery's unlocked-first sorts then place it
        // among the animals the player has, and in career it waits for the template's research. Asking for the template runs
        // these postfixes again with the template's id, which is not an added species, so they leave its answer alone.
        private static void Unlocked(object __instance, string __0, ref bool __result)
        {
            if (_isUnlocked != null && Templates.TryGetValue(__0, out var template)) __result = (bool)_isUnlocked.Invoke(__instance, new object[] { template });
        }
        private static void UnlockProgress(object __instance, string __0, ref float __result)
        {
            if (_unlockProgress != null && Templates.TryGetValue(__0, out var template)) __result = (float)_unlockProgress.Invoke(__instance, new object[] { template });
        }

        /// <summary>The game sorts its all-animals list by species id; added species go by their scientific name instead.</summary>
        private static void AllAnimalsRebuilt(object __instance)
        {
            if (SortNames.Count == 0) return;
            try
            {
                if (!(AccessTools.Field(__instance.GetType(), "allAnimalDataAssets")?.GetValue(__instance) is IList list)) return;
                var sorted = list.Cast<object>().ToList();
                sorted.Sort((a, b) => NurseryOrder.Compare(Field(a, "speciesID") as string ?? "", Field(b, "speciesID") as string ?? "", SortNames));
                for (var i = 0; i < sorted.Count; i++) list[i] = sorted[i];
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("New species could not be placed in the Nursery's alphabetical list: " + ex.GetBaseException().Message);
            }
        }
    }
}
