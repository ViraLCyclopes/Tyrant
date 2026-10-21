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
    /// Species sizes (sizes/*.json) and the size ranges (mod.json "sizeRanges"), in memory only.
    /// - Species: before the game copies species data into its native tables (prefix on AnimalsGlobal.InitializeNativeData, after
    ///   SpeciesModule registered new species there), each target's growth curve is replaced and its skins' sex sizes multiplied.
    ///   Order: the skins in the list at that moment (vanilla, a new species' own, mod skins already added) are multiplied
    ///   once; mod skins and stand-ins SkinsModule adds later are copies of a multiplied skin, and <see cref="Cloned"/> marks
    ///   them as done so they are not multiplied again. Every later native rebuild (each park load) only applies what is new.
    /// - Breeding: written into GlobalAnimalParameters.breedingGeneticSizeMinMax (only EmbryoGroup reads it).
    /// - Nursery: a transpiler swaps NurseryMenuV2's slider reads of incubationGeneticSizeMinMax for <see cref="SliderRange"/>.
    ///   The shared field is never written, so uncustomised eggs (VirtualAnimal.GenerateGeneticSize) keep the game's range.
    /// - Size limit: when the widest limit among the mods is not the game's 0.05 to 2, a transpiler swaps the cap constants of
    ///   AnimalNative.CurrentScale's getter for <see cref="ScaleLimit"/>'s fields, all or none (<see cref="ScaleCap"/>). The game's
    ///   error log stays: with the constants swapped it fires only outside Tyrant's limit and names it, so a scale nothing should
    ///   reach (a broken curve, another mod) is still reported, while sizes Tyrant backed off never log.
    /// With no sizes and no ranges nothing is patched.
    /// </summary>
    internal static class SizesModule
    {
        private const string Globals = "PrehistoricKingdom.AnimalsV2.Data.GlobalAnimalParameters";
        private static SizeTable _table = new SizeTable();
        private static SizePlan? _plan;
        private static bool _off;
        private static Vector2 _slider;
        private static FieldInfo? _incubation;
        private static SizeLimit _limit = SizeLimit.Game;
        private static bool _limitSwapped;

        public static void Start(IReadOnlyList<LoadedMod> mods, HarmonyLib.Harmony harmony)
        {
            _table = SizeTable.Build(mods, out var messages);
            foreach (var message in messages) FrameworkMod.Log.Warning(message);
            if (!_table.HasAny) return;
            if (!_table.Limit.IsGame) PatchLimit(harmony, _table.Limit);
            if (_table.Ranges.Nursery is { } nursery) PatchSlider(harmony, nursery);
            if (!_table.All.Any(s => !s.IsEmpty) && _table.Ranges.Breeding == null) return;
            var native = AccessTools.Method(AccessTools.TypeByName("PrehistoricKingdom.AnimalsV2.AI.AnimalsGlobal"), "InitializeNativeData");
            if (native == null)
            {
                FrameworkMod.Log.Warning("AnimalsGlobal.InitializeNativeData not found (game updated?); species sizes and the breeding size range are off.");
                return;
            }
            // Low priority: after SpeciesModule's prefix there, so new species are in the database.
            harmony.Patch(native, prefix: new HarmonyMethod(typeof(SizesModule), nameof(BeforeNativeData)) { priority = Priority.Low });
        }

        /// <summary>SkinsModule copied a skin: a copy of a multiplied skin already has its sizes.</summary>
        public static void Cloned(object source, object clone) => _plan?.Cloned(source, clone);

        // ---- Species and breeding: before the native tables are built ----

        // __0: the game's own list of animal data (AnimalsGlobal.InitializeNativeData(List<AnimalData>)).
        private static void BeforeNativeData(object __0)
        {
            if (_off) return;
            try
            {
                if (!(__0 is IList list))
                {
                    FrameworkMod.Log.Warning("InitializeNativeData's animal list is not a list (game updated?); species sizes are off.");
                    _off = true;
                    return;
                }
                _plan ??= Begin();
                if (_plan == null) return;
                var pass = _plan.Next(Species(list));
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

        /// <summary>Reads the game's ranges (before any change), writes the breeding range and makes the plan; null turns sizes off.</summary>
        private static SizePlan? Begin()
        {
            try
            {
                var persistent = AccessTools.TypeByName("PrehistoricKingdom.PKPersistentData") ?? throw new TypeLoadException("PKPersistentData");
                var globals = (AccessTools.Property(persistent, "GlobalAnimalParameters") ?? throw new MissingMemberException("PKPersistentData.GlobalAnimalParameters")).GetValue(null, null)
                    ?? throw new InvalidOperationException("the game's animal parameters are not loaded");
                var breedingField = AccessTools.Field(globals.GetType(), "breedingGeneticSizeMinMax") ?? throw new MissingFieldException(Globals, "breedingGeneticSizeMinMax");
                var incubation = Range((Vector2)(IncubationField(globals.GetType()).GetValue(globals)));
                var breeding = Range((Vector2)breedingField.GetValue(globals));
                if (_table.Ranges.Breeding != null)
                {
                    var (min, max) = SizePlan.RangeFor(SizeSite.Breeding, _table.Ranges, incubation, breeding);
                    breedingField.SetValue(globals, new Vector2((float)min, (float)max));
                    FrameworkMod.Log.Msg($"Breeding size range: {Number(min)} to {Number(max)} (the game's is {Number(breeding.Min)} to {Number(breeding.Max)}).");
                }
                return new SizePlan(_table, SizePlan.InEffect(_table.Ranges, incubation, breeding), _limit);
            }
            catch (Exception ex)
            {
                _off = true;
                FrameworkMod.Log.Warning("Species sizes and the breeding size range are off: " + ex.GetBaseException().Message);
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

        private static void PatchSlider(HarmonyLib.Harmony harmony, double[] range)
        {
            try
            {
                var menu = AccessTools.TypeByName("PrehistoricKingdom.NurseryMenuV2") ?? throw new TypeLoadException("NurseryMenuV2");
                var field = IncubationField(AccessTools.TypeByName(Globals) ?? throw new TypeLoadException(Globals));
                var methods = SizePlan.SliderMethods.Select(name => AccessTools.Method(menu, name) ?? throw new MissingMethodException("NurseryMenuV2", name)).ToList();
                // All or none: a slider whose creation and display used different ranges would show the wrong size.
                foreach (var method in methods)
                {
                    var code = PatchProcessor.GetOriginalInstructions(method);
                    if (!code.Any(i => i.LoadsField(field)) || code.Any(i => i.LoadsField(field, byAddress: true)))
                        throw new InvalidOperationException($"NurseryMenuV2.{method.Name} no longer reads the incubation size range as Tyrant expects (game updated?)");
                }
                _slider = new Vector2((float)range[0], (float)range[1]);
                var transpiler = new HarmonyMethod(typeof(SizesModule), nameof(SliderTranspiler));
                foreach (var method in methods) harmony.Patch(method, transpiler: transpiler);
                FrameworkMod.Log.Msg($"Nursery size slider range: {Number(range[0])} to {Number(range[1])}; uncustomised eggs keep the game's range.");
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The Nursery size slider keeps the game's range: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>parameters.incubationGeneticSizeMinMax becomes SliderRange(): the parameters object is popped, the call pushes the range.</summary>
        private static IEnumerable<CodeInstruction> SliderTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var slider = AccessTools.Method(typeof(SizesModule), nameof(SliderRange));
            foreach (var instruction in instructions)
            {
                if (instruction.LoadsField(_incubation!))
                {
                    instruction.opcode = OpCodes.Pop; // keeps the instruction's labels and blocks
                    instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call, slider);
                    continue;
                }
                yield return instruction;
            }
        }

        /// <summary>The Customize size slider's range (sizeRanges.nursery).</summary>
        internal static Vector2 SliderRange() => _slider;

        // ---- Size limit: the game's cap on the final scale ----

        private static void PatchLimit(HarmonyLib.Harmony harmony, SizeLimit limit)
        {
            try
            {
                var native = AccessTools.TypeByName(ScaleCap.Type) ?? throw new TypeLoadException(ScaleCap.Type);
                var getter = AccessTools.PropertyGetter(native, ScaleCap.Property) ?? throw new MissingMethodException("AnimalNative", "get_" + ScaleCap.Property);
                // All or none: a getter that compared with one limit and returned the other would size animals wrongly.
                if (ScaleCap.Find(Code(PatchProcessor.GetOriginalInstructions(getter))) == null)
                    throw new InvalidOperationException("AnimalNative.CurrentScale no longer caps the scale as Tyrant expects (game updated?)");
                ScaleLimit.Min = limit.Min;
                ScaleLimit.Max = limit.Max;
                harmony.Patch(getter, transpiler: new HarmonyMethod(typeof(SizesModule), nameof(LimitTranspiler)));
                // Harmony transpiles while patching: the swap is known now (another mod's transpiler could have changed the code first).
                if (!_limitSwapped)
                {
                    harmony.Unpatch(getter, HarmonyPatchType.Transpiler, harmony.Id);
                    throw new InvalidOperationException("AnimalNative.CurrentScale was changed by another patch; its cap was left alone");
                }
                _limit = limit;
                FrameworkMod.Log.Msg($"Animal size limit: {limit} (the game's is {SizeLimit.Game}).");
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"Animals keep the game's size limit ({SizeLimit.Game}): " + ex.GetBaseException().Message);
            }
        }

        /// <summary>Each ldc.r4 0.05 and ldc.r4 2 of the getter becomes a read of ScaleLimit.Min or Max (the instruction keeps its labels).</summary>
        private static IEnumerable<CodeInstruction> LimitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            if (!(ScaleCap.Find(Code(code)) is { } found)) return code; // PatchLimit then unpatches and warns
            var minField = AccessTools.Field(typeof(ScaleLimit), nameof(ScaleLimit.Min));
            var maxField = AccessTools.Field(typeof(ScaleLimit), nameof(ScaleLimit.Max));
            foreach (var i in found.Min) (code[i].opcode, code[i].operand) = (OpCodes.Ldsfld, minField);
            foreach (var i in found.Max) (code[i].opcode, code[i].operand) = (OpCodes.Ldsfld, maxField);
            _limitSwapped = true;
            return code;
        }

        private static List<(OpCode, object?)> Code(IEnumerable<CodeInstruction> code) => code.Select(i => (i.opcode, (object?)i.operand)).ToList();

        // ---- Helpers ----

        private static FieldInfo IncubationField(Type globals) =>
            _incubation ??= AccessTools.Field(globals, "incubationGeneticSizeMinMax") ?? throw new MissingFieldException(Globals, "incubationGeneticSizeMinMax");

        private static (double Min, double Max) Range(Vector2 v) => (v.x, v.y);

        private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static object? Get(object target, string name) => AccessTools.Field(target.GetType(), name)?.GetValue(target);

        private static void Set(object target, string name, object value) =>
            (AccessTools.Field(target.GetType(), name) ?? throw new MissingFieldException(target.GetType().FullName, name)).SetValue(target, value);
    }

    /// <summary>
    /// The size limit the patched AnimalNative.CurrentScale reads. Its own class with no static initialiser, so the read stays a plain
    /// static field load on that hot path (no type-initialisation check).
    /// </summary>
    internal static class ScaleLimit
    {
        internal static float Min, Max;
    }
}
