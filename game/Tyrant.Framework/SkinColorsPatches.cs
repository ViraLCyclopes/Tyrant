using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    internal static partial class SkinColorsModule
    {
        private static readonly System.Random Random = new System.Random();
        private static bool _keptAnnounced;

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            Hook(harmony, "PrehistoricKingdom.NurseryMenuV2", m => m.Name == "GenerateNewSkinVariation", nameof(AfterNurseryVariation), postfix: true);
            Hook(harmony, "PrehistoricKingdom.VirtualAnimal", m => m.Name == "CreateVirtualAnimal" && m.IsStatic && m.ReturnType.Name == "VirtualAnimal", nameof(AfterCreateVirtualAnimal), postfix: true);
            foreach (var type in new[] { "PrehistoricKingdom.Animal", "PrehistoricKingdom.VivariumAnimal" })
                Hook(harmony, type, m => m.Name == "set_SkinVariationRuntime", nameof(BeforeSetVariation), postfix: false);
        }

        private static void Hook(HarmonyLib.Harmony harmony, string typeName, Func<MethodInfo, bool> match, string patch, bool postfix)
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
                    if (postfix) harmony.Patch(method, postfix: harmonyMethod);
                    else harmony.Patch(method, prefix: harmonyMethod);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"{typeName}: skin pattern colours could not be hooked; they are off there: {ex.GetBaseException().Message}");
            }
        }

        /// <summary>The Nursery made new colours for a VirtualAnimal: give a pattern skin's normal animal its pattern colours.</summary>
        private static void AfterNurseryVariation(object animal) => FillVirtual(animal);

        private static void AfterCreateVirtualAnimal(object __result) => FillVirtual(__result);

        private static void FillVirtual(object? animal)
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
                Fill(box, pattern, asset!, keepStored: false);
                field.SetValue(animal, box);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Pattern colours could not be given to a new animal: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>
        /// Breeding and loading reset geneticsOn for normal animals (ClampValues) before this setter: turn it back on for a pattern
        /// skin, keeping colours stored before and filling them for an animal that never had them.
        /// </summary>
        private static void BeforeSetVariation(object __instance, object[] __args)
        {
            try
            {
                if (__args.Length == 0 || __args[0] == null || !IsBase(Traverse.Create(__instance).Property("SkinType").GetValue())) return;
                var skin = Traverse.Create(__instance).Property("SkinData").GetValue();
                var male = string.Equals(Traverse.Create(__instance).Property("Sex").GetValue()?.ToString(), "Male", StringComparison.Ordinal);
                var asset = skin == null ? null : Traverse.Create(skin).Field(male ? "maleVariationData" : "femaleVariationData").GetValue();
                if (!TryGetPattern(asset, out var pattern)) return;
                var box = __args[0];
                Fill(box, pattern, asset!, keepStored: true);
                __args[0] = box;
                if (!_keptAnnounced) FrameworkMod.Log.Msg("Skin pattern colours are applied to normal animals (and kept on load).");
                _keptAnnounced = true;
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Pattern colours could not be kept for an animal: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>Writes pattern colours into a boxed AnimalSkinVariationRuntime and turns the genetics colouring on.</summary>
        private static void Fill(object box, SkinColorSet pattern, object asset, bool keepStored)
        {
            var type = box.GetType();
            var storedA = AccessTools.Field(type, "patternColorA")?.GetValue(box) is Color c ? c.a : 0f;
            if (!keepStored || PatternFill.NeedsColours(storedA))
            {
                var v = PatternFill.Create(pattern, Random);
                var (eyeFeather, secondaryFeather) = Feathers(asset);
                AccessTools.Field(type, "patternColorA")?.SetValue(box, ToColor(v.A));
                AccessTools.Field(type, "patternColorB")?.SetValue(box, ToColor(v.B));
                AccessTools.Field(type, "patternColorSecondary")?.SetValue(box, ToColor(v.Secondary));
                AccessTools.Field(type, "patternColorEye")?.SetValue(box, ToColor(v.Eye));
                AccessTools.Field(type, "baseOverride")?.SetValue(box, v.Strength);
                AccessTools.Field(type, "patternsFeathers")?.SetValue(box, new Vector3(v.Softness, eyeFeather, secondaryFeather));
            }
            AccessTools.Field(type, "geneticsOn")?.SetValue(box, true);
        }

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
