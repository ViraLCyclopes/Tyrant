using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using MelonLoader.Utils;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Temporary development aid: writes the Nursery's UI hierarchy around the skin row to UserData/Tyrant/nursery-ui.txt once per
    /// session, so in-game UI (the skin browser) can be placed from the real layout. Remove before release.
    /// </summary>
    internal static class UiDump
    {
        private static bool _done;

        public static void Once(object menu)
        {
            if (_done) return;
            _done = true;
            try
            {
                var text = new StringBuilder();
                var root = ((Component)menu).transform;
                text.AppendLine("== path from the skin row up to the root");
                var template = Traverse.Create(menu).Field("templateSkinToggle").GetValue() as Component;
                for (var t = template?.transform; t != null; t = t.parent) text.AppendLine(Describe(t));
                foreach (var field in new[] { "animalCreationRect", "randomizeSkinToggle", "skinNameText", "gallerySearchInputField", "accessCustomizationToggle", "createAnimalButton" })
                {
                    var value = Traverse.Create(menu).Field(field).GetValue();
                    var transform = value is Component c ? c.transform : (value as GameObject)?.transform;
                    text.AppendLine();
                    text.AppendLine($"== {field}: {(transform == null ? "null" : Path(transform))}");
                    if (transform != null) Tree(transform, 0, field == "animalCreationRect" ? 6 : 3, text);
                }
                text.AppendLine();
                text.AppendLine("== objects named like close/exit under the menu's canvas");
                var canvas = root.GetComponentInParent<Canvas>()?.rootCanvas?.transform ?? root;
                foreach (var t in canvas.GetComponentsInChildren<Transform>(true).Where(t => t.name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("exit", StringComparison.OrdinalIgnoreCase) >= 0).Take(30))
                    text.AppendLine($"{Path(t)} :: {Describe(t)}");
                var path = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Tyrant", "nursery-ui.txt");
                File.WriteAllText(path, text.ToString());
                FrameworkMod.Log.Msg("Nursery UI layout written to " + path);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The Nursery UI layout could not be written: " + ex.Message);
            }
        }

        private static void Tree(Transform t, int depth, int max, StringBuilder text)
        {
            text.AppendLine(new string(' ', depth * 2) + Describe(t));
            if (depth >= max) return;
            for (var i = 0; i < t.childCount; i++) Tree(t.GetChild(i), depth + 1, max, text);
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        private static string Describe(Transform t)
        {
            var parts = new StringBuilder($"{t.name} [{(t.gameObject.activeSelf ? "on" : "off")}]");
            if (t is RectTransform r)
                parts.Append($" rect={r.rect.width:0}x{r.rect.height:0} anchors=({r.anchorMin.x:0.##},{r.anchorMin.y:0.##})-({r.anchorMax.x:0.##},{r.anchorMax.y:0.##}) pos=({r.anchoredPosition.x:0},{r.anchoredPosition.y:0}) size=({r.sizeDelta.x:0},{r.sizeDelta.y:0})");
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                var type = c.GetType();
                var name = type.Name;
                var tr = Traverse.Create(c);
                if (name == "Image") name += $"(sprite={(tr.Property("sprite").GetValue() as UnityEngine.Object)?.name}, type={tr.Property("type").GetValue()}, color={tr.Property("color").GetValue()})";
                else if (type.FullName?.StartsWith("TMPro.") == true && tr.Property("text").GetValue() is string s) name += $"(\"{s}\", size={tr.Property("fontSize").GetValue()})";
                else if (name.EndsWith("LayoutGroup")) name += $"(spacing={tr.Property("spacing").GetValue()}, padding={tr.Property("padding").GetValue()}, ctrlW={tr.Property("childControlWidth").GetValue()}, expandW={tr.Property("childForceExpandWidth").GetValue()})";
                else if (name == "LayoutElement") name += $"(min={tr.Property("minWidth").GetValue()}x{tr.Property("minHeight").GetValue()}, pref={tr.Property("preferredWidth").GetValue()}x{tr.Property("preferredHeight").GetValue()}, flex={tr.Property("flexibleWidth").GetValue()}x{tr.Property("flexibleHeight").GetValue()})";
                parts.Append(' ').Append(name);
            }
            return parts.ToString();
        }
    }
}
