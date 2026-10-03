using System;
using System.Collections;
using System.Collections.Generic;
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
            SkinBrowser.PatchEscape(harmony);
            SkinColorsModule.Patch(harmony);
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

        private static void AfterSkinRow(object __instance, bool rebuild)
        {
            try
            {
                NurseryScroll.Ensure(__instance, rebuild);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The Nursery skin row could not be made scrollable: " + ex.Message);
            }
            try
            {
                SkinBrowser.Refresh(__instance, rebuild);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The All skins button could not be updated: " + ex.Message);
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

    /// <summary>
    /// Makes the Nursery's skin row scroll sideways when it holds more than 5 visible skins (uGUI by reflection). The row moves into
    /// a new viewport that takes the row's exact old place, so the panel around it is unchanged. Inside it, swatches keep the width they have with 5 skins at
    /// the row's height and the row grows to fit them. With 5 or fewer, the row fills the viewport exactly as the game lays it out.
    /// </summary>
    internal static class NurseryScroll
    {
        private const string ViewportName = "TyrantSkinViewport";
        private const int Shown = 5;
        private static readonly Type? ScrollRect = AccessTools.TypeByName("UnityEngine.UI.ScrollRect");
        private static readonly Type? RectMask = AccessTools.TypeByName("UnityEngine.UI.RectMask2D");
        private static readonly Type? Fitter = AccessTools.TypeByName("UnityEngine.UI.ContentSizeFitter");
        private static readonly Type? LayoutElement = AccessTools.TypeByName("UnityEngine.UI.LayoutElement");
        private static readonly Type? LayoutGroup = AccessTools.TypeByName("UnityEngine.UI.HorizontalOrVerticalLayoutGroup");

        public static void Ensure(object menu, bool rebuild)
        {
            if (ScrollRect == null || RectMask == null || Fitter == null || LayoutElement == null) return;
            var template = Traverse.Create(menu).Field("templateSkinToggle").GetValue() as Component;
            var toggles = (Traverse.Create(menu).Field("skinToggles").GetValue() as IEnumerable)?.Cast<object>().OfType<Component>()
                .Where(t => t != null && t.gameObject.activeSelf).ToList();
            if (template == null || toggles == null || !(template.transform.parent is RectTransform content)) return;
            var viewport = content.parent as RectTransform;
            var wrapped = viewport != null && viewport.name == ViewportName;
            if (toggles.Count <= Shown)
            {
                if (wrapped) Fill(content, viewport!);
                return;
            }
            if (!wrapped) viewport = Wrap(content);
            Scroll(content, viewport!, toggles, template, rebuild);
        }

        /// <summary>Puts a viewport with the row's anchors, size and place between the row and its parent.</summary>
        private static RectTransform Wrap(RectTransform content)
        {
            var go = new GameObject(ViewportName, typeof(RectTransform)) { layer = content.gameObject.layer };
            var viewport = (RectTransform)go.transform;
            viewport.SetParent(content.parent, false);
            viewport.SetSiblingIndex(content.GetSiblingIndex());
            viewport.anchorMin = content.anchorMin;
            viewport.anchorMax = content.anchorMax;
            viewport.pivot = content.pivot;
            viewport.anchoredPosition = content.anchoredPosition;
            viewport.sizeDelta = content.sizeDelta;
            viewport.localScale = content.localScale;
            viewport.localRotation = content.localRotation;
            var element = content.GetComponent(LayoutElement!);
            if (element != null)
            {
                var copy = Traverse.Create(go.AddComponent(LayoutElement!));
                var from = Traverse.Create(element);
                foreach (var name in new[] { "ignoreLayout", "minWidth", "minHeight", "preferredWidth", "preferredHeight", "flexibleWidth", "flexibleHeight" })
                    copy.Property(name).SetValue(from.Property(name).GetValue());
            }
            content.SetParent(viewport, false);
            go.AddComponent(RectMask!);
            var scroll = Traverse.Create(go.AddComponent(ScrollRect!));
            scroll.Property("content").SetValue(content);
            scroll.Property("viewport").SetValue(viewport);
            scroll.Property("horizontal").SetValue(true);
            scroll.Property("vertical").SetValue(false);
            scroll.Property("scrollSensitivity").SetValue(30f);
            var movement = ScrollRect!.GetNestedType("MovementType");
            if (movement != null) scroll.Property("movementType").SetValue(Enum.ToObject(movement, 2)); // Clamped
            content.gameObject.AddComponent(Fitter!);
            return viewport;
        }

        /// <summary>The game's own layout: the row fills the viewport (which has the row's original rect); no scrolling.</summary>
        private static void Fill(RectTransform content, RectTransform viewport)
        {
            SetFit(content, 0); // Unconstrained
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 0.5f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            if (viewport.GetComponent(ScrollRect!) is Behaviour scroll) scroll.enabled = false;
        }

        private static void Scroll(RectTransform content, RectTransform viewport, List<Component> toggles, Component template, bool rebuild)
        {
            var spacing = LayoutGroup != null && content.GetComponent(LayoutGroup) is Component group ? Traverse.Create(group).Property("spacing").GetValue<float>() : 0f;
            var size = viewport.rect.width > 1f ? (viewport.rect.width - (Shown - 1) * spacing) / Shown : ((RectTransform)template.transform).rect.width; // as wide as the game's own with 5 skins
            if (size > 1f)
                foreach (var toggle in toggles)
                {
                    var element = Traverse.Create(toggle.GetComponent(LayoutElement!) ?? toggle.gameObject.AddComponent(LayoutElement!));
                    element.Property("minWidth").SetValue(size);
                    element.Property("preferredWidth").SetValue(size);
                    element.Property("flexibleWidth").SetValue(0f);
                }
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.sizeDelta = new Vector2(content.sizeDelta.x, 0f);
            SetFit(content, 2); // PreferredSize: the row is as wide as its swatches
            var scroll = viewport.GetComponent(ScrollRect!);
            if (scroll is Behaviour behaviour) behaviour.enabled = true;
            if (!rebuild) return;

            // A new species or a fresh row: show the selected swatch (the first 5 when it is among them).
            var selected = toggles.FindIndex(t => Traverse.Create(t).Property("isOn").GetValue<bool>());
            content.anchoredPosition = new Vector2(-Math.Max(0, selected - (Shown - 1)) * (Math.Max(size, 1f) + spacing), 0f);
            if (scroll != null) AccessTools.Method(scroll.GetType(), "StopMovement")?.Invoke(scroll, null);
        }

        private static void SetFit(RectTransform content, int fit)
        {
            var fitter = content.GetComponent(Fitter!);
            var mode = Fitter!.GetNestedType("FitMode");
            if (fitter != null && mode != null) Traverse.Create(fitter).Property("horizontalFit").SetValue(Enum.ToObject(mode, fit));
        }
    }
}
