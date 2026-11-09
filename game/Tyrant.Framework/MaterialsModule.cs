using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Mod material settings, iridescence and fur masks per animal, at draw time and never saved: a transpiler on
    /// AnimalMaterials.CalculateKeywords adds ExtraFeatures (the shader variant with iridescence) when the animal's strength or
    /// baby strength is above 0 and the fur variant when a block grows fur ("fur.grow", "fur.age"), and postfixes on
    /// AnimalVisuals.SetAnimalMaterialVariation and AnimalGrowthManager.UpdateGrowth (the two places the game writes
    /// _IridescenceStrength, _FresnelIntensity and _DroopDirection, each a lerp from baby to adult by the integument: see
    /// <see cref="AgeLooks"/>) write the settings, ramp, masks, shaping, strength and grown fur into the property block, read from the
    /// first LOD renderer and written to every LOD like the game's own edit scope. On top of the mods' table lie the in-game
    /// editor's live edits and the park's per-animal looks (<see cref="MaterialTable.MergeLayers"/>). A key Tyrant wrote on a
    /// renderer and no longer sets (the Nursery preview reuses one animal for every skin, the vivarium reuses pooled animals) goes back to the base material's value, or to the game's for the keys the game writes per animal.
    /// A ramp or mask taken from a species ({ "species": id }) is <see cref="SpeciesLooks"/>' copy, the game's own until it is made.
    /// </summary>
    internal static class MaterialsModule
    {
        private const int ExtraFeatures = 8; // AnimalMaterialKeywords.ExtraFeatures
        private const string RampProperty = "_IridescenceTexture";
        private const string MaskProperty = "_IridescenceMask";
        private const string StrengthProperty = "_IridescenceStrength";
        private const string DroopProperty = "_DroopDirection"; // AnimalShaderData.FuzzDroopinessDirectionID
        private const string AdultFurProperty = "_AdultFurMask";
        private const string InfantFurProperty = "_InfantFurMask";
        private const string FuzzProperty = "_FresnelIntensity"; // AnimalShaderData.FresnelIntensityID: only a rim sheen (fur length and coverage come from the masks); the game writes 0 without fur maps
        private const string FurMaskEmpty = "FurMaskEmpty"; // AnimalShaderData.FurMaskEmptyName: the game's blank fur mask
        private static readonly string[] Variants = { "normal", "albino", "melanistic", "leucistic" };

        private static MaterialTable _table = MaterialTable.Build(Array.Empty<LoadedMod>(), out _);
        private static List<LoadedMod> _mods = new List<LoadedMod>();
        private static bool _vanillaLooksSkipped;
        private static bool _active;

        /// <summary>Material names each species / skin / vanilla skin block sets, with the mod that set each (later in load order wins).</summary>
        private static readonly Dictionary<string, Dictionary<string, string>> SpeciesBlocks = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase); // species ids in any case, like the table
        private static readonly Dictionary<string, Dictionary<string, string>> SkinBlocks = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<string, string>> VanillaBlocks = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<(string Id, string Directory)> ModDirectories = new List<(string, string)>();
        private static readonly Dictionary<string, string> CustomSpecies = new Dictionary<string, string>(StringComparer.Ordinal); // species key -> its mod

        // The editor's unsaved edits and the park's per-animal looks (their files resolved, per animal, while its look is the same object).
        private static readonly LiveLooks Live = new LiveLooks();
        private static readonly PreparedLooks Saved = new PreparedLooks(Prepared);
        private static WeakReference? _previewAnimal; // SetPreviewLook's animal (a Nursery preview, gone when the Nursery is)
        private static string? _previewMaterial;
        private static MaterialOverride? _previewBlock;

        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        private static readonly List<Renderer> Renderers = new List<Renderer>();
        private static readonly HashSet<string> Announced = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<(string Kind, string Owner, string Name)> Reported = new HashSet<(string, string, string)>();

        // Textures by ramp and mask object: the pool gives equal content one object (a slider move or a reload builds nothing new
        // for what did not change), and the sweep destroys the textures of objects no table, live or saved block uses any more.
        private static readonly MaterialSourcePool Pool = new MaterialSourcePool(FileStamp);
        private static readonly Dictionary<RampSource, Texture2D?> Ramps = new Dictionary<RampSource, Texture2D?>(ReferenceComparer.Instance);
        private static readonly Dictionary<MaskSource, Texture?> FileMasks = new Dictionary<MaskSource, Texture?>(ReferenceComparer.Instance);
        private static readonly Dictionary<(MaskSource Mask, string Skin, bool Male), Texture?> SlotMasks = new Dictionary<(MaskSource, string, bool), Texture?>();
        private static readonly Dictionary<(MaskSource Mask, int Texture), Texture?> GameMasks = new Dictionary<(MaskSource, int), Texture?>();
        private static bool _sweepDue;

        /// <summary>Per first LOD renderer (it outlives a pooled vivarium animal): the property names Tyrant set there.</summary>
        private static readonly ConditionalWeakTable<Renderer, Written> WrittenTo = new ConditionalWeakTable<Renderer, Written>();
        private static readonly Dictionary<string, Kind> Writing = new Dictionary<string, Kind>(StringComparer.Ordinal);
        private static bool _wrote; // until Tyrant writes anything, an animal no mod covers costs no renderer lookup
        private static Enum? _materialFlag;
        private static bool _materialFlagRead;
        private static readonly Dictionary<Type, Members> MembersByType = new Dictionary<Type, Members>();
        private static readonly Dictionary<(Type, string), FieldInfo?> Fields = new Dictionary<(Type, string), FieldInfo?>();
        private static readonly Dictionary<Type, PropertyInfo?> GrowthAnimal = new Dictionary<Type, PropertyInfo?>();
        private static MethodInfo? _autoSetMaterial;
        private static bool _autoSetMaterialRead;
        private static (int Off, int Fur, int Extra)? _furKeywords; // AnimalMaterialKeywords' TessellationOff, TessellationFur, ExtraFeatures
        private static bool _furKeywordsRead;
        private static PropertyInfo? _tessellation;
        private static bool _tessellationRead;
        private static readonly Dictionary<Type, MethodInfo?> KeyValid = new Dictionary<Type, MethodInfo?>();

        /// <summary>The mods whose materials are in the table, in load order (Vanilla Looks last once it exists).</summary>
        public static IReadOnlyList<LoadedMod> Mods => _mods;

        public static void Start(IReadOnlyList<LoadedMod> mods)
        {
            _mods = mods.ToList();
            // There at start but not loading (turned off or broken): a reload must not bring it in behind the player's back.
            _vanillaLooksSkipped = File.Exists(VanillaLooksManifest) && _mods.All(m => m.Manifest.Id != ModCatalog.VanillaLooksId);
            Build();
        }

        /// <summary>
        /// Re-reads every mod's material folders (a Vanilla Looks created since the start joins, last), rebuilds the table and
        /// re-applies every loaded animal; textures nothing uses any more are destroyed on the next frame.
        /// </summary>
        public static void Reload(IReadOnlyList<LoadedMod> mods)
        {
            var list = mods.ToList();
            foreach (var mod in list)
            {
                var problems = new List<string>();
                MaterialFolders.Load(mod.Manifest, mod.Directory, problems);
                foreach (var problem in problems) FrameworkMod.Log.Warning($"{mod.Manifest.Id}: {problem}");
            }
            AddVanillaLooks(list);
            _mods = list;
            Build();
            _sweepDue = true;
            foreach (var animal in LoadedAnimals()) Reapply(animal, keywords: true);
        }

        /// <summary>The editor's unsaved block for one scope, key and material (null clears); file paths are full, or relative to UserData/Tyrant.</summary>
        public static void SetLive(LookScope scope, string key, string materialName, MaterialOverride? draft)
        {
            Live.Set(scope, key, materialName, draft == null || draft.IsEmpty ? null : Prepared(draft));
            _sweepDue = true;
        }

        /// <summary>
        /// The Nursery editor's Next created draft, on its preview animal (null clears): the preview has no persistent id, so
        /// the draft is kept by the object, at the live animal layer. Only one at a time; file paths as for <see cref="SetLive"/>.
        /// </summary>
        public static void SetPreviewLook(object? animal, string? materialName, MaterialOverride? draft)
        {
            var clear = animal == null || materialName == null || draft == null || draft.IsEmpty;
            _previewAnimal = clear ? null : new WeakReference(animal);
            _previewMaterial = clear ? null : materialName;
            _previewBlock = clear ? null : Prepared(draft!);
            _sweepDue = true;
        }

        /// <summary>
        /// The park's per-animal looks (null: none), prepared at once (call it again after changing the store): looks that did
        /// not change keep their ramps and masks through the sweep. File paths are full, or relative to UserData/Tyrant.
        /// </summary>
        public static void SetAnimalLooks(AnimalLooks? looks)
        {
            Saved.Use(looks);
            _sweepDue = true;
        }

        /// <summary>
        /// Once a frame: a species whose look finished loading (<see cref="SpeciesLooks"/>) is shown on every animal at once (its
        /// references showed the game's own until now); then the textures of ramps and masks no block uses any more are destroyed.
        /// </summary>
        public static void Tick()
        {
            try
            {
                if (SpeciesLooks.Tick().Count > 0 && _active) ReapplyAll();
            }
            catch (Exception ex)
            {
                Failed(ex);
            }
            if (!_sweepDue) return;
            _sweepDue = false;
            try
            {
                Sweep();
            }
            catch (Exception ex)
            {
                Failed(ex);
            }
        }

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            _active = Hook(harmony, "PrehistoricKingdom.AnimalMaterials", m => m.Name == "CalculateKeywords" && m.IsStatic && m.GetParameters().Length == 2, nameof(KeywordTranspiler), transpiler: true)
                && Hook(harmony, "PrehistoricKingdom.AnimalVisuals", m => m.Name == "SetAnimalMaterialVariation", nameof(AfterVariation), transpiler: false)
                && Hook(harmony, "PrehistoricKingdom.AnimalGrowthManager", m => m.Name == "UpdateGrowth" && m.GetParameters().Length == 1, nameof(AfterGrowth), transpiler: false);
            if (_active) HookMaterialKeywords(harmony);
        }

        /// <summary>
        /// A postfix on AnimalMaterials.ApplyKeywordsToMaterial (it sets a new shared material's keywords). Without it grown fur
        /// still shows, but on a furless species it ignores every mask: that is said once in the log.
        /// </summary>
        private static void HookMaterialKeywords(HarmonyLib.Harmony harmony)
        {
            try
            {
                var type = AccessTools.TypeByName("PrehistoricKingdom.AnimalMaterials");
                var method = type == null ? null : AccessTools.Method(type, "ApplyKeywordsToMaterial");
                if (method != null && method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType == typeof(Material))
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(MaterialsModule), nameof(AfterMaterialKeywords)));
                    return;
                }
                FrameworkMod.Log.Warning("AnimalMaterials.ApplyKeywordsToMaterial was not found (game updated?); on a furless species, grown fur ignores fur masks (it covers the whole body) and fur ages show no difference.");
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"AnimalMaterials.ApplyKeywordsToMaterial could not be hooked ({ex.GetBaseException().Message}); on a furless species, grown fur ignores fur masks.");
            }
        }

        /// <summary>
        /// After the game set a new shared material's keywords: a fur variant made from a furless template (_FURMASKING_NONE,
        /// which reads no mask) switches to texture masking, so Grow fur and fur ages obey the fur masks, and its strand noise
        /// tiling of 1, 1 becomes a furry skin's (<see cref="FurMasking"/>). Any other masking mode (furry species' texture
        /// masking, a vertex colour one) is left alone; the material is the game's per-variant copy, never its asset.
        /// </summary>
        private static void AfterMaterialKeywords(object[] __args)
        {
            if (!_active || __args.Length < 1 || !(__args[0] is Material material) || material == null) return;
            try
            {
                if (!FurMasking.NeedsTextureMasking(material.IsKeywordEnabled("_FEATURETESSELLATION_FUR"), material.IsKeywordEnabled(FurMasking.None))) return;
                material.DisableKeyword(FurMasking.None);
                material.DisableKeyword(FurMasking.VertexColor);
                material.EnableKeyword(FurMasking.Texture);
                if (material.HasProperty(FurMasking.DispTexScale))
                {
                    var scale = material.GetVector(FurMasking.DispTexScale);
                    if (FurMasking.DispScale(scale.x, scale.y) is { } tiled)
                        material.SetVector(FurMasking.DispTexScale, new Vector4(tiled.X, tiled.Y, scale.z, scale.w));
                }
                if (Announced.Add("masking|" + material.name)) FrameworkMod.Log.Msg($"fur masks on for {material.name} (its species has no fur of its own)");
            }
            catch (Exception ex)
            {
                Failed(ex);
            }
        }

        private static void Build()
        {
            _table = MaterialTable.Build(_mods, out var messages);
            foreach (var message in messages) FrameworkMod.Log.Warning(message);
            SpeciesBlocks.Clear();
            SkinBlocks.Clear();
            VanillaBlocks.Clear();
            ModDirectories.Clear();
            CustomSpecies.Clear();
            foreach (var mod in _mods)
            {
                var id = mod.Manifest.Id;
                ModDirectories.Add((id, mod.Directory));
                foreach (var species in mod.Manifest.Species) CustomSpecies[species.Key(id)] = id;
                foreach (var species in mod.Manifest.SpeciesMaterials)
                    Note(SpeciesBlocks, species.Key, species.Value.Keys, id);
                foreach (var skin in mod.Manifest.VanillaSkinMaterials)
                    Note(VanillaBlocks, skin.Key, skin.Value.Keys, id);
                foreach (var skin in mod.Manifest.Skins)
                    if (skin.Materials != null) Note(SkinBlocks, skin.Key(id), skin.Materials.Keys, id);
            }
            foreach (var block in _table.Blocks) Pool.Intern(block);
        }

        private static string VanillaLooksManifest => Path.Combine(FrameworkMod.ModsDirectory, ModCatalog.VanillaLooksId, ModManifest.FileName);

        /// <summary>The Vanilla Looks mod the editor created since the start: appended last, as ModCatalog.Plan orders it.</summary>
        private static void AddVanillaLooks(List<LoadedMod> mods)
        {
            if (_vanillaLooksSkipped || mods.Any(m => m.Manifest.Id == ModCatalog.VanillaLooksId) || !File.Exists(VanillaLooksManifest)) return;
            try
            {
                var directory = Path.GetDirectoryName(VanillaLooksManifest)!;
                var manifest = ModManifest.Parse(File.ReadAllText(VanillaLooksManifest));
                if (manifest.Id != ModCatalog.VanillaLooksId) return;
                var problems = new List<string>();
                MaterialFolders.Load(manifest, directory, problems);
                foreach (var problem in problems) FrameworkMod.Log.Warning($"{manifest.Id}: {problem}");
                mods.Add(new LoadedMod(manifest, directory));
            }
            catch (Exception ex) when (ex is ManifestException || ex is IOException || ex is UnauthorizedAccessException)
            {
                FrameworkMod.Log.Warning($"{ModCatalog.VanillaLooksId}: \"{VanillaLooksManifest}\" could not be read ({ex.Message}); looks saved there show after a restart.");
            }
        }

        private static bool Hook(HarmonyLib.Harmony harmony, string typeName, Func<MethodInfo, bool> match, string patch, bool transpiler)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                var methods = type == null ? Array.Empty<MethodInfo>() : AccessTools.GetDeclaredMethods(type).Where(match).ToArray();
                if (methods.Length == 0)
                {
                    FrameworkMod.Log.Warning($"{typeName}: no method for mod materials was found (game updated?); mod materials and iridescence are off.");
                    return false;
                }
                var harmonyMethod = new HarmonyMethod(typeof(MaterialsModule), patch);
                foreach (var method in methods)
                {
                    if (transpiler) harmony.Patch(method, transpiler: harmonyMethod);
                    else harmony.Patch(method, postfix: harmonyMethod);
                }
                return true;
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"{typeName}: mod materials could not be hooked; mod materials and iridescence are off: {ex.GetBaseException().Message}");
                return false;
            }
        }

        // ---- hooks ----

        /// <summary>Before every return of CalculateKeywords: keywords = AddKeywords(keywords, animal).</summary>
        private static IEnumerable<CodeInstruction> KeywordTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var add = AccessTools.Method(typeof(MaterialsModule), nameof(AddKeywords));
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    var first = new CodeInstruction(OpCodes.Ldarg_0);
                    first.labels.AddRange(instruction.labels); // jumps to the return now run our code first
                    instruction.labels.Clear();
                    first.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                    yield return first;
                    yield return new CodeInstruction(OpCodes.Call, add);
                }
                yield return instruction;
            }
        }

        /// <summary>
        /// Adds ExtraFeatures when the animal's iridescence strength for its variant, or its baby strength, is above 0 (so a baby
        /// shimmers on a skin the game gives none). When a block grows fur (grow or an age) and the game's tessellation is on,
        /// gives the fur variant as the game gives a skin with a fur map (TessellationFur and ExtraFeatures in place of
        /// TessellationOff). Never removes another keyword.
        /// </summary>
        private static int AddKeywords(int keywords, object animal)
        {
            if (!_active || animal == null) return keywords;
            try
            {
                var found = Resolve(animal, MembersOf(animal.GetType()));
                if (found == null) return keywords;
                if (AgeLooks.Shimmers(found.Override.Iridescence, SkinTypeOf(found.Members, animal))) keywords |= ExtraFeatures;
                if (AgeLooks.NeedsFurVariant(found.Override.Fur)) keywords = WithFur(keywords);
            }
            catch (Exception ex)
            {
                Failed(ex);
            }
            return keywords;
        }

        /// <summary>The fur variant's keywords (AnimalMaterials.CalculateKeywords with a fur map); unchanged while tessellation is off.</summary>
        private static int WithFur(int keywords)
        {
            var flags = FurKeywords();
            if (flags == null) return keywords;
            var tessellation = TessellationOn;
            if (tessellation != true)
            {
                if (tessellation == false && Announced.Add("tessellation"))
                    FrameworkMod.Log.Warning("Grow fur: fur needs the game's tessellated fur (the Fur graphics setting); grown fur shows once it is on.");
                return keywords;
            }
            var (off, fur, extra) = flags.Value;
            return (keywords & ~off) | fur | extra;
        }

        /// <summary>
        /// FurManager.EnableTessellation, which the game's Fur graphics setting sets (PKQualityFur.ApplyQuality); without it the
        /// game gives no animal the tessellated fur variant. Null when it cannot be read.
        /// </summary>
        public static bool? TessellationOn
        {
            get
            {
                if (!_tessellationRead)
                {
                    _tessellationRead = true;
                    var type = AccessTools.TypeByName("FurManager");
                    _tessellation = type == null ? null : AccessTools.Property(type, "EnableTessellation");
                    if (_tessellation == null) FrameworkMod.Log.Warning("FurManager.EnableTessellation was not found (game updated?); Grow fur is off.");
                }
                try
                {
                    return _tessellation?.GetValue(null, null) is bool on ? on : (bool?)null;
                }
                catch (Exception ex)
                {
                    Failed(ex);
                    return null;
                }
            }
        }

        /// <summary>AnimalMaterialKeywords' TessellationOff, TessellationFur and ExtraFeatures, read by name once; null when one is missing.</summary>
        private static (int Off, int Fur, int Extra)? FurKeywords()
        {
            if (_furKeywordsRead) return _furKeywords;
            _furKeywordsRead = true;
            var type = AccessTools.TypeByName("PrehistoricKingdom.AnimalMaterialKeywords");
            try
            {
                if (type != null && type.IsEnum)
                    _furKeywords = (Flag(type, "TessellationOff"), Flag(type, "TessellationFur"), Flag(type, "ExtraFeatures"));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException)
            {
                _furKeywords = null;
            }
            if (_furKeywords == null)
                FrameworkMod.Log.Warning("AnimalMaterialKeywords has no TessellationOff, TessellationFur or ExtraFeatures (game updated?); Grow fur is off.");
            return _furKeywords;
        }

        private static int Flag(Type type, string name) => Convert.ToInt32(Enum.Parse(type, name), System.Globalization.CultureInfo.InvariantCulture);

        // __args[0]: the animal (the extension method's 'this IAnimal'). The game returns early, writing nothing, for an animal
        // that is not visually present: then neither does Tyrant.
        private static void AfterVariation(object[] __args)
        {
            var animal = __args.Length > 0 ? __args[0] : null;
            if (!_active || animal == null) return;
            try
            {
                if (!IsVisuallyPresent(MembersOf(animal.GetType()), animal)) return;
            }
            catch (Exception ex)
            {
                Failed(ex);
                return;
            }
            Apply(animal, fromHook: true);
        }

        /// <summary>
        /// After a Tyrant skin's PNGs went onto the animal (AnimalTexturePatch, which runs after the game's variation hook): the
        /// look is written again so its fur masks win over the skin's. As the editor's write (not the hook's), so the game's
        /// values recorded by the hook are kept, not replaced by Tyrant's own still in the block.
        /// </summary>
        public static void AfterSkinTextures(object animal) => Reapply(animal, keywords: false);

        // __args[0]: the AnimalGrowthFlags; only a Material update writes _IridescenceStrength (the rest is bones and shapes).
        private static void AfterGrowth(object __instance, object[] __args)
        {
            if (!_active || __instance == null || (__args.Length > 0 && !UpdatesMaterial(__args[0]))) return;
            object? animal;
            try
            {
                var type = __instance.GetType();
                if (!GrowthAnimal.TryGetValue(type, out var property)) GrowthAnimal[type] = property = AccessTools.Property(type, "Animal");
                animal = property?.GetValue(__instance, null);
            }
            catch (Exception ex)
            {
                Failed(ex);
                return;
            }
            Apply(animal, fromHook: true);
        }

        // ---- the editor's API ----

        /// <summary>
        /// Writes the animal's look again after an edit. With <paramref name="keywords"/> it first lets the game pick the shader
        /// variant again (AnimalVisuals.AutoSetSharedMaterialVariant, which leaves a blocked Nursery preview alone), so turning
        /// iridescence on or off shows.
        /// </summary>
        public static void Reapply(object animal, bool keywords)
        {
            if (!_active || animal == null) return;
            try
            {
                if (!IsVisuallyPresent(MembersOf(animal.GetType()), animal)) return; // its own setup runs the hooks when it shows
                if (keywords && AutoSetMaterial() is MethodInfo autoSet)
                {
                    try
                    {
                        autoSet.Invoke(null, new[] { animal });
                    }
                    catch (Exception ex)
                    {
                        Failed(ex); // e.g. its asset is not loaded (AddUpdateAnimal throws): its block is still written below
                    }
                }
                ApplyTo(animal, fromHook: false);
            }
            catch (Exception ex)
            {
                Failed(ex);
            }
        }

        /// <summary>
        /// Every loaded animal, the Nursery's preview animal (a saved look shows on it with the editor closed) and the one showing
        /// a Next created draft, written again (no keyword change).
        /// </summary>
        private static void ReapplyAll()
        {
            foreach (var animal in LoadedAnimals()) Reapply(animal, keywords: false);
            var nursery = LookEditorWindow.NurseryAnimal();
            if (nursery != null) Reapply(nursery, keywords: false);
            if (_previewAnimal?.Target is object preview && !ReferenceEquals(preview, nursery)) Reapply(preview, keywords: false);
        }

        /// <summary>The selected animal first, then every loaded animal (park and vivarium) the edit of this scope also reaches.</summary>
        public static IEnumerable<object> AnimalsMatching(LookScope scope, object selected)
        {
            var result = new List<object> { selected };
            var context = Describe(selected);
            if (context == null || scope == LookScope.Animal || (scope == LookScope.Skin && context.SkinKey == null)) return result;
            foreach (var animal in LoadedAnimals())
            {
                if (ReferenceEquals(animal, selected) || !(Describe(animal) is AnimalContext other)) continue;
                if (scope == LookScope.Species ? other.Species == context.Species : string.Equals(other.SkinKey, context.SkinKey, StringComparison.OrdinalIgnoreCase))
                    result.Add(animal);
            }
            return result;
        }

        /// <summary>The mods' table (for <see cref="SaveTargets.For"/> and an owner's own block).</summary>
        public static MaterialTable Table => _table;

        /// <summary>True when the hooks are in: without them the editor would change nothing.</summary>
        public static bool Active => _active;

        /// <summary>Re-applies the loaded animals with these ids, e.g. once a park's per-animal looks are read after its animals were set up.</summary>
        public static void ReapplyIds(IReadOnlyCollection<ulong> ids, bool keywords)
        {
            var wanted = new HashSet<ulong>(ids);
            var animals = new List<object>();
            ReadMaps(map =>
            {
                foreach (DictionaryEntry entry in map)
                    if (entry.Value != null && FieldOf(entry.Key, "uniqueID")?.GetValue(entry.Key) is ulong id && wanted.Contains(id)) animals.Add(entry.Value);
            });
            foreach (var animal in animals) Reapply(animal, keywords); // after the read lock is released
        }

        /// <summary>
        /// What lies under one scope's own block for this animal (<see cref="MaterialTable.MergeBelow"/>): the values the editor
        /// shows when its draft sets none. Null when no layer below sets anything (then the game's own values show).
        /// </summary>
        public static MaterialOverride? Below(object animal, LookScope scope)
        {
            if (animal == null) return null;
            try
            {
                return Resolve(animal, MembersOf(animal.GetType()), scope)?.Override;
            }
            catch (Exception ex)
            {
                Failed(ex);
                return null;
            }
        }

        /// <summary>
        /// A VirtualAnimal (incubating, held, being placed) as <see cref="Describe"/> keys a placed one: its BaseAnimalData's
        /// speciesID, the skin key of its skinsData[skinIdx] (a mod skin's, else the vanilla skin's), its sex, its UniqueID and
        /// OwnName; null when the species cannot be read.
        /// </summary>
        public static (string Species, string? SkinKey, string? Sex, ulong Id, string? Name)? DescribeVirtual(object virtualAnimal)
        {
            if (virtualAnimal == null) return null;
            var data = FieldOf(virtualAnimal, "OwnAD")?.GetValue(virtualAnimal);
            if (data == null || !(FieldOf(data, "speciesID")?.GetValue(data) is string species)) return null;
            var skins = FieldOf(data, "skinsData")?.GetValue(data) as IList;
            var index = FieldOf(virtualAnimal, "skinIdx")?.GetValue(virtualAnimal) is int i ? i : -1;
            var skinData = skins != null && index >= 0 && index < skins.Count ? skins[index] : null;
            var skinKey = SkinsModule.TryGet(skinData, out var modId, out var entry, out _) ? modId + "/" + entry.Id : VanillaKeyOf(skinData, species);
            var sex = FieldOf(virtualAnimal, "Sex")?.GetValue(virtualAnimal) is Enum s ? (s.ToString() == "Male" ? "male" : "female") : null;
            var type = virtualAnimal.GetType();
            if (!VirtualIdOf.TryGetValue(type, out var idProperty)) VirtualIdOf[type] = idProperty = AccessTools.Property(type, "UniqueID");
            var id = idProperty?.GetValue(virtualAnimal, null) is ulong unique ? unique : 0UL;
            return (species, skinKey, sex, id, FieldOf(virtualAnimal, "OwnName")?.GetValue(virtualAnimal) as string);
        }

        private static readonly Dictionary<Type, PropertyInfo?> VirtualIdOf = new Dictionary<Type, PropertyInfo?>();

        /// <summary>The animal's species, skin, variant, id, material and the skin's source; null when it has no species or material.</summary>
        public static AnimalContext? Describe(object animal)
        {
            if (animal == null) return null;
            try
            {
                var members = MembersOf(animal.GetType());
                var species = SpeciesOf(members, animal);
                if (species == null || !(members.BaseMaterial?.GetValue(animal, null) is Material material) || material == null) return null;
                var skinData = members.SkinData?.GetValue(animal, null);
                var own = SkinsModule.TryGet(skinData, out var modId, out var entry, out _);
                var vanillaKey = own ? null : VanillaKeyOf(skinData, species);
                var modName = own ? _mods.FirstOrDefault(m => m.Manifest.Id == modId)?.Manifest.Name : null;
                var source = !own ? "vanilla" : string.IsNullOrEmpty(modName) ? modId : modName!;
                var custom = CustomSpecies.TryGetValue(species, out var speciesMod) ? speciesMod : null;
                var skinType = SkinTypeOf(members, animal);
                return new AnimalContext(species, own ? (modId, entry.Id) : ((string, string)?)null, vanillaKey,
                    Variants[skinType >= 0 && skinType < Variants.Length ? skinType : 0], IdOf(members, animal), material.name, source, custom,
                    members.Sex == null ? null : IsMale(members, animal) ? "male" : "female");
            }
            catch (Exception ex)
            {
                Failed(ex);
                return null;
            }
        }

        /// <summary>
        /// The mask the animal shows now (for export): Tyrant's when a block sets one, else the game's (its fur map, or the
        /// material's iridescence mask). The infant fur mask falls back to the adult one Tyrant sets. Null when there is none,
        /// and also when the mask is not a texture to read: <paramref name="white"/> is the size of an all-white mask to write
        /// instead while Grow fur paints Unity's white texture over the whole body (the game's own under it is its blank
        /// FurMaskEmpty), and for an iridescence mask that is everywhere or none at all (<see cref="MaskExports.IridescenceMode"/>);
        /// with <paramref name="black"/> that mask is all black instead, the same size (a fur slot the fur age leaves bare);
        /// <paramref name="species"/> names the species whose iridescence mask the animal shows by reference
        /// (the game's images are never exported).
        /// </summary>
        public static Texture? EffectiveMask(object animal, bool fur, bool infant, out Vector2Int? white, out bool black, out string? species)
        {
            white = null;
            black = false;
            species = null;
            if (animal == null) return null;
            try
            {
                var members = MembersOf(animal.GetType());
                var found = Resolve(animal, members);
                if (!Lods(members, animal)) return null;
                var first = Renderers[0];
                first.GetPropertyBlock(Block);
                WrittenTo.TryGetValue(first, out var record);
                var material = members.BaseMaterial?.GetValue(animal, null) as Material;
                if (fur)
                {
                    var property = infant ? InfantFurProperty : AdultFurProperty;
                    // An age the fur age leaves bare shows Tyrant's black, over any mask PNG.
                    if (record != null && record.Ours.TryGetValue(property, out var bare) && bare == _noFur && Block.GetTexture(property) == bare)
                    {
                        white = PaintSize(record, material, infant); // a full-size black image to paint fur back on, not the 4 × 4 texture
                        black = true;
                        return null;
                    }
                    var source = found?.Override.Fur is FurMasks masks ? infant ? masks.InfantMask ?? masks.Mask : masks.Mask : null;
                    if (source != null && FileMask(source) is Texture own && own != null) return own;
                    if (record != null && record.Ours.TryGetValue(property, out var ours) && ours == Texture2D.whiteTexture && Block.GetTexture(property) == ours)
                    {
                        white = PaintSize(record, material, infant);
                        return null;
                    }
                    return Present(GameTexture(record, property)) ?? MaterialTexture(material, property);
                }
                if (found?.Override.Iridescence?.Mask is MaskSource { Species: { } taken })
                {
                    species = taken;
                    return null;
                }
                var shown = found?.Override.Iridescence?.Mask;
                var texture = shown != null && !shown.Everywhere && MaskTexture(shown, found!, IsMale(members, animal), record) is Texture made && made != null
                    ? made
                    : MaterialTexture(material, MaskProperty);
                switch (MaskExports.IridescenceMode(shown, texture != null))
                {
                    case MaskExportMode.Image:
                        return texture;
                    case MaskExportMode.White: // everywhere, or no mask at all: a white mask the size of the skin's mask or diffuse map
                        var game = Present(GameTexture(record, MaskProperty)) ?? MaterialTexture(material, MaskProperty);
                        white = game != null && game.width >= 64 && game.height >= 64 ? new Vector2Int(game.width, game.height) : PaintSize(record, material, infant: false);
                        return null;
                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                Failed(ex);
                return null;
            }
        }

        /// <summary>A size to paint a whole-body mask at: the animal's diffuse map's (the block's, else the material's), else 1024 × 1024.</summary>
        private static Vector2Int PaintSize(Written? record, Material? material, bool infant)
        {
            var property = SkinSlotNames.Property(infant ? "infantDiffuse" : "diffuse");
            var diffuse = Present(GameTexture(record, property)) ?? MaterialTexture(material, property);
            return diffuse != null && diffuse.width >= 64 && diffuse.height >= 64 ? new Vector2Int(diffuse.width, diffuse.height) : new Vector2Int(1024, 1024);
        }

        // ---- applying ----

        private static void Apply(object? animal, bool fromHook)
        {
            if (!_active || animal == null) return;
            try
            {
                ApplyTo(animal, fromHook);
            }
            catch (Exception ex)
            {
                Failed(ex);
            }
        }

        /// <param name="fromHook">True right after the game wrote its per-animal values (strength, droop); false for an editor re-apply.</param>
        private static void ApplyTo(object animal, bool fromHook)
        {
            var members = MembersOf(animal.GetType());
            var found = Resolve(animal, members);
            if (found == null)
            {
                if (_wrote) Forget(members, animal, fromHook);
                return;
            }
            if (!Lods(members, animal)) return;
            var o = found.Override;
            var i = o.Iridescence;
            var first = Renderers[0];
            first.GetPropertyBlock(Block);
            WrittenTo.TryGetValue(first, out var before);
            var record = before ?? new Written();

            // Textures first (a failure is reported and leaves that one out); a mask from the game's map reads the block.
            var male = IsMale(members, animal);
            // "game" (the material's own) writes nothing: a key Tyrant wrote before goes back to the material's value. So does a
            // species' ramp or mask until its copy is made (SpeciesLooks loads it; the animal is written again then) or when it fails.
            var ramp = i?.Ramp == null || i.Ramp.Game ? null : i.Ramp.Species != null ? SpeciesLooks.Ramp(i.Ramp.Species) : RampTexture(i.Ramp);
            var mask = i?.Mask == null || i.Mask.Game ? null : MaskTexture(i.Mask, found, male, record);
            var adultFur = o.Fur?.Mask == null || o.Fur.Mask.Game ? null : FileMask(o.Fur.Mask); // "game": the game's own per-animal mask stays
            var infantFur = o.Fur?.InfantMask == null ? adultFur : o.Fur.InfantMask.Game ? null : FileMask(o.Fur.InfantMask); // one file covers both ages

            Writing.Clear();
            foreach (var setting in o.Settings)
            {
                var v = setting.Value;
                if (v.Kind == MaterialValueKind.Color) SetColor(setting.Key, new Color(v.Color.R, v.Color.G, v.Color.B, 1f)); // sRGB; Unity converts
                else if (v.Kind == MaterialValueKind.Vector)
                {
                    var vector = new Vector4(v.Vector[0], v.Vector[1], v.Vector[2], v.Vector[3]);
                    if (setting.Key == DroopProperty) SetGameVector(record, DroopProperty, vector, fromHook); // the game writes its own lerp per animal
                    else SetVector(setting.Key, vector);
                }
                else SetFloat(setting.Key, v.Number);
            }
            if (i != null)
            {
                if (i.Scale is float scale) SetFloat("_IridescenceScale", scale);
                if (i.Power is float power) SetFloat("_IridescencePower", power);
                if (i.Bias is float bias) SetFloat("_IridescenceBias", bias);
                if (i.Tiling != null || i.Offset != null)
                    SetVector("_IridescenceMask_ST", new Vector4(i.Tiling?[0] ?? 1, i.Tiling?[1] ?? 1, i.Offset?[0] ?? 0, i.Offset?[1] ?? 0));
                if (ramp != null) SetTexture(RampProperty, ramp);
                if (mask != null) SetTexture(MaskProperty, mask);
                if (AgeLooks.WritesStrength(i))
                {
                    // From the baby strength (0 when left out, the game's own) to the adult one (Tyrant's, else the game's), by
                    // the integument the game wrote just before; both vary between animals alike.
                    var variation = MaterialTable.EffectiveVariation(i);
                    var id = IdOf(members, animal);
                    var adult = i.Strength != null ? MaterialStrength.For(i.Strength.For(SkinTypeOf(members, animal)), variation, id) : GameStrength(members, animal);
                    var baby = MaterialStrength.For(i.BabyStrength ?? 0, variation, id);
                    SetGameFloat(record, StrengthProperty, AgeLooks.Strength(baby, adult, Block.GetFloat("_IntegumentMaturity")), fromHook);
                }
            }
            // Fur, through the masks (the shader's fur height is lerp(infant mask R, adult mask R, integument) × _Length): an age
            // the fur age leaves bare gets black; an age that should have fur keeps the block's PNG or the game's own mask, else
            // white (fur on the whole body); a "game" fur mask keeps the game's. With grow or an age, the fur sheen is a furry
            // skin's 1 (the game writes 0 without fur maps). The keywords, and texture masking on a furless template, come
            // from AddKeywords and AfterMaterialKeywords.
            var grow = o.Fur?.Grow == true;
            var age = o.Fur?.Age;
            var growing = AgeLooks.NeedsFurVariant(o.Fur);
            var adultGame = o.Fur?.Mask?.Game == true;
            var infantGame = o.Fur?.InfantMask?.Game ?? adultGame;
            if (AgeLooks.WritesFurSheen(o.Fur)) SetGameFloat(record, FuzzProperty, 1f, fromHook);
            FurSlot(record, AdultFurProperty, AgeLooks.FurSlot(age, grow, infant: false, adultFur != null, adultGame,
                growing && GameHasFur(members, animal, record, AdultFurProperty, "adultFurMap")), adultFur);
            FurSlot(record, InfantFurProperty, AgeLooks.FurSlot(age, grow, infant: true, infantFur != null, infantGame,
                growing && GameHasFur(members, animal, record, InfantFurProperty, "infantFurMap")), infantFur);

            Restore(record, found.Material, Writing, fromHook);
            foreach (var renderer in Renderers) renderer.SetPropertyBlock(Block);
            Remember(first, before, record);

            var skin = found.SkinKey ?? found.VanillaKey;
            if (Reported.Add(("on", skin ?? found.Species, "")))
                FrameworkMod.Log.Msg($"materials on {found.Species}{(skin == null ? "" : ", skin " + skin)} ({(found.ModId.Length > 0 ? found.ModId : "in-game look")})");
        }

        /// <summary>Writes one fur slot as <see cref="AgeLooks.FurSlot"/> decided; Leave writes nothing (a key Tyrant wrote before goes back).</summary>
        private static void FurSlot(Written record, string property, FurFill fill, Texture? file)
        {
            switch (fill)
            {
                case FurFill.File:
                    SetGameTexture(record, property, file!);
                    break;
                case FurFill.White:
                    SetGameTexture(record, property, Texture2D.whiteTexture);
                    break;
                case FurFill.Black:
                    SetGameTexture(record, property, NoFur);
                    break;
            }
        }

        private static Texture2D? _noFur;

        /// <summary>Tyrant's own all-black 4 × 4 fur mask: no fur at that age (the game's FurMaskEmpty does the same; no game image is used).</summary>
        private static Texture2D NoFur
        {
            get
            {
                if (_noFur != null) return _noFur;
                _noFur = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "Tyrant no fur", hideFlags = HideFlags.DontUnloadUnusedAsset };
                _noFur.SetPixels32(new Color32[16]); // all 0: black, no fur
                _noFur.Apply(false, true);
                return _noFur;
            }
        }

        private enum Kind
        {
            Float,
            Color,
            Vector,
            Texture,
            GameFloat, // the game writes it per animal just before each hook: put back from the game's value
            GameVector,
            GameTexture, // the game writes it when the skin's maps load: put back from the game's texture under Tyrant's
        }

        private sealed class Written
        {
            public readonly Dictionary<string, Kind> Keys = new Dictionary<string, Kind>(StringComparer.Ordinal);

            /// <summary>The game's value of a GameFloat (x) or GameVector key, read before Tyrant's went over it.</summary>
            public readonly Dictionary<string, Vector4> Game = new Dictionary<string, Vector4>(StringComparer.Ordinal);

            /// <summary>For a GameTexture key: the texture Tyrant wrote, and the game's under it.</summary>
            public readonly Dictionary<string, Texture> Ours = new Dictionary<string, Texture>(StringComparer.Ordinal);
            public readonly Dictionary<string, Texture?> Under = new Dictionary<string, Texture?>(StringComparer.Ordinal);
        }

        private static void SetFloat(string key, float value)
        {
            Block.SetFloat(key, value);
            Writing[key] = Kind.Float;
        }

        private static void SetColor(string key, Color value)
        {
            Block.SetColor(key, value);
            Writing[key] = Kind.Color;
        }

        private static void SetVector(string key, Vector4 value)
        {
            Block.SetVector(key, value);
            Writing[key] = Kind.Vector;
        }

        private static void SetTexture(string key, Texture value)
        {
            Block.SetTexture(key, value);
            Writing[key] = Kind.Texture;
        }

        // A hook comes right after the game wrote its value; an editor re-apply finds the game's value only before Tyrant's first write.
        private static void SetGameFloat(Written record, string key, float value, bool fromHook)
        {
            if (fromHook || !record.Game.ContainsKey(key)) record.Game[key] = new Vector4(Block.GetFloat(key), 0, 0, 0);
            Block.SetFloat(key, value);
            Writing[key] = Kind.GameFloat;
        }

        private static void SetGameVector(Written record, string key, Vector4 value, bool fromHook)
        {
            if (fromHook || !record.Game.ContainsKey(key)) record.Game[key] = Block.GetVector(key);
            Block.SetVector(key, value);
            Writing[key] = Kind.GameVector;
        }

        // The block no longer holding Tyrant's texture means the game loaded the skin's maps since: that is the game's texture now.
        private static void SetGameTexture(Written record, string key, Texture value)
        {
            var current = Block.GetTexture(key);
            if (!record.Ours.TryGetValue(key, out var ours) || current != ours) record.Under[key] = current;
            Block.SetTexture(key, value);
            record.Ours[key] = value;
            Writing[key] = Kind.GameTexture;
        }

        /// <summary>What this Apply wrote becomes what the next one starts from (nothing written: the renderer is forgotten).</summary>
        private static void Remember(Renderer first, Written? before, Written record)
        {
            if (Writing.Count == 0)
            {
                if (before != null) WrittenTo.Remove(first);
                return;
            }
            if (before == null) WrittenTo.Add(first, record);
            record.Keys.Clear();
            foreach (var pair in Writing) record.Keys[pair.Key] = pair.Value;
            _wrote = true;
        }

        /// <summary>No layer changes the animal's material now: the keys Tyrant wrote on its renderers go back, then they are forgotten.</summary>
        private static void Forget(Members members, object animal, bool fromHook)
        {
            if (!Lods(members, animal) || !WrittenTo.TryGetValue(Renderers[0], out var before)) return;
            WrittenTo.Remove(Renderers[0]);
            Renderers[0].GetPropertyBlock(Block);
            Writing.Clear();
            Restore(before, members.BaseMaterial?.GetValue(animal, null) as Material, null, fromHook);
            foreach (var renderer in Renderers) renderer.SetPropertyBlock(Block);
        }

        /// <summary>
        /// Into <see cref="Block"/>: each key written before and not in <paramref name="keep"/> goes back to the base material's
        /// value, or for the keys the game writes per animal to the game's (after a hook the game's is already there).
        /// </summary>
        private static void Restore(Written before, Material? material, Dictionary<string, Kind>? keep, bool fromHook)
        {
            foreach (var pair in before.Keys)
            {
                if (keep != null && keep.ContainsKey(pair.Key)) continue;
                switch (pair.Value)
                {
                    case Kind.GameFloat:
                        if (!fromHook && before.Game.TryGetValue(pair.Key, out var number)) Block.SetFloat(pair.Key, number.x);
                        before.Game.Remove(pair.Key);
                        continue;
                    case Kind.GameVector:
                        if (!fromHook && before.Game.TryGetValue(pair.Key, out var vector)) Block.SetVector(pair.Key, vector);
                        before.Game.Remove(pair.Key);
                        continue;
                    case Kind.GameTexture:
                        if (before.Ours.TryGetValue(pair.Key, out var ours) && Block.GetTexture(pair.Key) == ours)
                        {
                            var under = before.Under.TryGetValue(pair.Key, out var game) ? Present(game) : null;
                            Block.SetTexture(pair.Key, under ?? MaterialTexture(material, pair.Key) ?? Texture2D.blackTexture); // black: no fur
                        }
                        before.Ours.Remove(pair.Key);
                        before.Under.Remove(pair.Key);
                        continue;
                }
                if (material == null || !material.HasProperty(pair.Key)) continue;
                switch (pair.Value)
                {
                    case Kind.Color:
                        Block.SetColor(pair.Key, material.GetColor(pair.Key));
                        break;
                    case Kind.Vector:
                        Block.SetVector(pair.Key, material.GetVector(pair.Key));
                        break;
                    case Kind.Texture:
                        // A block cannot hold null: no texture of its own is a ramp adding nothing, or a mask showing all.
                        var texture = material.GetTexture(pair.Key);
                        Block.SetTexture(pair.Key, texture != null ? texture : pair.Key == MaskProperty ? Texture2D.whiteTexture : Texture2D.blackTexture);
                        break;
                    default:
                        Block.SetFloat(pair.Key, material.GetFloat(pair.Key));
                        break;
                }
            }
        }

        /// <summary>False when an AnimalGrowthFlags value has no Material flag (read by name once); true when that cannot be told.</summary>
        private static bool UpdatesMaterial(object? flags)
        {
            if (!(flags is Enum value)) return true;
            if (!_materialFlagRead)
            {
                _materialFlagRead = true;
                try
                {
                    _materialFlag = (Enum)Enum.Parse(value.GetType(), "Material");
                }
                catch (Exception ex) when (ex is ArgumentException || ex is OverflowException)
                {
                    FrameworkMod.Log.Warning("AnimalGrowthFlags has no Material flag (game updated?); mod materials are written on every growth update.");
                }
            }
            return _materialFlag == null || value.HasFlag(_materialFlag);
        }

        /// <summary>What the animal wears: null when no layer changes its material.</summary>
        /// <param name="below">For the editor: merge only the layers under this scope's own block.</param>
        private static Resolved? Resolve(object animal, Members members, LookScope? below = null)
        {
            var species = SpeciesOf(members, animal);
            var skinData = members.SkinData?.GetValue(animal, null);
            var own = SkinsModule.TryGet(skinData, out var modId, out var entry, out var directory);
            var inherited = SkinsModule.TryGetInherited(skinData, out var baseModId, out var baseEntry, out var baseDirectory);
            // The vanilla skin's name (a reflection read) only when a vanilla skin block or a live skin edit could use it.
            var skinName = own || skinData == null || !(_table.CoversVanillaSkinsOf(species) || Live.HasSkins)
                ? null : FieldOf(skinData, "skinName")?.GetValue(skinData) as string;
            // Most animals: nothing changes them. Said before the material is read or any result is built; a skin key is built
            // only while live skin edits exist, the id read only while live animal edits or saved looks exist.
            var covered = _table.CoversSpecies(species) || (own && _table.CoversSkin(modId, entry.Id)) || (inherited && _table.CoversSkin(baseModId, baseEntry.Id))
                || (!string.IsNullOrEmpty(skinName) && _table.CoversVanillaSkinsOf(species)) || Live.CoversSpecies(species)
                || (Live.HasSkins && Live.CoversSkin(own ? entry.Key(modId) : string.IsNullOrEmpty(skinName) || species == null ? null : species + "/" + skinName));
            var id = Live.HasAnimals || !Saved.IsEmpty ? IdOf(members, animal) : null;
            // The Nursery preview's Next created draft (it has no id of its own): by the object, while one is set.
            var preview = _previewBlock != null && ReferenceEquals(_previewAnimal?.Target, animal);
            if (!covered && !preview && !(id is ulong known && (Live.CoversAnimal(known) || Saved.Has(known)))) return null;
            var saved = id is ulong savedId ? Saved.Get(savedId) : null;
            if (!(members.BaseMaterial?.GetValue(animal, null) is Material material) || material == null) return null;
            var name = material.name;
            var resolved = new Resolved(members, species ?? "", material);
            if (own) (resolved.SkinKey, resolved.SkinModId, resolved.Entry, resolved.Directory) = (entry.Key(modId), modId, entry, directory);
            if (inherited) (resolved.InheritedKey, resolved.BaseModId, resolved.BaseEntry, resolved.BaseDirectory) = (baseEntry.Key(baseModId), baseModId, baseEntry, baseDirectory);
            if (!string.IsNullOrEmpty(skinName) && species != null) resolved.VanillaKey = species + "/" + skinName;

            ReportOtherNames(SkinBlocks, resolved.SkinKey, name);
            ReportOtherNames(SkinBlocks, resolved.InheritedKey, name);
            ReportOtherNames(VanillaBlocks, resolved.VanillaKey, name);
            ReportOtherNames(SpeciesBlocks, species, name);

            var liveAnimal = preview && string.Equals(_previewMaterial, name, StringComparison.Ordinal) ? _previewBlock
                : id is ulong animalId ? Live.Animal(animalId, name) : null;
            var savedAnimal = saved != null && saved.TryGetValue(name, out var look) ? look : null;
            var liveSkin = Live.Skin(resolved.SkinKey ?? resolved.VanillaKey, name);
            var tableSkin = resolved.SkinKey != null ? _table.Block(MaterialOwnerKind.Skin, resolved.SkinKey, name) : _table.Block(MaterialOwnerKind.VanillaSkin, resolved.VanillaKey, name);
            var baseSkin = _table.Block(MaterialOwnerKind.Skin, resolved.InheritedKey, name);
            var liveSpecies = Live.Species(species, name);
            var tableSpecies = _table.Block(MaterialOwnerKind.Species, species, name);
            var found = below is LookScope scope
                ? MaterialTable.MergeBelow(scope, liveAnimal, savedAnimal, liveSkin, tableSkin, baseSkin, liveSpecies, tableSpecies)
                : MaterialTable.MergeLayers(liveAnimal, savedAnimal, liveSkin, tableSkin, baseSkin, liveSpecies, tableSpecies);
            if (found == null) return null;
            resolved.Override = found;
            resolved.ModId = OwnerOf(SkinBlocks, resolved.SkinKey, name) ?? OwnerOf(VanillaBlocks, resolved.VanillaKey, name)
                ?? OwnerOf(SkinBlocks, resolved.InheritedKey, name) ?? OwnerOf(SpeciesBlocks, species, name) ?? "";
            return resolved;
        }

        /// <summary>An editor or per-animal block, copied with its files made full (relative ones are in UserData/Tyrant) and pooled.</summary>
        private static MaterialOverride Prepared(MaterialOverride block)
        {
            var copy = MaterialTable.WithFullPaths(block, FrameworkMod.TyrantDirectory);
            Pool.Intern(copy);
            return copy;
        }

        // ---- textures ----

        /// <summary>The ramp texture (32×32 from stops or a thin film, or the mod's PNG), uncompressed and without mips; null when it cannot be made.</summary>
        private static Texture2D? RampTexture(RampSource ramp)
        {
            if (Ramps.TryGetValue(ramp, out var cached)) return cached;
            Texture2D? texture = null;
            try
            {
                var stops = IridescenceRamp.Stops(ramp); // a thin film is worked out here, once
                if (stops != null)
                {
                    texture = NewRamp();
                    texture.LoadRawTextureData(IridescenceRamp.Pixels(stops));
                    texture.Apply(false, true);
                }
                else if (ramp.File != null)
                {
                    var bytes = File.ReadAllBytes(ramp.File);
                    texture = NewRamp();
                    if (!texture.LoadImage(bytes, true))
                    {
                        UnityEngine.Object.Destroy(texture);
                        texture = null;
                        FrameworkMod.Log.Warning($"{ModOf(ramp.File)}: {Path.GetFileName(ramp.File)} is not a readable PNG; the iridescence keeps the material's own ramp.");
                    }
                }
                if (texture != null) texture.wrapMode = TextureWrapMode.Clamp;
            }
            catch (Exception ex)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                texture = null;
                FrameworkMod.Log.Warning($"{ModOf(ramp.File ?? "")}: the iridescence ramp {Path.GetFileName(ramp.File ?? "")} could not be made ({ex.GetBaseException().Message}); the iridescence keeps the material's own ramp.");
            }
            Ramps[ramp] = texture; // a failure is remembered too, so it is reported once and not retried every tick
            return texture;
        }

        private static Texture2D NewRamp() =>
            new Texture2D(32, 32, TextureFormat.RGBA32, false, false) { name = "Tyrant iridescence ramp", hideFlags = HideFlags.DontUnloadUnusedAsset };

        /// <summary>A mask PNG, owned by this module; under a mod's materials/ folder the PNG wins over its cooked copy.</summary>
        private static Texture? FileMask(MaskSource mask)
        {
            if (mask.Everywhere) return Texture2D.whiteTexture;
            if (mask.File == null) return null;
            if (FileMasks.TryGetValue(mask, out var file)) return file;
            var modId = ModOf(mask.File);
            var directory = ModDirectories.FirstOrDefault(m => m.Id == modId).Directory;
            var looseFirst = directory == null || MaterialFolders.IsMaterialImage(mask.File, directory);
            FileMasks[mask] = file = TextureCache.Load(new Replacement(modId.Length > 0 ? modId : "Tyrant", "material mask", mask.File), SlotKind.Data, looseFirst);
            return file;
        }

        private static Texture? MaskTexture(MaskSource mask, Resolved found, bool male, Written? record)
        {
            if (mask.Species != null) return SpeciesLooks.Mask(mask.Species); // Tyrant's copy, null while it loads
            if (mask.Everywhere || mask.File != null) return FileMask(mask);
            if (mask.From == null) return null;

            // One mask object can serve several skins (a starting skin's, inherited): each skin's own map makes its own mask.
            var skin = found.SkinKey ?? found.InheritedKey ?? found.Species;
            var key = (mask, skin, male);
            if (SlotMasks.TryGetValue(key, out var cached)) return cached;
            var has = SlotFile(found.Entry, found.SkinModId, found.Directory, male, mask.From, out var source)
                || SlotFile(found.BaseEntry, found.BaseModId, found.BaseDirectory, male, mask.From, out source);
            if (!has) return GameMask(mask, found, record); // a vanilla skin, or a slot the mod skin takes from the game
            Texture? result = Texture2D.whiteTexture;
            if (source != null) // null: TextureCache reported the PNG
            {
                try
                {
                    result = Extract(source, mask.Channel, mask.Invert, skin, mask.From);
                }
                catch (Exception ex)
                {
                    FrameworkMod.Log.Warning($"{skin}: its iridescence mask could not be made from {mask.From} ({ex.GetBaseException().Message}); the whole body shimmers.");
                }
            }
            SlotMasks[key] = result;
            return result;
        }

        /// <summary>
        /// A mask from the map the game put in the block for that slot (under Tyrant's own fur mask, when it set one), cached by
        /// that texture; null while the game has not loaded it (tried again on the next write).
        /// </summary>
        private static Texture? GameMask(MaskSource mask, Resolved found, Written? record)
        {
            string property;
            try
            {
                property = SkinSlotNames.Property(SkinSlotNames.Canonical(mask.From!) ?? mask.From!);
            }
            catch (ArgumentException)
            {
                if (Reported.Add(("slot", found.Species, mask.From!)))
                    FrameworkMod.Log.Warning($"{found.VanillaKey ?? found.Species}: its iridescence mask uses {mask.From}, which is not a skin map; the whole body shimmers.");
                return Texture2D.whiteTexture;
            }
            var source = Present(GameTexture(record, property));
            if (source == null) return null;
            var key = (mask, source.GetInstanceID());
            if (GameMasks.TryGetValue(key, out var cached)) return cached;
            Texture? result = Texture2D.whiteTexture;
            try
            {
                result = Extract(source, mask.Channel, mask.Invert, found.VanillaKey ?? found.Species, mask.From!);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"{found.VanillaKey ?? found.Species}: its iridescence mask could not be made from {mask.From} ({ex.GetBaseException().Message}); the whole body shimmers.");
            }
            GameMasks[key] = result;
            return result;
        }

        /// <summary>The texture in <see cref="Block"/> for a key, or the game's under it when Tyrant's own is there.</summary>
        private static Texture? GameTexture(Written? record, string property)
        {
            var current = Block.GetTexture(property);
            return record != null && record.Ours.TryGetValue(property, out var ours) && current == ours && record.Under.TryGetValue(property, out var under)
                ? under
                : current;
        }

        /// <summary>
        /// True when the game gives this age a fur mask of its own: its skin data names a fur map (CalculateKeywords' test) and
        /// the block holds a mask that is not the blank FurMaskEmpty (AnimalVisuals' test), nor a skin's mask with no red (a
        /// starting skin's copy of a furless template's). Grow fur leaves such a mask alone. A mask in the block without a map
        /// is another skin's (the Nursery reuses one animal), so it does not count.
        /// </summary>
        private static bool GameHasFur(Members members, object animal, Written record, string property, string mapField)
        {
            var texture = Present(GameTexture(record, property));
            if (texture == null || texture.name == FurMaskEmpty || TextureCache.HasNoFur(texture)) return false;
            var data = members.OwnSkinVariationData?.GetValue(animal, null);
            var field = data == null ? null : FieldOf(data, mapField);
            if (field == null) return true; // the skin data cannot be read: the block alone tells
            var map = field.GetValue(data);
            if (map == null) return false;
            var type = map.GetType();
            if (!KeyValid.TryGetValue(type, out var valid)) KeyValid[type] = valid = AccessTools.Method(type, "RuntimeKeyIsValid", Type.EmptyTypes);
            return valid == null || !(valid.Invoke(map, null) is bool ok) || ok;
        }

        private static Texture? MaterialTexture(Material? material, string property) =>
            material != null && material.HasProperty(property) ? Present(material.GetTexture(property)) : null;

        /// <summary>Null for a missing or destroyed texture (Unity's own null).</summary>
        private static Texture? Present(Texture? texture) => texture != null ? texture : null;

        /// <summary>True when the skin has a PNG for the adult slot for this sex; the texture is null when it could not be loaded.</summary>
        private static bool SlotFile(SkinEntry? entry, string? modId, string? directory, bool male, string slot, out Texture2D? texture)
        {
            texture = null;
            var files = entry == null ? null : male ? entry.Male : entry.Female;
            if (files == null || modId == null || directory == null || !files.TryGetValue(slot, out var file)) return false;
            var path = Path.Combine(directory, file);
            if (!ModPaths.IsInside(path, directory)) return false;
            texture = TextureCache.Get(new Replacement(modId, $"{entry!.Key(modId)}:{slot}", Path.GetFullPath(path)), SkinSlotNames.KindOf(slot));
            return true;
        }

        /// <summary>
        /// One channel of a (possibly compressed, unreadable) texture as a grey mask, compressed and no longer readable. A size
        /// not divisible by 4 cannot be compressed: it stays uncompressed (about 4 times the memory), said once per
        /// <paramref name="owner"/> and slot.
        /// </summary>
        private static Texture2D Extract(Texture source, char channel, bool invert, string owner, string from)
        {
            var mask = ReadGrey(source, channel, invert, mipmaps: true);
            try
            {
                if (mask.width % 4 == 0 && mask.height % 4 == 0) mask.Compress(true);
                else if (Reported.Add(("size", owner, from)))
                    FrameworkMod.Log.Warning($"{owner}: its iridescence mask from {from} is {mask.width}x{mask.height}; sizes divisible by 4 use less memory (it stays uncompressed).");
                mask.Apply(false, true); // no longer readable: frees the CPU copy
                return mask;
            }
            catch
            {
                UnityEngine.Object.Destroy(mask);
                throw;
            }
        }

        /// <summary>
        /// One channel of a (possibly compressed, unreadable) texture as a readable grey RGBA texture, through a linear render
        /// texture; the caller owns it (the editor's Export mask PNG encodes it). The channel is taken in the texture's own pixel
        /// buffer: no managed copy of the pixels is made.
        /// </summary>
        public static Texture2D ReadGrey(Texture source, char channel, bool invert, bool mipmaps) => Read(source, mipmaps, channel, invert);

        /// <summary>
        /// A (possibly compressed, unreadable) texture as a readable RGBA texture, read back through a linear render texture into
        /// a linear texture, so the shader samples the values it sampled from the source. The caller owns it.
        /// </summary>
        public static Texture2D ReadPixels(Texture source, bool mipmaps) => Read(source, mipmaps, null, false);

        /// <summary><see cref="ReadPixels"/>, with one channel made grey in place first when <paramref name="channel"/> is set.</summary>
        private static Texture2D Read(Texture source, bool mipmaps, char? channel, bool invert)
        {
            int width = source.width, height = source.height;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, mipmaps, true) { name = "Tyrant iridescence mask", hideFlags = HideFlags.DontUnloadUnusedAsset };
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                // Level 0 in Unity's own buffer (a 4K map is 64 MB: a managed copy would sit on Mono's large-object heap for good).
                if (channel is char c) MaskChannel.ExtractInPlace(new NativeBytes(copy.GetPixelData<byte>(0)), c, invert);
                copy.Apply(mipmaps, false);
                return copy;
            }
            catch
            {
                UnityEngine.Object.Destroy(copy);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        /// <summary>A texture's pixel buffer (a view into Unity's memory, written in place) as <see cref="MaskChannel"/> reads it.</summary>
        private readonly struct NativeBytes : IByteBuffer
        {
            private readonly Unity.Collections.NativeArray<byte> _data;

            public NativeBytes(Unity.Collections.NativeArray<byte> data) => _data = data;

            public int Length => _data.Length;

            public byte this[int index]
            {
                get => _data[index];
                set
                {
                    var data = _data; // NativeArray's setter is not readonly; the copy points at the same buffer
                    data[index] = value;
                }
            }
        }

        /// <summary>Forgets the ramps and masks no table, live or saved block uses, and destroys the textures made for them.</summary>
        private static void Sweep()
        {
            Pool.Retain(_table.Blocks.Concat(Live.Blocks).Concat(Saved.Blocks).Concat(_previewBlock == null ? Enumerable.Empty<MaterialOverride>() : new[] { _previewBlock }));
            Drop(Ramps, ramp => Pool.Contains(ramp));
            Drop(FileMasks, mask => Pool.Contains(mask));
            Drop(SlotMasks, key => Pool.Contains(key.Mask));
            Drop(GameMasks, key => Pool.Contains(key.Mask));
        }

        private static void Drop<TKey, TTexture>(Dictionary<TKey, TTexture> cache, Func<TKey, bool> used) where TTexture : Texture?
        {
            foreach (var key in cache.Keys.Where(k => !used(k)).ToList())
            {
                UnityEngine.Object? texture = cache[key];
                cache.Remove(key);
                // Unity's own white and black stand in for a mask that failed; they are never destroyed.
                if (texture != null && texture != Texture2D.whiteTexture && texture != Texture2D.blackTexture)
                    UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>A file's time and size, so an image saved again is a new source (and a new texture) on the next reload.</summary>
        private static string FileStamp(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info.LastWriteTimeUtc.Ticks + ":" + info.Length : "missing";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return "unreadable";
            }
        }

        // ---- the game's animals ----

        private static Type? _gameType;
        private static PropertyInfo? _hasInstance, _animalManager, _animals, _animalMap, _vivariumMap;
        private static MethodInfo? _getReader;
        private static bool _animalsRead;

        /// <summary>
        /// Every animal in the park and the vivarium, from Game.AnimalManager.Animals under its read lock (copied, then released);
        /// empty outside a park.
        /// </summary>
        private static List<object> LoadedAnimals()
        {
            var animals = new List<object>();
            ReadMaps(map =>
            {
                foreach (var value in map.Values)
                    if (value != null) animals.Add(value);
            });
            return animals;
        }

        /// <summary>
        /// The persistent ids of every animal in the park and the vivarium (an unrealised vivarium animal included); empty for a
        /// park without animals, null outside a park or when the maps cannot be read.
        /// </summary>
        public static List<ulong>? LoadedIds()
        {
            var ids = new List<ulong>();
            return ReadMaps(map =>
            {
                foreach (var key in map.Keys)
                    if (key != null && FieldOf(key, "uniqueID")?.GetValue(key) is ulong id) ids.Add(id);
            }) ? ids : null;
        }

        /// <summary>Calls <paramref name="read"/> with the park's and the vivarium's maps; false when they could not be read.</summary>
        private static bool ReadMaps(Action<IDictionary> read)
        {
            try
            {
                if (!_animalsRead)
                {
                    _animalsRead = true;
                    _gameType = AccessTools.TypeByName("PrehistoricKingdom.Game");
                    _hasInstance = _gameType == null ? null : AccessTools.Property(_gameType, "HasInstance");
                    _animalManager = _gameType == null ? null : AccessTools.Property(_gameType, "AnimalManager");
                    if (_gameType == null || _hasInstance == null || _animalManager == null)
                        FrameworkMod.Log.Warning("Game.AnimalManager was not found (game updated?); live edits of a whole skin or species show on the selected animal only.");
                }
                if (_hasInstance == null || _animalManager == null || !(_hasInstance.GetValue(null, null) is bool has) || !has) return false;
                var manager = _animalManager.GetValue(null, null);
                if (manager == null) return false;
                _animals ??= AccessTools.Property(manager.GetType(), "Animals");
                var collection = _animals?.GetValue(manager, null);
                if (collection == null) return false;
                _getReader ??= AccessTools.Method(collection.GetType(), "GetReader", Type.EmptyTypes);
                var reader = _getReader?.Invoke(collection, null); // a boxed AnimalCollection.Reader, holding the read lock
                if (reader == null) return false;
                try
                {
                    _animalMap ??= AccessTools.Property(reader.GetType(), "PersistentIDToAnimalMap");
                    _vivariumMap ??= AccessTools.Property(reader.GetType(), "PersistentIDToVivariumAnimalMap");
                    if (!(_animalMap?.GetValue(reader, null) is IDictionary animals)) return false;
                    read(animals);
                    if (_vivariumMap?.GetValue(reader, null) is IDictionary vivarium) read(vivarium);
                    return true;
                }
                finally
                {
                    (reader as IDisposable)?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Failed(ex);
                return false;
            }
        }

        /// <summary>AnimalVisuals.AutoSetSharedMaterialVariant (private): AnimalMaterials.AddUpdateAnimal unless the animal blocks it.</summary>
        private static MethodInfo? AutoSetMaterial()
        {
            if (_autoSetMaterialRead) return _autoSetMaterial;
            _autoSetMaterialRead = true;
            var type = AccessTools.TypeByName("PrehistoricKingdom.AnimalVisuals");
            _autoSetMaterial = type == null ? null : AccessTools.Method(type, "AutoSetSharedMaterialVariant");
            if (_autoSetMaterial == null)
                FrameworkMod.Log.Warning("AnimalVisuals.AutoSetSharedMaterialVariant was not found (game updated?); turning iridescence on or off in the editor shows after the animal reloads.");
            return _autoSetMaterial;
        }

        // ---- reflection and helpers ----

        private sealed class Members
        {
            public Members(Type type)
            {
                BaseData = AccessTools.Property(type, "BaseData");
                SkinData = AccessTools.Property(type, "SkinData");
                OwnSkinVariationData = AccessTools.Property(type, "OwnSkinVariationData");
                SkinVariationRuntime = AccessTools.Property(type, "SkinVariationRuntime");
                SkinType = AccessTools.Property(type, "SkinType");
                Sex = AccessTools.Property(type, "Sex");
                BaseMaterial = AccessTools.Property(type, "BaseMaterial");
                HasValidPersistentId = AccessTools.Property(type, "HasValidPersistentID");
                PersistentId = AccessTools.Property(type, "PersistentID");
                Lods = AccessTools.Property(type, "Lods");
                IsVisuallyPresent = AccessTools.Method(type, "IsVisuallyPresent", Type.EmptyTypes);
                try
                {
                    var sex = Sex?.PropertyType;
                    if (sex != null && sex.IsEnum) Male = Enum.Parse(sex, "Male");
                }
                catch (ArgumentException)
                {
                    // no Male in the game's Sex: every animal counts as female for masks from a map
                }
            }

            public PropertyInfo? BaseData { get; }
            public PropertyInfo? SkinData { get; }

            /// <summary>IAnimal.OwnSkinVariationData (an AnimalSkinVariationAsset): its fur map references say whether the game has fur.</summary>
            public PropertyInfo? OwnSkinVariationData { get; }

            /// <summary>IAnimal.SkinVariationRuntime: the animal's own iridescenceOn and iridescenceStrength.</summary>
            public PropertyInfo? SkinVariationRuntime { get; }

            public PropertyInfo? SkinType { get; }
            public PropertyInfo? Sex { get; }
            public PropertyInfo? BaseMaterial { get; }
            public PropertyInfo? HasValidPersistentId { get; }
            public PropertyInfo? PersistentId { get; }
            public PropertyInfo? Lods { get; }
            public MethodInfo? IsVisuallyPresent { get; }

            /// <summary>The game's Sex.Male, boxed once: comparing to it needs no string per animal.</summary>
            public object? Male { get; }
        }

        private sealed class Resolved
        {
            public Resolved(Members members, string species, Material material)
            {
                Members = members;
                Species = species;
                Material = material;
            }

            public Members Members { get; }
            public string Species { get; }

            /// <summary>The animal's BaseMaterial: its name picks the blocks, and keys go back to its values.</summary>
            public Material Material { get; }

            public MaterialOverride Override { get; set; } = null!;
            public string ModId { get; set; } = "";
            public string? SkinKey { get; set; }
            public string? SkinModId { get; set; }
            public SkinEntry? Entry { get; set; }
            public string? Directory { get; set; }
            public string? InheritedKey { get; set; }
            public string? BaseModId { get; set; }
            public SkinEntry? BaseEntry { get; set; }
            public string? BaseDirectory { get; set; }

            /// <summary>"&lt;species&gt;/&lt;skin name&gt;" when the animal wears a vanilla skin.</summary>
            public string? VanillaKey { get; set; }
        }

        private static Members MembersOf(Type type)
        {
            if (!MembersByType.TryGetValue(type, out var members)) MembersByType[type] = members = new Members(type);
            return members;
        }

        private static FieldInfo? FieldOf(object target, string name)
        {
            var key = (target.GetType(), name);
            if (!Fields.TryGetValue(key, out var field)) Fields[key] = field = AccessTools.Field(key.Item1, name);
            return field;
        }

        private static string? SpeciesOf(Members members, object animal)
        {
            var data = members.BaseData?.GetValue(animal, null);
            return data == null ? null : FieldOf(data, "speciesID")?.GetValue(data) as string;
        }

        /// <summary>"&lt;species&gt;/&lt;skinName&gt;" of a vanilla skin, as SoundsModule's AnimalInfo.SkinKey builds it.</summary>
        private static string? VanillaKeyOf(object? skinData, string species)
        {
            var name = skinData == null ? null : FieldOf(skinData, "skinName")?.GetValue(skinData) as string;
            return string.IsNullOrEmpty(name) ? null : species + "/" + name;
        }

        private static bool IsVisuallyPresent(Members members, object animal) =>
            members.IsVisuallyPresent == null || (members.IsVisuallyPresent.Invoke(animal, null) is bool present && present);

        private static bool IsMale(Members members, object animal) =>
            members.Male != null && members.Male.Equals(members.Sex?.GetValue(animal, null));

        /// <summary>IAnimal.SkinType as the game's AnimalSkinType number (0 base, 1 albino, 2 melanistic, 3 leucistic).</summary>
        private static int SkinTypeOf(Members members, object animal) =>
            members.SkinType?.GetValue(animal, null) is IConvertible value ? value.ToInt32(null) : 0;

        /// <summary>
        /// The grown animal's strength in the game (SkinVariationRuntime: iridescenceStrength while iridescenceOn, else 0), the
        /// adult end of a baby strength's lerp when no layer sets the adult one; 0 when it cannot be read.
        /// </summary>
        private static float GameStrength(Members members, object animal)
        {
            var runtime = members.SkinVariationRuntime?.GetValue(animal, null);
            if (runtime == null || !(FieldOf(runtime, "iridescenceOn")?.GetValue(runtime) is bool on) || !on) return 0f;
            return FieldOf(runtime, "iridescenceStrength")?.GetValue(runtime) is float strength ? strength : 0f;
        }

        /// <summary>PersistentID.uniqueID when the animal has a valid one; null otherwise (then it gets the set strength exactly).</summary>
        private static ulong? IdOf(Members members, object animal)
        {
            if (!(members.HasValidPersistentId?.GetValue(animal, null) is bool valid) || !valid) return null;
            var id = members.PersistentId?.GetValue(animal, null);
            return id != null && FieldOf(id, "uniqueID")?.GetValue(id) is ulong unique ? unique : (ulong?)null;
        }

        /// <summary>IAnimal.Lods into <see cref="Renderers"/>; false when there are none.</summary>
        private static bool Lods(Members members, object animal)
        {
            Renderers.Clear();
            if (members.Lods?.GetValue(animal, null) is IEnumerable items)
                foreach (var item in items)
                    if (item is Renderer renderer && renderer != null) Renderers.Add(renderer);
            return Renderers.Count > 0;
        }

        private static void Note(Dictionary<string, Dictionary<string, string>> blocks, string owner, IEnumerable<string> materials, string modId)
        {
            if (!blocks.TryGetValue(owner, out var names)) blocks[owner] = names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var material in materials) names[material] = modId;
        }

        private static string? OwnerOf(Dictionary<string, Dictionary<string, string>> blocks, string? owner, string material) =>
            owner != null && blocks.TryGetValue(owner, out var names) && names.TryGetValue(material, out var modId) ? modId : null;

        /// <summary>Part A changes only the animal's own material: any other name in a block is reported once.</summary>
        private static void ReportOtherNames(Dictionary<string, Dictionary<string, string>> blocks, string? owner, string material)
        {
            if (owner == null || !blocks.TryGetValue(owner, out var names)) return;
            foreach (var pair in names)
                if (!string.Equals(pair.Key, material, StringComparison.Ordinal) && Reported.Add(("name", owner, pair.Key)))
                    FrameworkMod.Log.Warning($"{pair.Value}: \"{pair.Key}\" is not this animal's material ({owner}); extra materials come in a later Tyrant.");
        }

        /// <summary>The mod whose folder holds a file (its cooked pack is looked up by that mod's id); "" when none does.</summary>
        private static string ModOf(string path)
        {
            var best = "";
            var length = -1;
            foreach (var (id, directory) in ModDirectories)
                if (directory.Length > length && ModPaths.IsInside(path, directory)) (best, length) = (id, directory.Length);
            return best;
        }

        private static void Failed(Exception ex)
        {
            var message = ex.GetBaseException().Message;
            if (Announced.Add("failed|" + message))
                FrameworkMod.Log.Error("Mod materials failed for an animal; it keeps its vanilla look: " + message);
        }
    }
}
