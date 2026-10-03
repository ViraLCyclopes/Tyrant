using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Pattern colours on normal animals of a pattern-coloured skin. The game keeps an animal's colours in the
    /// AnimalSkinVariationRuntime struct and resets its genetics colouring for normal animals in two places:
    /// ClampValues (on load, vivarium load and breeding, right before the material is built from it) and the Nursery's
    /// UpdateAnimalPreview (on every skin pick, before the preview and placement read the VirtualAnimal). A transpiler on
    /// ClampValues and prefixes where the preview and placement read the VirtualAnimal put the colours back. A struct cannot be
    /// patched through an object __instance (and __args changes to it are not written back), hence the transpiler.
    /// </summary>
    internal static partial class SkinColorsModule
    {
        private static readonly System.Random Random = new System.Random();
        private static bool _keptAnnounced;

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            Hook(harmony, "PrehistoricKingdom.NurseryMenuV2", m => m.Name == "GenerateNewSkinVariation", nameof(AfterNurseryVariation), HookKind.Postfix);
            Hook(harmony, "PrehistoricKingdom.VirtualAnimal",
                m => m.Name == "CreateVirtualAnimal" && m.IsStatic && m.GetParameters().FirstOrDefault()?.ParameterType.Name == "BaseAnimalData",
                nameof(AfterCreateVirtualAnimal), HookKind.Postfix);
            Hook(harmony, "PrehistoricKingdom.AnimalPreviewComponent", m => m.Name == "CreateAnimalPreview" || m.Name == "UpdateAnimalPreview", nameof(BeforePreview), HookKind.Prefix);
            Hook(harmony, "PrehistoricKingdom.AnimalPlacementPipeline", m => m.Name == "ApplyPostPlacementStartStateForNewPlacement", nameof(BeforePlacement), HookKind.Prefix);
            Hook(harmony, "PrehistoricKingdom.AnimalSkinVariationRuntime", m => m.Name == "ClampValues" && m.GetParameters().Length == 2, nameof(ClampTranspiler), HookKind.Transpiler);
        }

        private enum HookKind
        {
            Prefix,
            Postfix,
            Transpiler,
        }

        private static void Hook(HarmonyLib.Harmony harmony, string typeName, Func<MethodInfo, bool> match, string patch, HookKind kind)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                var methods = type == null ? Array.Empty<MethodInfo>() : AccessTools.GetDeclaredMethods(type).Where(match).ToArray();
                if (methods.Length == 0)
                {
                    FrameworkMod.Log.Warning($"{typeName}: no method for skin pattern colours was found (game updated?); pattern colours on normal animals are off there.");
                    return;
                }
                var harmonyMethod = new HarmonyMethod(typeof(SkinColorsModule), patch);
                foreach (var method in methods)
                {
                    if (kind == HookKind.Prefix) harmony.Patch(method, prefix: harmonyMethod);
                    else if (kind == HookKind.Postfix) harmony.Patch(method, postfix: harmonyMethod);
                    else harmony.Patch(method, transpiler: harmonyMethod);
                }
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"{typeName}: skin pattern colours could not be hooked; they are off there: {ex.GetBaseException().Message}");
            }
        }

        // ---- Nursery and spawns (VirtualAnimal is a class) ----

        /// <summary>The Nursery made new colours: give a pattern skin's normal animal new pattern colours.</summary>
        private static void AfterNurseryVariation(object animal) => FillVirtual(animal, keepStored: false);

        private static void AfterCreateVirtualAnimal(object __result) => FillVirtual(__result, keepStored: false);

        /// <summary>UpdateAnimalPreview resets the genetics colouring on every skin pick: put it back before the preview reads it.</summary>
        private static void BeforePreview(object animal) => FillVirtual(animal, keepStored: true);

        private static void BeforePlacement(object virtualAnimal) => FillVirtual(virtualAnimal, keepStored: true);

        private static void FillVirtual(object? animal, bool keepStored)
        {
            try
            {
                // VirtualAnimal's SkinType, Sex, OwnAD and skinIdx are public fields (Animal's are properties).
                if (animal == null || !IsBase(Traverse.Create(animal).Field("SkinType").GetValue())) return;
                var asset = AssetOf(Traverse.Create(animal).Field("OwnAD").GetValue(),
                    Traverse.Create(animal).Field("skinIdx").GetValue<int>(), Traverse.Create(animal).Field("Sex").GetValue());
                if (!TryGetPattern(asset, out var pattern)) return;
                var field = AccessTools.Field(animal.GetType(), "variationRuntimeData");
                if (field == null) return;
                var box = field.GetValue(animal);
                Fill(box, pattern, asset!, keepStored);
                field.SetValue(animal, box);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Pattern colours could not be given to an animal in the Nursery: " + ex.GetBaseException().Message);
            }
        }

        // ---- Load, vivarium load and breeding: ClampValues ----

        /// <summary>Before every return of ClampValues: this = AfterClamp((object)this, variationAsset, (object)skinType).</summary>
        private static IEnumerable<CodeInstruction> ClampTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var runtime = original.DeclaringType!;
            var skinType = original.GetParameters()[1].ParameterType;
            var after = AccessTools.Method(typeof(SkinColorsModule), nameof(AfterClamp));
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
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldobj, runtime);
                    yield return new CodeInstruction(OpCodes.Box, runtime);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Box, skinType);
                    yield return new CodeInstruction(OpCodes.Call, after);
                    yield return new CodeInstruction(OpCodes.Unbox_Any, runtime);
                    yield return new CodeInstruction(OpCodes.Stobj, runtime);
                }
                yield return instruction;
            }
        }

        /// <summary>ClampValues just turned the genetics colouring off for a normal animal: back on for a pattern skin, stored colours kept.</summary>
        private static object AfterClamp(object runtime, object asset, object skinType)
        {
            try
            {
                if (!IsBase(skinType) || !TryGetPattern(asset, out var pattern)) return runtime;
                Fill(runtime, pattern, asset, keepStored: true);
                if (!_keptAnnounced) FrameworkMod.Log.Msg("Skin pattern colours were kept for a loaded or bred animal.");
                _keptAnnounced = true;
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Pattern colours could not be kept for an animal: " + ex.GetBaseException().Message);
            }
            return runtime;
        }

        // ---- shared ----

        /// <summary>Writes pattern colours into a boxed AnimalSkinVariationRuntime and turns the genetics colouring on.</summary>
        private static void Fill(object box, SkinColorSet pattern, object asset, bool keepStored)
        {
            var type = box.GetType();
            PatternValues v;
            if (keepStored)
            {
                var (a, alphaA) = Read(box, "patternColorA");
                var (b, alphaB) = Read(box, "patternColorB");
                var (secondary, alphaSecondary) = Read(box, "patternColorSecondary");
                var (eye, alphaEye) = Read(box, "patternColorEye");
                var strength = AccessTools.Field(type, "baseOverride")?.GetValue(box) is float s ? s : 0f;
                var softness = AccessTools.Field(type, "patternsFeathers")?.GetValue(box) is Vector3 f ? f.x : 0f;
                v = PatternFill.Keep(new StoredPattern(a, alphaA, b, alphaB, secondary, alphaSecondary, eye, alphaEye, strength, softness), pattern, Random);
            }
            else
            {
                v = PatternFill.Create(pattern, Random);
            }
            var (eyeFeather, secondaryFeather) = Feathers(asset);
            AccessTools.Field(type, "patternColorA")?.SetValue(box, ToColor(v.A));
            AccessTools.Field(type, "patternColorB")?.SetValue(box, ToColor(v.B));
            AccessTools.Field(type, "patternColorSecondary")?.SetValue(box, ToColor(v.Secondary));
            AccessTools.Field(type, "patternColorEye")?.SetValue(box, ToColor(v.Eye));
            AccessTools.Field(type, "baseOverride")?.SetValue(box, v.Strength);
            AccessTools.Field(type, "patternsFeathers")?.SetValue(box, new Vector3(v.Softness, eyeFeather, secondaryFeather));
            AccessTools.Field(type, "geneticsOn")?.SetValue(box, true);
        }

        private static (Rgb Colour, float Alpha) Read(object box, string field) =>
            AccessTools.Field(box.GetType(), field)?.GetValue(box) is Color c ? (new Rgb(c.r, c.g, c.b), c.a) : (default, 0f);

        private static object? AssetOf(object? speciesData, int skinIndex, object? sex)
        {
            if (speciesData == null || !(Traverse.Create(speciesData).Field("skinsData").GetValue() is System.Collections.IList skins) || skinIndex < 0 || skinIndex >= skins.Count) return null;
            var male = string.Equals(sex?.ToString(), "Male", StringComparison.Ordinal);
            return Traverse.Create(skins[skinIndex]).Field(male ? "maleVariationData" : "femaleVariationData").GetValue();
        }

        private static bool IsBase(object? skinType) => string.Equals(skinType?.ToString(), "Base", StringComparison.Ordinal);

        private static Color ToColor(Rgb c) => new Color(c.R, c.G, c.B, 1f);
    }
}
