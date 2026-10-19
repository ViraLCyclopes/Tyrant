using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace Tyrant.Framework
{
    /// <summary>
    /// Shared helpers for windows built from the game's own uGUI and TextMeshPro, by reflection (the repo has no game or UI
    /// assemblies): clones of the game's pieces, plain rect children, click handlers, texts, colours and icons drawn at runtime.
    /// </summary>
    internal static class GameUi
    {
        /// <summary>The Nursery panels' grey, and the green of their top strip.</summary>
        public static readonly Color Panel = new Color(0.259f, 0.286f, 0.310f, 0.98f);
        public static readonly Color Green = new Color(0.345f, 0.678f, 0.184f, 1f);

        public static readonly Type? Image = AccessTools.TypeByName("UnityEngine.UI.Image");
        public static readonly Type? Button = AccessTools.TypeByName("UnityEngine.UI.Button");
        public static readonly Type? ScrollRect = AccessTools.TypeByName("UnityEngine.UI.ScrollRect");
        public static readonly Type? RectMask = AccessTools.TypeByName("UnityEngine.UI.RectMask2D");
        public static readonly Type? GridGroup = AccessTools.TypeByName("UnityEngine.UI.GridLayoutGroup");
        public static readonly Type? Fitter = AccessTools.TypeByName("UnityEngine.UI.ContentSizeFitter");

        /// <summary>Clones a game object inactive, without its localisation components (they would overwrite our texts).</summary>
        public static GameObject CloneInactive(GameObject source, Transform parent)
        {
            var wasActive = source.activeSelf;
            source.SetActive(false);
            GameObject clone;
            try
            {
                clone = UnityEngine.Object.Instantiate(source, parent, false);
            }
            finally
            {
                source.SetActive(wasActive);
            }
            foreach (var component in clone.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "PKLocalizationGUI").ToList())
                UnityEngine.Object.DestroyImmediate(component);
            return clone;
        }

        public static RectTransform Child(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>Replaces a button's click listeners (the original's included) with <paramref name="action"/>.</summary>
        public static void OnClick(GameObject button, Action action)
        {
            var component = button.GetComponent(Button!);
            var property = AccessTools.Property(Button!, "onClick");
            if (component == null || property == null) return;
            var clicked = (UnityEvent)Activator.CreateInstance(property.PropertyType); // drops the original's listeners
            clicked.AddListener(() => action());
            property.SetValue(component, clicked, null);
            Traverse.Create(component).Property("interactable").SetValue(true);
        }

        /// <summary>Puts a sprite on a button's icon (its first child image).</summary>
        public static void SetIcon(Transform button, Sprite sprite)
        {
            for (var i = 0; i < button.childCount; i++)
                if (button.GetChild(i).GetComponent(Image!) is Component icon)
                {
                    Traverse.Create(icon).Property("sprite").SetValue(sprite);
                    return;
                }
        }

        /// <summary>The TextMeshPro component on a transform, if any.</summary>
        public static Component? Text(Transform? transform) =>
            transform == null ? null : transform.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().FullName?.StartsWith("TMPro.") == true);

        public static void SetText(Transform? transform, string text)
        {
            var component = Text(transform);
            if (component != null) Traverse.Create(component).Property("text").SetValue(text);
        }

        /// <summary>A centred, ellipsised small text (its size fitter removed, so the rect decides).</summary>
        public static void Center(Component? text)
        {
            if (text == null) return;
            var fitter = Fitter == null ? null : text.GetComponent(Fitter);
            if (fitter != null) UnityEngine.Object.DestroyImmediate(fitter);
            var t = Traverse.Create(text);
            var alignment = AccessTools.Property(text.GetType(), "alignment")?.PropertyType;
            if (alignment != null) t.Property("alignment").SetValue(Enum.Parse(alignment, "Center"));
            var overflow = AccessTools.Property(text.GetType(), "overflowMode")?.PropertyType;
            if (overflow != null) t.Property("overflowMode").SetValue(Enum.Parse(overflow, "Ellipsis"));
            t.Property("fontSize").SetValue(11f);
        }

        public static void SetColor(Component graphic, Color color) => Traverse.Create(graphic).Property("color").SetValue(color);

        /// <summary>A white-on-clear square sprite of <paramref name="size"/> pixels, drawn once by the caller (y runs up).</summary>
        public static Sprite DrawIcon(int size, Func<int, int, bool> inside, string name)
        {
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                pixels[y * size + x] = inside(x, y) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
