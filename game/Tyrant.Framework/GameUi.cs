using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public static readonly Type? RawImage = AccessTools.TypeByName("UnityEngine.UI.RawImage");
        public static readonly Type? Button = AccessTools.TypeByName("UnityEngine.UI.Button");
        public static readonly Type? ScrollRect = AccessTools.TypeByName("UnityEngine.UI.ScrollRect");
        public static readonly Type? RectMask = AccessTools.TypeByName("UnityEngine.UI.RectMask2D");
        public static readonly Type? GridGroup = AccessTools.TypeByName("UnityEngine.UI.GridLayoutGroup");
        public static readonly Type? Fitter = AccessTools.TypeByName("UnityEngine.UI.ContentSizeFitter");
        public static readonly Type? Slider = AccessTools.TypeByName("UnityEngine.UI.Slider");
        public static readonly Type? VerticalGroup = AccessTools.TypeByName("UnityEngine.UI.VerticalLayoutGroup");
        public static readonly Type? LayoutElement = AccessTools.TypeByName("UnityEngine.UI.LayoutElement");
        public static readonly Type? Scaler = AccessTools.TypeByName("UnityEngine.UI.CanvasScaler");
        public static readonly Type? Raycaster = AccessTools.TypeByName("UnityEngine.UI.GraphicRaycaster");
        public static readonly Type? EventTrigger = AccessTools.TypeByName("UnityEngine.EventSystems.EventTrigger");
        public static readonly Type? TriggerType = AccessTools.TypeByName("UnityEngine.EventSystems.EventTriggerType");
        public static readonly Type? InputField = AccessTools.TypeByName("TMPro.TMP_InputField");

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

        // ---- widgets built from scratch (the game's font, our layout) ----

        /// <summary>A TextMeshPro text in the font of <paramref name="template"/> (any game text), not catching clicks.</summary>
        public static Component Label(Transform parent, Component template, string text, float size, string alignment = "Left")
        {
            var rect = Child(parent, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var label = rect.gameObject.AddComponent(template.GetType());
            var t = Traverse.Create(label);
            t.Property("font").SetValue(Traverse.Create(template).Property("font").GetValue());
            t.Property("fontSize").SetValue(size);
            t.Property("color").SetValue(Color.white);
            t.Property("raycastTarget").SetValue(false);
            t.Property("richText").SetValue(true);
            t.Property("enableWordWrapping").SetValue(false);
            var align = AccessTools.Property(label.GetType(), "alignment")?.PropertyType;
            if (align != null) t.Property("alignment").SetValue(Enum.Parse(align, alignment));
            var overflow = AccessTools.Property(label.GetType(), "overflowMode")?.PropertyType;
            if (overflow != null) t.Property("overflowMode").SetValue(Enum.Parse(overflow, "Ellipsis"));
            t.Property("text").SetValue(text);
            return label;
        }

        /// <summary>A flat button: an image of <paramref name="color"/> with a centred label; returns the button object and its label.</summary>
        public static (GameObject Button, Component Label) FlatButton(Transform parent, Component font, string text, Color color, Action onClick)
        {
            var rect = Child(parent, "Button", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var image = rect.gameObject.AddComponent(Image!);
            SetColor(image, color);
            var button = rect.gameObject.AddComponent(Button!);
            Traverse.Create(button).Property("targetGraphic").SetValue(image); // AddComponent does not run the editor's Reset
            OnClick(rect.gameObject, onClick);
            var label = Label(rect, font, text, 11f, "Center");
            ((RectTransform)label.transform).offsetMin = new Vector2(3f, 0f);
            ((RectTransform)label.transform).offsetMax = new Vector2(-3f, 0f);
            return (rect.gameObject, label);
        }

        /// <summary>A picture of <paramref name="texture"/> filling <paramref name="rect"/> (a RawImage); null without uGUI's RawImage.</summary>
        public static Component? Picture(RectTransform rect, Texture texture, bool catchesClicks)
        {
            if (RawImage == null) return null;
            var picture = rect.gameObject.AddComponent(RawImage);
            var t = Traverse.Create(picture);
            t.Property("texture").SetValue(texture);
            t.Property("raycastTarget").SetValue(catchesClicks);
            return picture;
        }

        /// <summary>Turns a button, slider or input field on or off (its Selectable's interactable).</summary>
        public static void SetInteractable(Component? selectable, bool on)
        {
            if (selectable != null) Traverse.Create(selectable).Property("interactable").SetValue(on);
        }

        /// <summary>A horizontal slider (track, green fill, white handle) from min to max; <paramref name="changed"/> gets every move.</summary>
        public static Component NewSlider(Transform parent, float min, float max, float value, Action<float> changed)
        {
            var rect = Child(parent, "Slider", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var track = Child(rect, "Background", new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            SetColor(track.gameObject.AddComponent(Image!), new Color(0.12f, 0.13f, 0.15f, 1f));
            var fillArea = Child(rect, "Fill Area", new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fillArea.offsetMin = new Vector2(2f, 0f);
            fillArea.offsetMax = new Vector2(-6f, 0f);
            var fill = Child(fillArea, "Fill", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 0f));
            SetColor(fill.gameObject.AddComponent(Image!), Green);
            var handleArea = Child(rect, "Handle Slide Area", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            handleArea.offsetMin = new Vector2(5f, 0f);
            handleArea.offsetMax = new Vector2(-5f, 0f);
            var handle = Child(handleArea, "Handle", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 0f));
            var handleImage = handle.gameObject.AddComponent(Image!);
            SetColor(handleImage, new Color(0.92f, 0.93f, 0.94f, 1f));

            var slider = rect.gameObject.AddComponent(Slider!);
            var s = Traverse.Create(slider);
            s.Property("fillRect").SetValue(fill);
            s.Property("handleRect").SetValue(handle);
            s.Property("targetGraphic").SetValue(handleImage);
            s.Property("wholeNumbers").SetValue(false);
            s.Property("minValue").SetValue(min);
            s.Property("maxValue").SetValue(max);
            s.Property("value").SetValue(value);
            if (s.Property("onValueChanged").GetValue() is UnityEvent<float> moved) moved.AddListener(v => changed(v));
            return slider;
        }

        /// <summary>Sets a slider without its listeners hearing it (a typed value, a reset).</summary>
        public static void SetSliderQuietly(Component slider, float value)
        {
            var method = AccessTools.Method(slider.GetType(), "SetValueWithoutNotify", new[] { typeof(float) });
            if (method != null) method.Invoke(slider, new object[] { value });
            else Traverse.Create(slider).Property("value").SetValue(value);
        }

        /// <summary>
        /// A TextMeshPro input box: a clone of <paramref name="template"/> (a game input field, its events replaced) when there is
        /// one, else built by reflection. <paramref name="endEdit"/> gets the text when the player presses Enter or leaves the box.
        /// </summary>
        public static Component? NewInput(Transform parent, Component? template, Component font, Action<string> endEdit)
        {
            if (InputField == null) return null;
            GameObject go;
            Component? input;
            if (template != null)
            {
                go = CloneInactive(template.gameObject, parent);
                input = go.GetComponent(template.GetType());
                var placeholder = input == null ? null : Traverse.Create(input).Property("placeholder").GetValue() as Component;
                if (placeholder != null) SetText(placeholder.transform, "");
            }
            else
            {
                var rect = Child(parent, "Input", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                go = rect.gameObject;
                go.SetActive(false); // TMP_InputField wants its text component before it is enabled
                var background = go.AddComponent(Image!);
                SetColor(background, new Color(0.12f, 0.13f, 0.15f, 1f));
                var area = Child(rect, "Text Area", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                area.offsetMin = new Vector2(4f, 1f);
                area.offsetMax = new Vector2(-4f, -1f);
                if (RectMask != null) area.gameObject.AddComponent(RectMask);
                var text = Label(area, font, "", 11f, "Left");
                input = go.AddComponent(InputField);
                var t = Traverse.Create(input);
                t.Property("textViewport").SetValue(area);
                t.Property("textComponent").SetValue(text);
                t.Property("targetGraphic").SetValue(background);
                t.Property("fontAsset").SetValue(Traverse.Create(font).Property("font").GetValue());
            }
            if (input == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }
            var box = (RectTransform)go.transform;
            box.anchorMin = Vector2.zero;
            box.anchorMax = Vector2.one;
            box.pivot = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = Vector2.zero;
            box.sizeDelta = Vector2.zero;
            foreach (var name in new[] { "onValueChanged", "onEndEdit", "onSubmit", "onSelect", "onDeselect" })
            {
                var property = AccessTools.Property(input.GetType(), name);
                if (property != null && property.CanWrite) property.SetValue(input, Activator.CreateInstance(property.PropertyType), null);
            }
            var i = Traverse.Create(input);
            i.Property("text").SetValue("");
            i.Property("characterLimit").SetValue(10);
            i.Property("pointSize").SetValue(11f); // the text and placeholder of a cloned (bigger) box
            var lineType = AccessTools.Property(input.GetType(), "lineType")?.PropertyType;
            if (lineType != null) i.Property("lineType").SetValue(Enum.Parse(lineType, "SingleLine"));
            if (i.Property("onEndEdit").GetValue() is UnityEvent<string> ended) ended.AddListener(text => endEdit(text ?? ""));
            go.SetActive(true);
            return input;
        }

        /// <summary>True while the player types in this input box (then a slider move does not overwrite the text).</summary>
        public static bool IsFocused(Component? input) => input != null && Traverse.Create(input).Property("isFocused").GetValue() is true;

        public static void SetInputText(Component? input, string text)
        {
            if (input == null) return;
            var method = AccessTools.Method(input.GetType(), "SetTextWithoutNotify", new[] { typeof(string) });
            if (method != null) method.Invoke(input, new object[] { text });
            else Traverse.Create(input).Property("text").SetValue(text);
        }

        /// <summary>A vertical stack whose children set their own heights (<see cref="SetHeight"/>), growing with its content.</summary>
        public static void Stack(RectTransform content, float spacing)
        {
            if (VerticalGroup == null) return;
            var group = Traverse.Create(content.gameObject.AddComponent(VerticalGroup));
            group.Property("spacing").SetValue(spacing);
            group.Property("childControlWidth").SetValue(true);
            group.Property("childControlHeight").SetValue(true);
            group.Property("childForceExpandWidth").SetValue(true);
            group.Property("childForceExpandHeight").SetValue(false);
            group.Property("padding").SetValue(new RectOffset(10, 10, 6, 10));
            if (Fitter == null) return;
            var fitter = content.gameObject.AddComponent(Fitter);
            var mode = Fitter.GetNestedType("FitMode");
            if (mode != null) Traverse.Create(fitter).Property("verticalFit").SetValue(Enum.ToObject(mode, 2)); // PreferredSize
        }

        public static void SetHeight(GameObject row, float height)
        {
            if (LayoutElement == null) return;
            var element = Traverse.Create(row.GetComponent(LayoutElement) ?? row.AddComponent(LayoutElement));
            element.Property("minHeight").SetValue(height);
            element.Property("preferredHeight").SetValue(height);
        }

        /// <summary>A vertical scroll view filling <paramref name="parent"/>; returns its content (a <see cref="Stack"/>).</summary>
        public static RectTransform ScrollView(Transform parent, string name)
        {
            var viewport = Child(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            SetColor(viewport.gameObject.AddComponent(Image!), new Color(0f, 0f, 0f, 0.01f)); // catches the wheel between rows
            if (RectMask != null) viewport.gameObject.AddComponent(RectMask);
            var content = Child(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            Stack(content, 4f);
            if (ScrollRect == null) return content;
            var scroll = Traverse.Create(viewport.gameObject.AddComponent(ScrollRect));
            scroll.Property("content").SetValue(content);
            scroll.Property("viewport").SetValue(viewport);
            scroll.Property("horizontal").SetValue(false);
            scroll.Property("vertical").SetValue(true);
            scroll.Property("scrollSensitivity").SetValue(30f);
            var movement = ScrollRect.GetNestedType("MovementType");
            if (movement != null) scroll.Property("movementType").SetValue(Enum.ToObject(movement, 2)); // Clamped
            return content;
        }

        // ---- pointer events (EventTrigger entries added by reflection; no interface-implementing MonoBehaviours) ----

        private sealed class Relay
        {
            private readonly Action<object> _action;

            public Relay(Action<object> action) => _action = action;

            // Bound to UnityAction<BaseEventData>: a reference-type parameter binds contravariantly to object.
            public void Invoke(object data) => _action(data);
        }

        private static readonly MethodInfo RelayInvoke = typeof(Relay).GetMethod(nameof(Relay.Invoke))!;

        /// <summary>Calls <paramref name="action"/> with the BaseEventData of <paramref name="eventId"/> ("Drag", "EndDrag", …) on that object.</summary>
        public static bool OnPointer(GameObject target, string eventId, Action<object> action)
        {
            if (EventTrigger == null || TriggerType == null) return false;
            var trigger = target.GetComponent(EventTrigger) ?? target.AddComponent(EventTrigger);
            var entryType = EventTrigger.GetNestedType("Entry");
            if (entryType == null || !(AccessTools.Property(EventTrigger, "triggers")?.GetValue(trigger, null) is IList triggers)) return false;
            var entry = Activator.CreateInstance(entryType);
            AccessTools.Field(entryType, "eventID").SetValue(entry, Enum.Parse(TriggerType, eventId));
            var callback = AccessTools.Field(entryType, "callback").GetValue(entry);
            var add = callback?.GetType().GetMethod("AddListener");
            if (callback == null || add == null) return false;
            add.Invoke(callback, new object[] { Delegate.CreateDelegate(add.GetParameters()[0].ParameterType, new Relay(action), RelayInvoke) });
            triggers.Add(entry);
            return true;
        }

        private static readonly Dictionary<Type, PropertyInfo?> Deltas = new Dictionary<Type, PropertyInfo?>();

        /// <summary>PointerEventData.delta (screen pixels since the last event); zero for other event data.</summary>
        public static Vector2 PointerDelta(object data)
        {
            var type = data.GetType();
            if (!Deltas.TryGetValue(type, out var delta)) Deltas[type] = delta = AccessTools.Property(type, "delta");
            return delta?.GetValue(data, null) is Vector2 v ? v : Vector2.zero;
        }

        private static readonly Dictionary<Type, PropertyInfo?> Positions = new Dictionary<Type, PropertyInfo?>();
        private static readonly Dictionary<Type, PropertyInfo?> Thresholds = new Dictionary<Type, PropertyInfo?>();

        /// <summary>
        /// On a press: PointerEventData.useDragThreshold off, so Drag starts with the first pixel moved (not after the
        /// EventSystem's threshold); the EventSystem turns it on again for the next press.
        /// </summary>
        public static void NoDragThreshold(object data)
        {
            var type = data.GetType();
            if (!Thresholds.TryGetValue(type, out var threshold)) Thresholds[type] = threshold = AccessTools.Property(type, "useDragThreshold");
            if (threshold != null && threshold.CanWrite) threshold.SetValue(data, false, null);
        }

        /// <summary>PointerEventData.position (the pointer in screen pixels); null for other event data.</summary>
        public static Vector2? PointerPosition(object data)
        {
            var type = data.GetType();
            if (!Positions.TryGetValue(type, out var position)) Positions[type] = position = AccessTools.Property(type, "position");
            return position?.GetValue(data, null) is Vector2 v ? v : (Vector2?)null;
        }
    }
}
