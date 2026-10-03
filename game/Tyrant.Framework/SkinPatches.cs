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
            Patch(harmony, "AnimalsV2.AnimalGenetics", "Load", nameof(BeforeGeneticsLoad), postfix: false);
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
                if (choices.Count > 0) skin.SetValue(choices[UnityEngine.Random.Range(0, choices.Count)]);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("A random skin could not be re-rolled: " + ex.Message);
            }
        }

        private static void BeforeGeneticsLoad(object __instance, object animal)
        {
            try
            {
                var data = Traverse.Create(animal).Property("BaseData").GetValue();
                var count = (Traverse.Create(data).Field("skinsData").GetValue() as IList)?.Count ?? 0;
                var skin = Traverse.Create(__instance).Field("skinIdx");
                var index = skin.GetValue<int>();
                if (count == 0 || (index >= 0 && index < count)) return;
                skin.SetValue(0);
                if (!_clampAnnounced) FrameworkMod.Log.Warning($"A saved animal wore skin number {index}, which does not exist now; it shows skin 0.");
                _clampAnnounced = true;
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("A saved skin number could not be checked: " + ex.Message);
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
