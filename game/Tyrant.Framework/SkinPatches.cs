using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Tyrant.Framework.Core;
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
            LookEditorWindow.NurseryChanged(); // a look editor opened from the Nursery looks at its preview animal now
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
    /// a new viewport that takes the row's exact old place, so the panel around it is unchanged. Inside it, a mask inset at both
    /// ends leaves room for small back and forward buttons that page 5 skins at a time (greyed out at their end); the mouse wheel
    /// and dragging scroll too. Swatches keep the width they have with 5 skins in the mask and the row grows to fit them. With 5
    /// or fewer, the buttons hide and the row fills the viewport exactly as the game lays it out.
    /// </summary>
    internal static class NurseryScroll
    {
        private const string ViewportName = "TyrantSkinViewport";
        private const string MaskName = "TyrantSkinMask";
        private const string BackName = "TyrantSkinBack", ForwardName = "TyrantSkinForward";
        private const int Shown = SkinRowPaging.PerPage;
        private const float ArrowWidth = 14f, ArrowGap = 2f;
        private static readonly Type? ScrollRect = AccessTools.TypeByName("UnityEngine.UI.ScrollRect");
        private static readonly Type? RectMask = AccessTools.TypeByName("UnityEngine.UI.RectMask2D");
        private static readonly Type? Fitter = AccessTools.TypeByName("UnityEngine.UI.ContentSizeFitter");
        private static readonly Type? LayoutElement = AccessTools.TypeByName("UnityEngine.UI.LayoutElement");
        private static readonly Type? LayoutGroup = AccessTools.TypeByName("UnityEngine.UI.HorizontalOrVerticalLayoutGroup");
        private static readonly Type? Rebuilder = AccessTools.TypeByName("UnityEngine.UI.LayoutRebuilder");

        // The one Nursery row: what the buttons page through.
        private static RectTransform? _content, _mask;
        private static Component? _scroll, _back, _forward;
        private static int _count;
        private static float _stride;
        private static Sprite? _backIcon, _forwardIcon;

        public static void Ensure(object menu, bool rebuild)
        {
            if (ScrollRect == null || RectMask == null || Fitter == null || LayoutElement == null) return;
            var template = Traverse.Create(menu).Field("templateSkinToggle").GetValue() as Component;
            var toggles = (Traverse.Create(menu).Field("skinToggles").GetValue() as IEnumerable)?.Cast<object>().OfType<Component>()
                .Where(t => t != null && t.gameObject.activeSelf).ToList();
            if (template == null || toggles == null || !(template.transform.parent is RectTransform content)) return;
            var mask = content.parent as RectTransform;
            var viewport = mask == null ? null : mask.parent as RectTransform;
            var wrapped = mask != null && mask.name == MaskName && viewport != null && viewport.name == ViewportName;
            if (!SkinRowPaging.Scrolls(toggles.Count))
            {
                if (wrapped) Fill(content, mask!, viewport!);
                return;
            }
            if (!wrapped) (viewport, mask) = Wrap(content);
            Scroll(content, mask!, viewport!, toggles, template, rebuild);
        }

        /// <summary>Puts a viewport with the row's anchors, size and place between the row and its parent, a mask inside it, and the buttons.</summary>
        private static (RectTransform Viewport, RectTransform Mask) Wrap(RectTransform content)
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
            var mask = GameUi.Child(viewport, MaskName, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            mask.gameObject.AddComponent(RectMask!);
            content.SetParent(mask, false);
            var scroll = go.AddComponent(ScrollRect!);
            var s = Traverse.Create(scroll);
            s.Property("content").SetValue(content);
            s.Property("viewport").SetValue(mask);
            s.Property("horizontal").SetValue(true);
            s.Property("vertical").SetValue(false);
            s.Property("scrollSensitivity").SetValue(30f);
            var movement = ScrollRect!.GetNestedType("MovementType");
            if (movement != null) s.Property("movementType").SetValue(Enum.ToObject(movement, 2)); // Clamped
            if (Members.Get(scroll, "onValueChanged") is UnityEngine.Events.UnityEvent<Vector2> moved) moved.AddListener(_ => UpdateButtons());
            content.gameObject.AddComponent(Fitter!);
            Arrow(viewport, back: true);
            Arrow(viewport, back: false);
            return (viewport, mask);
        }

        /// <summary>The game's own layout: the row fills the viewport (which has the row's original rect); no scrolling, no buttons.</summary>
        private static void Fill(RectTransform content, RectTransform mask, RectTransform viewport)
        {
            SetFit(content, 0); // Unconstrained
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 0.5f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            mask.offsetMin = Vector2.zero;
            mask.offsetMax = Vector2.zero;
            ShowButtons(viewport, false);
            if (viewport.GetComponent(ScrollRect!) is Behaviour scroll) scroll.enabled = false;
        }

        private static void Scroll(RectTransform content, RectTransform mask, RectTransform viewport, List<Component> toggles, Component template, bool rebuild)
        {
            const float inset = ArrowWidth + ArrowGap; // the buttons sit in the viewport's ends, the swatches in the mask between them
            mask.offsetMin = new Vector2(inset, 0f);
            mask.offsetMax = new Vector2(-inset, 0f);
            ShowButtons(viewport, true);
            var spacing = LayoutGroup != null && content.GetComponent(LayoutGroup) is Component group ? Traverse.Create(group).Property("spacing").GetValue<float>() : 0f;
            var size = mask.rect.width > 1f ? (mask.rect.width - (Shown - 1) * spacing) / Shown : ((RectTransform)template.transform).rect.width; // as wide as the game's own with 5 skins
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
            (_content, _mask, _scroll, _count, _stride) = (content, mask, scroll, toggles.Count, Math.Max(size, 1f) + spacing);
            if (rebuild)
            {
                // A new species or a fresh row: show the selected swatch (the first 5 when it is among them).
                var selected = toggles.FindIndex(t => Traverse.Create(t).Property("isOn").GetValue<bool>());
                content.anchoredPosition = new Vector2(-Math.Max(0, selected - (Shown - 1)) * _stride, 0f);
                if (scroll != null) AccessTools.Method(scroll.GetType(), "StopMovement")?.Invoke(scroll, null);
            }
            // The row's width comes from its size fitter: lay it out now so the buttons know where its end is.
            if (Rebuilder != null) AccessTools.Method(Rebuilder, "ForceRebuildLayoutImmediate", new[] { typeof(RectTransform) })?.Invoke(null, new object[] { content });
            UpdateButtons();
        }

        /// <summary>One page of 5 swatches back (-1) or on (+1), from the swatch nearest the row's left edge.</summary>
        private static void Page(int direction)
        {
            try
            {
                if (_content == null || _mask == null) return;
                var first = SkinRowPaging.FirstVisible(-_content.anchoredPosition.x, _stride, _count);
                var target = direction < 0 ? SkinRowPaging.PreviousFirst(first, _count) : SkinRowPaging.NextFirst(first, _count);
                if (_scroll != null) AccessTools.Method(_scroll.GetType(), "StopMovement")?.Invoke(_scroll, null);
                _content.anchoredPosition = new Vector2(-SkinRowPaging.Offset(target, _stride, MaxOffset()), _content.anchoredPosition.y);
                UpdateButtons();
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The Nursery skin row could not be paged: " + ex.Message);
            }
        }

        private static float MaxOffset() => _content == null || _mask == null ? 0f : Math.Max(0f, _content.rect.width - _mask.rect.width);

        private static void UpdateButtons()
        {
            if (_content == null) return;
            var offset = -_content.anchoredPosition.x;
            SetEnabled(_back, SkinRowPaging.CanGoBack(offset));
            SetEnabled(_forward, SkinRowPaging.CanGoForward(offset, MaxOffset()));
        }

        private static void SetEnabled(Component? button, bool enabled)
        {
            if (button == null) return;
            Members.Set(button, "interactable", enabled);
            var icon = button.transform.childCount > 0 && GameUi.Image != null ? button.transform.GetChild(0).GetComponent(GameUi.Image) : null;
            if (icon != null) GameUi.SetColor(icon, enabled ? Color.white : new Color(1f, 1f, 1f, 0.25f));
        }

        private static void ShowButtons(RectTransform viewport, bool show)
        {
            foreach (var name in new[] { BackName, ForwardName })
            {
                var button = viewport.Find(name);
                if (button != null) button.gameObject.SetActive(show);
            }
        }

        /// <summary>A small button filling the inset at one end of the row: a dim strip with a chevron.</summary>
        private static void Arrow(RectTransform viewport, bool back)
        {
            if (GameUi.Image == null || GameUi.Button == null) return;
            var end = back ? 0f : 1f;
            var rect = GameUi.Child(viewport, back ? BackName : ForwardName, new Vector2(end, 0f), new Vector2(end, 1f),
                new Vector2(end, 0.5f), Vector2.zero, new Vector2(ArrowWidth, 0f));
            var strip = rect.gameObject.AddComponent(GameUi.Image);
            GameUi.SetColor(strip, new Color(0f, 0f, 0f, 0.35f));
            var iconRect = GameUi.Child(rect, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ArrowWidth, ArrowWidth));
            var icon = iconRect.gameObject.AddComponent(GameUi.Image);
            Members.Set(icon, "sprite", back ? BackIcon() : ForwardIcon());
            Members.Set(icon, "raycastTarget", false);
            var button = rect.gameObject.AddComponent(GameUi.Button);
            Members.Set(button, "targetGraphic", strip);
            GameUi.OnClick(rect.gameObject, () => Page(back ? -1 : 1));
            if (back) _back = button; else _forward = button;
        }

        // A chevron 16 pixels high and about two thick, pointing left (back) or, mirrored, right.
        private static bool Chevron(int x, int y) => x >= 4 && x <= 11 && Math.Abs((x - 4) - Math.Abs(y - 7.5) * 0.9) < 1.2;
        private static Sprite BackIcon() => _backIcon != null ? _backIcon : _backIcon = GameUi.DrawIcon(16, Chevron, "Tyrant skin row back");
        private static Sprite ForwardIcon() => _forwardIcon != null ? _forwardIcon : _forwardIcon = GameUi.DrawIcon(16, (x, y) => Chevron(15 - x, y), "Tyrant skin row forward");

        private static void SetFit(RectTransform content, int fit)
        {
            var fitter = content.GetComponent(Fitter!);
            var mode = Fitter!.GetNestedType("FitMode");
            if (fitter != null && mode != null) Traverse.Create(fitter).Property("horizontalFit").SetValue(Enum.ToObject(mode, fit));
        }
    }
}
