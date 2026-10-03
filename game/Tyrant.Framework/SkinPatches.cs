using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>Unlocks added skins, hides stand-ins, scrolls the Nursery row, re-rolls random stand-ins, clamps unknown saved numbers.</summary>
    internal static class SkinPatches
    {
        private static bool _clampAnnounced;

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            Patch(harmony, "PrehistoricKingdom.ScenarioManager", "IsRewardUnlocked", nameof(Unlocked), postfix: true);
            Patch(harmony, "PrehistoricKingdom.NurseryMenuV2", "SetCreationRect", nameof(AfterSkinRow), postfix: true);
            Patch(harmony, "PrehistoricKingdom.NurseryMenuV2", "GenerateNewAnimal", nameof(AfterGenerate), postfix: true);
            // AnimalGenetics is a struct, so its Load cannot be prefixed with an object __instance; every path that gives an animal
            // its skin number (save load via AnimalGenetics.Load, SetAnimalVisuals, eggs) sets CurrentSkinIDX, so the clamp sits there.
            PatchSetter(harmony, "PrehistoricKingdom.Animal");
            PatchSetter(harmony, "PrehistoricKingdom.VivariumAnimal");
        }

        private static void PatchSetter(HarmonyLib.Harmony harmony, string typeName)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                var setter = type == null ? null : AccessTools.PropertySetter(type, "CurrentSkinIDX");
                if (setter == null)
                {
                    FrameworkMod.Log.Warning($"{typeName}.CurrentSkinIDX was not found (game updated?); unknown saved skin numbers are not checked.");
                    return;
                }
                harmony.Patch(setter, prefix: new HarmonyMethod(typeof(SkinPatches), nameof(BeforeSetSkin)));
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"{typeName}.CurrentSkinIDX could not be patched (game updated?); unknown saved skin numbers are not checked: {ex.GetBaseException().Message}");
            }
        }

        /// <summary>Each patch stands alone: a game update that renames, overloads or re-signs one method turns off only that part.</summary>
        private static void Patch(HarmonyLib.Harmony harmony, string typeName, string methodName, string patch, bool postfix)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                var method = type == null ? null : AccessTools.Method(type, methodName);
                if (method == null)
                {
                    FrameworkMod.Log.Warning($"{typeName}.{methodName} was not found (game updated?); one part of added skins is off.");
                    return;
                }
                var harmonyMethod = new HarmonyMethod(typeof(SkinPatches), patch);
                if (postfix) harmony.Patch(method, postfix: harmonyMethod);
                else harmony.Patch(method, prefix: harmonyMethod);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"{typeName}.{methodName} could not be patched (game updated?); one part of added skins is off: {ex.GetBaseException().Message}");
            }
        }

        private static void Unlocked(object reward, ref bool __result)
        {
            if (SkinsModule.IsAdded(reward)) __result = true;
            else if (SkinsModule.IsStandIn(reward)) __result = false;
        }

        private static void AfterSkinRow(object __instance)
        {
            try
            {
                NurseryScroll.Ensure(__instance);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The Nursery skin row could not be made scrollable: " + ex.Message);
            }
        }

        private static void AfterGenerate(object __instance, object __result)
        {
            try
            {
                if (__result == null) return;
                var data = Traverse.Create(__instance).Property("CurrentSelectedAnimalData").GetValue();
                var skin = Traverse.Create(__result).Field("skinIdx");
                if (!SkinsModule.IsStandInIndex(data, skin.GetValue<int>())) return;
                var count = (Traverse.Create(data).Field("skinsData").GetValue() as IList)?.Count ?? 0;
                var choices = Enumerable.Range(0, count).Where(i => !SkinsModule.IsStandInIndex(data, i)).ToList();
                if (choices.Count == 0) return;
                skin.SetValue(choices[UnityEngine.Random.Range(0, choices.Count)]);
                AccessTools.Method(__instance.GetType(), "GenerateNewSkinVariation")?.Invoke(__instance, new[] { __result });
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("A random skin could not be re-rolled: " + ex.Message);
            }
        }

        /// <summary>A skin number past the species' list (its mod's numbers were lost) becomes skin 0 instead of breaking the animal.</summary>
        private static void BeforeSetSkin(object __instance, ref int value)
        {
            try
            {
                var data = Traverse.Create(__instance).Property("BaseData").GetValue();
                var count = data == null ? 0 : (Traverse.Create(data).Field("skinsData").GetValue() as IList)?.Count ?? 0;
                if (count == 0 || (value >= 0 && value < count)) return;
                if (!_clampAnnounced) FrameworkMod.Log.Warning($"An animal wore skin number {value}, which does not exist now; it shows skin 0.");
                _clampAnnounced = true;
                value = 0;
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("A skin number could not be checked: " + ex.Message);
            }
        }
    }

    /// <summary>Makes the Nursery's skin row scroll sideways when it holds more than 5 visible skins (uGUI by reflection).</summary>
    internal static class NurseryScroll
    {
        private static readonly Type? ScrollRect = AccessTools.TypeByName("UnityEngine.UI.ScrollRect");
        private static readonly Type? RectMask = AccessTools.TypeByName("UnityEngine.UI.RectMask2D");
        private static readonly Type? Fitter = AccessTools.TypeByName("UnityEngine.UI.ContentSizeFitter");

        public static void Ensure(object menu)
        {
            if (ScrollRect == null) return;
            var template = Traverse.Create(menu).Field("templateSkinToggle").GetValue() as Component;
            var toggles = Traverse.Create(menu).Field("skinToggles").GetValue() as IEnumerable;
            if (template == null || toggles == null) return;
            var visible = toggles.Cast<object>().OfType<Component>().Count(t => t != null && t.gameObject.activeSelf);
            if (visible <= 5) return;
            if (!(template.transform.parent is RectTransform content) || !(content.parent is RectTransform viewport)) return;

            var scroll = viewport.GetComponent(ScrollRect) ?? viewport.gameObject.AddComponent(ScrollRect);
            var s = Traverse.Create(scroll);
            s.Property("content").SetValue(content);
            s.Property("horizontal").SetValue(true);
            s.Property("vertical").SetValue(false);
            s.Property("scrollSensitivity").SetValue(30f);
            if (RectMask != null && viewport.GetComponent(RectMask) == null) viewport.gameObject.AddComponent(RectMask);
            if (Fitter != null)
            {
                var fitter = content.GetComponent(Fitter) ?? content.gameObject.AddComponent(Fitter);
                var mode = Fitter.GetNestedType("FitMode");
                if (mode != null) Traverse.Create(fitter).Property("horizontalFit").SetValue(Enum.ToObject(mode, 2)); // PreferredSize
            }
        }
    }
}
