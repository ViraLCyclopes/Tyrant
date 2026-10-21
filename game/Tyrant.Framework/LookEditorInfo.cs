using System;
using System.Collections.Generic;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;
using static Tyrant.Framework.GameUi;

namespace Tyrant.Framework
{
    /// <summary>
    /// The look editor's info buttons: a small round "i" at the end of a row; pressed, a short explanation opens under the row
    /// (one at a time per tab, kept across redraws), pressed again it closes.
    /// </summary>
    internal static partial class LookEditorWindow
    {
        private const float InfoSize = 16f;
        private static readonly InfoToggles Infos = new InfoToggles();
        private static Sprite? _circle;

        private static readonly Dictionary<string, string> InfoTexts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["variation"] = "Each animal's shimmer strength differs from the set strength by up to this share (0.15 = ±15%), so a herd doesn't look identical. Each animal keeps its own offset. The Nursery preview always shows the exact strength; the variation shows on the animals you create. Not available for one animal (This animal / Next created): there is nothing to vary between.",
            ["babyStrength"] = "How strong the shimmer is on a newborn. It slides from this to Strength as the animal grows (0 = no shimmer on babies, as in the game).",
            ["furAge"] = "Which ages have fur: Babies (fluff that goes away), Adults, Both, or Game's (the skin's own). An age without fur gets an all-black fur mask, the way the game removes baby fluff.",
            ["shaping"] = "How the shimmer changes with the viewing angle: Scale sets how strong the edge effect is, Power how sharply it falls off from the edges to the middle, Bias shifts it all brighter or darker.",
            ["mask"] = "Where the shimmer shows. White = full, black = none, grey = partial. A PNG mask is laid out like the skin texture. Everywhere = the whole body; Channel = taken from one channel of a skin map; Species = another species' mask.",
            ["colours"] = "The colours the shimmer cycles through as the angle changes: one colour gives a single tone, several give a blend.",
            ["grow"] = "Grows fur on skins the game gives none. Needs the game's Fur graphics setting on. Where the game has no fur mask, fur covers the whole body until you give it a mask.",
            ["length"] = "How long the fur is at its longest. The fur mask's red channel scales it per area: black = none, grey = shorter, white = this full length.",
            ["droop"] = "How much the fur hangs down instead of standing out from the skin. Droop X, Y and Z set the direction it falls towards.",
            ["wind"] = "How the fur sways in the wind: strength is how far it moves, speed how fast, scale how big the gusts are across the body.",
        };

        /// <summary>
        /// Puts an info button at the right end of the row just added (its first part, a label or a button, makes room) and,
        /// while it is open, the explanation under it. Call it right after building the row.
        /// </summary>
        private static void Info(string key)
        {
            if (_content == null || _font == null || _content.childCount == 0 || !InfoTexts.TryGetValue(key, out var text)) return;
            var row = (RectTransform)_content.GetChild(_content.childCount - 1);
            if (row.childCount == 0) return;
            var tab = _onFurTab ? "fur" : "iridescence";
            var open = Infos.IsOpen(tab, key);

            var first = (RectTransform)row.GetChild(0);
            var right = first.offsetMax.x;
            first.offsetMax = new Vector2(right - InfoSize - 4f, first.offsetMax.y);
            if (Text(first) is Component label) // a long name wraps instead of running under the button
            {
                Traverse.Create(label).Property("enableWordWrapping").SetValue(true);
                var overflow = AccessTools.Property(label.GetType(), "overflowMode")?.PropertyType;
                if (overflow != null) Traverse.Create(label).Property("overflowMode").SetValue(Enum.Parse(overflow, "Overflow"));
            }
            var (button, _) = FlatButton(row, _font, "<b>i</b>", open ? Green : Idle, Safe(() =>
            {
                Infos.Toggle(tab, key);
                Rebuild();
            }));
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(first.anchorMax.x, 0.5f);
            rect.anchorMax = new Vector2(first.anchorMax.x, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(right - 2f, 0f);
            rect.sizeDelta = new Vector2(InfoSize, InfoSize);
            if (button.GetComponent(Image!) is Component image)
                Traverse.Create(image).Property("sprite").SetValue(_circle != null ? _circle : _circle = Circle());
            if (open) Explanation(text);
        }

        /// <summary>The open explanation: a wrapped, muted line as tall as its text needs.</summary>
        private static void Explanation(string text)
        {
            var row = Row();
            var label = Label(row, _font!, text, 10f);
            Traverse.Create(label).Property("enableWordWrapping").SetValue(true);
            Traverse.Create(label).Property("color").SetValue(Faint);
            ((RectTransform)label.transform).offsetMin = new Vector2(6f, 0f);
            var width = Mathf.Max(120f, (_w?.Value ?? MinWidth) - 50f); // the window less its margins and the scroll bar
            var height = text.Length / Mathf.Max(1f, width / 5.2f) * 13f + 8f; // about 5 pixels a character at this size
            var measure = AccessTools.Method(label.GetType(), "GetPreferredValues", new[] { typeof(string), typeof(float), typeof(float) });
            try
            {
                if (measure?.Invoke(label, new object[] { text, width - 6f, 0f }) is Vector2 preferred && preferred.y > 0) height = preferred.y + 6f;
            }
            catch (System.Reflection.TargetInvocationException)
            {
                // the estimate stays
            }
            SetHeight(row.gameObject, Mathf.Ceil(height));
            row.sizeDelta = new Vector2(row.sizeDelta.x, Mathf.Ceil(height));
        }

        private static Sprite Circle() => DrawIcon(32, (x, y) =>
        {
            var dx = x - 15.5f;
            var dy = y - 15.5f;
            return dx * dx + dy * dy <= 15.5f * 15.5f;
        }, "Tyrant info circle");
    }
}
