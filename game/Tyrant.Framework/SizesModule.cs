using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Species sizes (sizes/*.json: each species' sizes and genetic size ranges; its size limit follows from them), in memory only.
    /// - Species: before the game copies species data into its native tables (prefix on AnimalsGlobal.InitializeNativeData, after
    ///   SpeciesModule registered new species there), each target's growth curve is replaced and its skins' sex sizes multiplied.
    ///   Order: the skins in the list at that moment (vanilla, a new species' own, mod skins already added) are multiplied
    ///   once; mod skins and stand-ins SkinsModule adds later are copies of a multiplied skin, and <see cref="Cloned"/> marks
    ///   them as done so they are not multiplied again. Every later native rebuild (each park load) only applies what is new.
    /// - Nursery: a transpiler follows each of NurseryMenuV2's slider reads of incubationGeneticSizeMinMax with
    ///   <see cref="SliderRange"/>, which gives the selected species' range (CurrentSelectedAnimalData) or the game's.
    /// - Breeding: the same for EmbryoGroup.GetOffspring's read of breedingGeneticSizeMinMax (<see cref="BreedingRange"/>, the
    ///   group's species). The shared fields are never written, so uncustomised eggs (VirtualAnimal.GenerateGeneticSize) and
    ///   species without a range keep the game's.
    /// - Size limit: automatic. Each species gets the limit its sizes need (<see cref="SizePlan"/>, <see cref="SizeLimit.Fitting"/>,
    ///   up to Tyrant's 20). The first time a species needs more than the game's 0.05 to 2, a transpiler swaps the cap constants of
    ///   AnimalNative.CurrentScale's getter for a read of the animal's species' limit (<see cref="ScaleLimit"/>, by native species
    ///   index, filled at each InitializeNativeData), all or none (<see cref="ScaleCap"/>). The game's error log stays: with the
    ///   constants swapped it fires only outside the species' limit and names it, so a scale nothing should reach (a broken
    ///   curve, another mod) is still reported, while sizes Tyrant backed off never log.
    /// With no sizes or ranges nothing is patched, and while every species' limit is the game's the cap is left alone.
    /// </summary>
    internal static class SizesModule
    {
        private const string Globals = "PrehistoricKingdom.AnimalsV2.Data.GlobalAnimalParameters";
        private static SizeTable _table = new SizeTable();
        private static SizePlan? _plan;
        private static bool _off;
        private static FieldInfo? _incubation, _breedingField;
        private static Dictionary<string, Vector2> _sliders = new Dictionary<string, Vector2>();
        private static Dictionary<string, Vector2> _breeding = new Dictionary<string, Vector2>();
        private static MethodInfo? _selectedSpecies;
        private static MethodInfo? _nativeSpecies, _speciesIndex;
        private static bool _limitsLifted;
        private static bool _limitSwapped;
        private static HarmonyLib.Harmony? _harmony;

        public static void Start(IReadOnlyList<LoadedMod> mods, HarmonyLib.Harmony harmony)
        {
            _table = SizeTable.Build(mods, out var messages);
            foreach (var message in messages) FrameworkMod.Log.Warning(message);
            if (!_table.HasAny) return;
            _harmony = harmony; // the cap is lifted later, only when a species' sizes need it (LiftLimits)
            if (_table.SetsSlider) PatchSlider(harmony);
            if (_table.SetsBreeding) PatchBreeding(harmony);
            var native = AccessTools.Method(AccessTools.TypeByName("PrehistoricKingdom.AnimalsV2.AI.AnimalsGlobal"), "InitializeNativeData");
            if (native == null)
            {
                FrameworkMod.Log.Warning("AnimalsGlobal.InitializeNativeData not found (game updated?); species sizes and size limits are off.");
                return;
            }
            // Low priority: after SpeciesModule's prefix there, so new species are in the database.
            harmony.Patch(native, prefix: new HarmonyMethod(typeof(SizesModule), nameof(BeforeNativeData)) { priority = Priority.Low });
        }

        /// <summary>SkinsModule copied a skin: a copy of a multiplied skin already has its sizes.</summary>
        public static void Cloned(object source, object clone) => _plan?.Cloned(source, clone);

        // ---- Species and size limits: before the native tables are built ----

        // __0: the game's own list of animal data (AnimalsGlobal.InitializeNativeData(List<AnimalData>)).
        private static void BeforeNativeData(object __0)
        {
            try
            {
                if (!(__0 is IList list))
                {
                    if (!_off) FrameworkMod.Log.Warning("InitializeNativeData's animal list is not a list (game updated?); species sizes are off.");
                    _off = true;
                    return;
                }
                if (_off) return;
                _plan ??= Begin();
                if (_plan == null) return;
                var pass = _plan.Next(Species(list));
                // InitializeNativeData numbers the species by their place in this list (AnimalData.NativeSpeciesID).
                if (_limitsLifted) ScaleLimit.Table = _plan.Limits(list.Cast<object>().Select(d => d == null ? null : Get(d, "speciesID") as string).ToList());
                foreach (var (data, id, keys) in pass.Curves)
                    try
                    {
                        SetCurve(data, keys);
                    }
                    catch (Exception ex)
                    {
                        FrameworkMod.Log.Warning($"{id}: its growth curve could not be replaced ({ex.GetBaseException().Message}); it keeps the game's.");
                    }
                foreach (var (skin, male, female) in pass.Skins)
                {
                    Set(skin, "maleSizeMultiplier", (float)male);
                    Set(skin, "femaleSizeMultiplier", (float)female);
                }
                foreach (var warning in pass.Warnings) FrameworkMod.Log.Warning(warning);
                foreach (var note in pass.Notes) FrameworkMod.Log.Msg(note);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Species sizes could not be applied: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>Reads the game's genetic ranges and makes the plan; null turns sizes off.</summary>
        private static SizePlan? Begin()
        {
            try
            {
                var persistent = AccessTools.TypeByName("PrehistoricKingdom.PKPersistentData") ?? throw new TypeLoadException("PKPersistentData");
                var globals = (AccessTools.Property(persistent, "GlobalAnimalParameters") ?? throw new MissingMemberException("PKPersistentData.GlobalAnimalParameters")).GetValue(null, null)
                    ?? throw new InvalidOperationException("the game's animal parameters are not loaded");
                var incubation = Range((Vector2)IncubationField(globals.GetType()).GetValue(globals));
                var breeding = Range((Vector2)BreedingField(globals.GetType()).GetValue(globals));
                return new SizePlan(_table, incubation, breeding, LiftLimits);
            }
            catch (Exception ex)
            {
                _off = true;
                FrameworkMod.Log.Warning("Species sizes are off: " + ex.GetBaseException().Message);
                return null;
            }
        }

        /// <summary>The species that have sizes, as the plan sees them; the rest are not read.</summary>
        private static List<SizeSpecies> Species(IList list)
        {
            var result = new List<SizeSpecies>();
            foreach (var data in list.Cast<object>())
            {
                if (data == null || !(Get(data, "speciesID") is string id) || _table.For(id) is not { IsEmpty: false }) continue;
                if (!(Get(data, "scaleGrowthCurve") is AnimationCurve curve) || !(Get(data, "skinsData") is IList skins))
                {
                    FrameworkMod.Log.Warning($"{id}: its growth curve or skins were not found (game updated?); its sizes are skipped.");
                    continue;
                }
                // Read as the game reads it (Unity's Hermite with the keys' tangents and weights), not as lines between the keys.
                var keys = SizeMath.Sample(curve.keys.Select(k => k.time), curve.Evaluate);
                var sizeSkins = skins.Cast<object>().Where(s => s != null)
                    .Select(s => new SizeSkin(s, Convert.ToDouble(Get(s, "maleSizeMultiplier")), Convert.ToDouble(Get(s, "femaleSizeMultiplier")))).ToList();
                result.Add(new SizeSpecies(data, id, keys, sizeSkins));
            }
            return result;
        }

        /// <summary>A new curve through the rebuilt keys, with smooth tangents and the old curve's wrap modes.</summary>
        private static void SetCurve(object data, (float Time, float Value)[] keys)
        {
            var old = (AnimationCurve)Get(data, "scaleGrowthCurve")!;
            var slopes = SizePlan.Slopes(keys);
            var frames = keys.Select((k, i) => new Keyframe(k.Time, k.Value, slopes[i], slopes[i])).ToArray();
            Set(data, "scaleGrowthCurve", new AnimationCurve(frames) { preWrapMode = old.preWrapMode, postWrapMode = old.postWrapMode });
        }

        // ---- Nursery: the Customize size slider ----

        private static void PatchSlider(HarmonyLib.Harmony harmony)
        {
            try
            {
                var menu = AccessTools.TypeByName("PrehistoricKingdom.NurseryMenuV2") ?? throw new TypeLoadException("NurseryMenuV2");
                var field = IncubationField(AccessTools.TypeByName(Globals) ?? throw new TypeLoadException(Globals));
                var methods = SizePlan.SliderMethods.Select(name => AccessTools.Method(menu, name) ?? throw new MissingMethodException("NurseryMenuV2", name)).ToList();
                // All or none: a slider whose creation and display used different ranges would show the wrong size.
                foreach (var method in methods) Expect(method, field, "the incubation size range");
                _selectedSpecies = AccessTools.PropertyGetter(menu, "CurrentSelectedAnimalData") ?? throw new MissingMethodException("NurseryMenuV2", "get_CurrentSelectedAnimalData");
                _sliders = Vectors(_table.SliderRanges);
                var transpiler = new HarmonyMethod(typeof(SizesModule), nameof(SliderTranspiler));
                foreach (var method in methods) harmony.Patch(method, transpiler: transpiler);
                FrameworkMod.Log.Msg($"Nursery size slider ranges: {Ranges(_table.SliderRanges)}; uncustomised eggs and other species keep the game's range.");
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The Nursery size slider keeps the game's range: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>After each read of parameters.incubationGeneticSizeMinMax: this (the menu) is pushed and SliderRange picks the range.</summary>
        private static IEnumerable<CodeInstruction> SliderTranspiler(IEnumerable<CodeInstruction> instructions) =>
            AfterRead(instructions, _incubation!, nameof(SliderRange));

        /// <summary>The Customize size slider's range: the selected species' "nursery", else the game's.</summary>
        internal static Vector2 SliderRange(Vector2 game, object menu)
        {
            try
            {
                return _selectedSpecies?.Invoke(menu, null) is { } data && Get(data, "speciesID") is string id && _sliders.TryGetValue(id, out var range) ? range : game;
            }
            catch (Exception)
            {
                return game;
            }
        }

        // ---- Breeding: the size range of bred young ----

        private static void PatchBreeding(HarmonyLib.Harmony harmony)
        {
            try
            {
                var group = AccessTools.TypeByName("AnimalsV2.EmbryoGroup") ?? throw new TypeLoadException("EmbryoGroup");
                var method = AccessTools.Method(group, "GetOffspring") ?? throw new MissingMethodException("EmbryoGroup", "GetOffspring");
                if (AccessTools.Field(group, "speciesData") == null) throw new MissingFieldException("EmbryoGroup", "speciesData");
                Expect(method, BreedingField(AccessTools.TypeByName(Globals) ?? throw new TypeLoadException(Globals)), "the breeding size range");
                _breeding = Vectors(_table.BreedingRanges);
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(SizesModule), nameof(BreedingTranspiler)));
                FrameworkMod.Log.Msg($"Breeding size ranges: {Ranges(_table.BreedingRanges)}; other species keep the game's range.");
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Breeding keeps the game's size range: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>After GetOffspring's read of parameters.breedingGeneticSizeMinMax: this (the group) is pushed and BreedingRange picks the range.</summary>
        private static IEnumerable<CodeInstruction> BreedingTranspiler(IEnumerable<CodeInstruction> instructions) =>
            AfterRead(instructions, _breedingField!, nameof(BreedingRange));

        /// <summary>The genetic size range of a group's young: its species' "breeding", else the game's.</summary>
        internal static Vector2 BreedingRange(Vector2 game, object group)
        {
            try
            {
                return Get(group, "speciesData") is { } data && Get(data, "speciesID") is string id && _breeding.TryGetValue(id, out var range) ? range : game;
            }
            catch (Exception)
            {
                return game;
            }
        }

        /// <summary>The method reads the field (by value, never by address) and is an instance method, as the transpilers expect.</summary>
        private static void Expect(MethodInfo method, FieldInfo field, string what)
        {
            var code = PatchProcessor.GetOriginalInstructions(method);
            if (method.IsStatic || !code.Any(i => i.LoadsField(field)) || code.Any(i => i.LoadsField(field, byAddress: true)))
                throw new InvalidOperationException($"{method.DeclaringType?.Name}.{method.Name} no longer reads {what} as Tyrant expects (game updated?)");
        }

        /// <summary>Each read of the field (the game's range) is followed by ldarg.0 and a call of picker(game, this), which leaves the range to use.</summary>
        private static IEnumerable<CodeInstruction> AfterRead(IEnumerable<CodeInstruction> instructions, FieldInfo field, string picker)
        {
            var pick = AccessTools.Method(typeof(SizesModule), picker);
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!instruction.LoadsField(field)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, pick);
            }
        }

        private static Dictionary<string, Vector2> Vectors(IReadOnlyDictionary<string, (double Min, double Max)> ranges) =>
            ranges.ToDictionary(r => r.Key, r => new Vector2((float)r.Value.Min, (float)r.Value.Max), StringComparer.OrdinalIgnoreCase);

        private static string Ranges(IReadOnlyDictionary<string, (double Min, double Max)> ranges) =>
            string.Join(", ", ranges.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key} {Number(r.Value.Min)} to {Number(r.Value.Max)}"));

        // ---- Size limit: the game's cap on the final scale ----

        /// <summary>The plan asks once, when the first species needs a limit wider than the game's: lifts the cap and says whether it worked.</summary>
        private static bool LiftLimits()
        {
            if (_harmony != null) PatchLimit(_harmony);
            return _limitsLifted;
        }

        private static void PatchLimit(HarmonyLib.Harmony harmony)
        {
            try
            {
                var native = AccessTools.TypeByName(ScaleCap.Type) ?? throw new TypeLoadException(ScaleCap.Type);
                var getter = AccessTools.PropertyGetter(native, ScaleCap.Property) ?? throw new MissingMethodException("AnimalNative", "get_" + ScaleCap.Property);
                // All or none: a getter that compared with one limit and returned the other would size animals wrongly.
                if (ScaleCap.Find(Code(PatchProcessor.GetOriginalInstructions(getter))) == null)
                    throw new InvalidOperationException("AnimalNative.CurrentScale no longer caps the scale as Tyrant expects (game updated?)");
                // The animal's species: AnimalNative.SpeciesID (an AnimalSpeciesID), whose SpeciesID is its native index.
                _nativeSpecies = AccessTools.PropertyGetter(native, "SpeciesID") ?? throw new MissingMethodException("AnimalNative", "get_SpeciesID");
                _speciesIndex = AccessTools.PropertyGetter(_nativeSpecies.ReturnType, "SpeciesID");
                if (!_nativeSpecies.ReturnType.IsValueType || _speciesIndex == null || _speciesIndex.ReturnType != typeof(ushort))
                    throw new InvalidOperationException("AnimalNative.SpeciesID is no longer a species index as Tyrant expects (game updated?)");
                harmony.Patch(getter, transpiler: new HarmonyMethod(typeof(SizesModule), nameof(LimitTranspiler)));
                // Harmony transpiles while patching: the swap is known now (another mod's transpiler could have changed the code first).
                if (!_limitSwapped)
                {
                    harmony.Unpatch(getter, HarmonyPatchType.Transpiler, harmony.Id);
                    throw new InvalidOperationException("AnimalNative.CurrentScale was changed by another patch; its cap was left alone");
                }
                _limitsLifted = true;
                FrameworkMod.Log.Msg($"Animal size limits follow their species' sizes (the game's, {SizeLimit.Game}, for the other species).");
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"Animals keep the game's size limit ({SizeLimit.Game}): " + ex.GetBaseException().Message);
            }
        }

        /// <summary>
        /// Each ldc.r4 0.05 and ldc.r4 2 of the getter becomes its species' limit: ldarg.0, SpeciesID (into a local), its SpeciesID
        /// index, then ScaleLimit.MinOf or MaxOf. The first new instruction is the old one, so its labels and blocks stay.
        /// </summary>
        private static IEnumerable<CodeInstruction> LimitTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            if (!(ScaleCap.Find(Code(code)) is { } found)) return code; // PatchLimit then unpatches and warns
            var species = generator.DeclareLocal(_nativeSpecies!.ReturnType);
            var minOf = AccessTools.Method(typeof(ScaleLimit), nameof(ScaleLimit.MinOf));
            var maxOf = AccessTools.Method(typeof(ScaleLimit), nameof(ScaleLimit.MaxOf));
            var result = new List<CodeInstruction>();
            for (var i = 0; i < code.Count; i++)
            {
                var limit = found.Min.Contains(i) ? minOf : found.Max.Contains(i) ? maxOf : null;
                if (limit == null)
                {
                    result.Add(code[i]);
                    continue;
                }
                (code[i].opcode, code[i].operand) = (OpCodes.Ldarg_0, null);
                result.Add(code[i]);
                result.Add(new CodeInstruction(OpCodes.Call, _nativeSpecies));
                result.Add(new CodeInstruction(OpCodes.Stloc, species));
                result.Add(new CodeInstruction(OpCodes.Ldloca, species));
                result.Add(new CodeInstruction(OpCodes.Call, _speciesIndex));
                result.Add(new CodeInstruction(OpCodes.Call, limit));
            }
            _limitSwapped = true;
            return result;
        }

        private static List<(OpCode, object?)> Code(IEnumerable<CodeInstruction> code) => code.Select(i => (i.opcode, (object?)i.operand)).ToList();

        // ---- Helpers ----

        private static FieldInfo IncubationField(Type globals) =>
            _incubation ??= AccessTools.Field(globals, "incubationGeneticSizeMinMax") ?? throw new MissingFieldException(Globals, "incubationGeneticSizeMinMax");

        private static FieldInfo BreedingField(Type globals) =>
            _breedingField ??= AccessTools.Field(globals, "breedingGeneticSizeMinMax") ?? throw new MissingFieldException(Globals, "breedingGeneticSizeMinMax");

        private static (double Min, double Max) Range(Vector2 v) => (v.x, v.y);

        private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static object? Get(object target, string name) => AccessTools.Field(target.GetType(), name)?.GetValue(target);

        private static void Set(object target, string name, object value) =>
            (AccessTools.Field(target.GetType(), name) ?? throw new MissingFieldException(target.GetType().FullName, name)).SetValue(target, value);
    }

    /// <summary>
    /// The species' size limits the patched AnimalNative.CurrentScale reads: [min0, max0, min1, max1, ...] by native species index
    /// (<see cref="SizePlan.Limits"/>). Its own class with no static initialiser, so the reads stay plain static field loads on
    /// that hot path (no type-initialisation check). The array is replaced whole, never changed in place; before the first one
    /// (and for an index past it) every species has the game's limit.
    /// </summary>
    internal static class ScaleLimit
    {
        internal static float[]? Table;

        internal static float MinOf(int species)
        {
            var table = Table;
            var i = species * 2;
            return table != null && (uint)(i + 1) < (uint)table.Length ? table[i] : SizeMath.FinalMin;
        }

        internal static float MaxOf(int species)
        {
            var table = Table;
            var i = species * 2 + 1;
            return table != null && (uint)i < (uint)table.Length ? table[i] : SizeMath.FinalMax;
        }
    }
}
