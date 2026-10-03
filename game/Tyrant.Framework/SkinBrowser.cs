using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Tyrant.Framework
{
    /// <summary>
    /// The Nursery's "All skins" window: a grid button next to "Design" (shown when a species has more than 5 skins) opens a
    /// window on the left with every skin, a search box (skin or mod name) and an X. Clicking a skin selects it the way the row
    /// does: tick its row swatch, then the game's SelectSkin and SetCreationRect. Built from clones of the game's own UI pieces
    /// (uGUI and TextMeshPro by reflection; the repo has no game or UI assemblies).
    /// </summary>
    internal static class SkinBrowser
    {
        private const int RowLimit = 5;
        private const string ButtonName = "TyrantAllSkinsButton";
        private const string CloseButtonPath = "Menu/GalleryMenu/GalleryRect/SettingsHeader/AnimalFilterMenu/Header/CloseButton";
        private static readonly Color Panel = new Color(0.259f, 0.286f, 0.310f, 0.98f);
        private static readonly Color Green = new Color(0.345f, 0.678f, 0.184f, 1f);
        private static readonly Type? Image = AccessTools.TypeByName("UnityEngine.UI.Image");
        private static readonly Type? Button = AccessTools.TypeByName("UnityEngine.UI.Button");
        private static readonly Type? ScrollRect = AccessTools.TypeByName("UnityEngine.UI.ScrollRect");
        private static readonly Type? RectMask = AccessTools.TypeByName("UnityEngine.UI.RectMask2D");
        private static readonly Type? Grid = AccessTools.TypeByName("UnityEngine.UI.GridLayoutGroup");
        private static readonly Type? Fitter = AccessTools.TypeByName("UnityEngine.UI.ContentSizeFitter");
        private static readonly Type? ToggleUtil = AccessTools.TypeByName("PrehistoricKingdom.ToggleUtil") ?? AccessTools.TypeByName("ToggleUtil");

        private sealed class Cell
        {
            public Cell(int index, string name, string? modName, GameObject root, Component toggle)
            {
                Index = index;
                Name = name;
                ModName = modName;
                Root = root;
                Toggle = toggle;
            }

            public int Index { get; }
            public string Name { get; }
            public string? ModName { get; }
            public GameObject Root { get; }
            public Component Toggle { get; }
        }

        private static readonly List<Cell> Cells = new List<Cell>();
        private static object? _menu;
        private static GameObject? _window;
        private static RectTransform? _content;
        private static Component? _count;
        private static Component? _empty;
        private static Component? _search;
        private static Sprite? _gridIcon;
        private static string _query = "";

        private static bool IsOpen => _window != null && _window.activeSelf;

        /// <summary>After the game (re)builds the Design panel: place or hide the button; refresh an open window.</summary>
        public static void Refresh(object menu, bool rebuild)
        {
            if (Image == null || Button == null || ScrollRect == null || ToggleUtil == null) return;
            _menu = menu;
            var visible = Visible(menu).Count;
            PlaceButton(menu, visible > RowLimit);
            if (!IsOpen) return;
            if (visible <= RowLimit) Close();
            else if (rebuild) Fill(menu);
            else Highlight(menu);
        }

        /// <summary>Esc closes the window (legacy input may be unavailable; then only the X closes it).</summary>
        public static void Tick()
        {
            if (!IsOpen) return;
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Close();
            }
            catch (Exception)
            {
                // the game uses the new input system only
            }
        }

        private static void Open()
        {
            if (_menu == null) return;
            try
            {
                if (_window == null) Build(_menu);
                if (_window == null) return;
                _window.SetActive(true);
                _window.transform.SetAsLastSibling();
                Fill(_menu);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The All skins window could not be opened: " + ex.Message);
            }
        }

        private static void Close()
        {
            if (_window != null) _window.SetActive(false);
        }

        // ---- the button next to "Design" ----

        private static void PlaceButton(object menu, bool show)
        {
            var header = (Traverse.Create(menu).Field("skinNameText").GetValue() as Component)?.transform.parent as RectTransform; // DesignHeader
            if (header == null) return;
            var button = header.Find(ButtonName) as RectTransform;
            if (button == null)
            {
                if (!show) return;
                var source = (menu as Component)?.transform.Find(CloseButtonPath);
                if (source == null) return;
                button = (RectTransform)CloneInactive(source.gameObject, header).transform;
                button.name = ButtonName;
                SetIcon(button, GridIcon());
                OnClick(button.gameObject, Open);
                button.gameObject.SetActive(true);
            }
            button.gameObject.SetActive(show);
            if (!show) return;
            var label = header.Find("Header") as RectTransform; // the "Design" text; its width depends on the language
            var x = label == null ? 80f : label.anchoredPosition.x + label.rect.width * (1f - label.pivot.x) + 8f;
            button.anchorMin = button.anchorMax = new Vector2(0f, 0.5f);
            button.pivot = new Vector2(0f, 0.5f);
            button.anchoredPosition = new Vector2(x, 0f);
        }

        // ---- the window ----

        private static void Build(object menu)
        {
            var creation = Traverse.Create(menu).Field("animalCreationRect").GetValue() as GameObject;
            var nameText = Traverse.Create(menu).Field("skinNameText").GetValue() as Component;
            var searchSource = Traverse.Create(menu).Field("gallerySearchInputField").GetValue() as Component;
            var closeSource = (menu as Component)?.transform.Find(CloseButtonPath);
            if (creation == null || nameText == null || closeSource == null) return;

            var window = new GameObject("TyrantSkinBrowser", typeof(RectTransform)) { layer = creation.layer };
            var rect = (RectTransform)window.transform;
            rect.SetParent(creation.transform.parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -72f);
            rect.sizeDelta = new Vector2(600f, -120f);
            SetColor(window.AddComponent(Image!), Panel);

            var strip = Child(window.transform, "Strip", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 4f));
            SetColor(strip.gameObject.AddComponent(Image!), Green);

            // Header: a clone of the Design header ("All skins" on the left, the count on the right) with the game's X.
            var header = (RectTransform)CloneInactive(nameText.transform.parent.gameObject, window.transform).transform;
            var copiedButton = header.Find(ButtonName);
            if (copiedButton != null) UnityEngine.Object.DestroyImmediate(copiedButton.gameObject);
            header.name = "Header";
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = new Vector2(0f, -4f);
            header.sizeDelta = new Vector2(0f, 31f);
            SetText(header.Find("Header"), "All skins");
            var count = header.Find("Value") as RectTransform;
            if (count != null) count.anchoredPosition = new Vector2(-48f, count.anchoredPosition.y);
            _count = Text(count);
            header.gameObject.SetActive(true);
            var close = (RectTransform)CloneInactive(closeSource.gameObject, header).transform;
            close.anchorMin = close.anchorMax = new Vector2(1f, 0.5f);
            close.pivot = new Vector2(0.5f, 0.5f);
            close.anchoredPosition = new Vector2(-20f, 0f);
            OnClick(close.gameObject, Close);
            close.gameObject.SetActive(true);

            // Search: a clone of the species list's search field, with its events replaced.
            if (searchSource != null)
            {
                var field = (RectTransform)CloneInactive(searchSource.gameObject, window.transform).transform;
                field.anchorMin = new Vector2(0f, 1f);
                field.anchorMax = new Vector2(1f, 1f);
                field.pivot = new Vector2(0.5f, 1f);
                field.anchoredPosition = new Vector2(0f, -45f);
                field.sizeDelta = new Vector2(-24f, 40f);
                _search = field.GetComponent(searchSource.GetType());
                var input = Traverse.Create(_search);
                foreach (var name in new[] { "onValueChanged", "onEndEdit", "onSubmit", "onSelect", "onDeselect" })
                {
                    var property = AccessTools.Property(searchSource.GetType(), name);
                    if (property != null && property.CanWrite) property.SetValue(_search, Activator.CreateInstance(property.PropertyType), null);
                }
                input.Property("text").SetValue("");
                SetText((input.Property("placeholder").GetValue() as Component)?.transform, "Search skins or mods…");
                if (input.Property("onValueChanged").GetValue() is UnityEvent<string> changed) changed.AddListener(q => { _query = q ?? ""; Filter(); });
                field.gameObject.SetActive(true);
            }

            // The grid, in a vertical scroll view.
            var viewport = Child(window.transform, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            viewport.offsetMin = new Vector2(12f, 12f);
            viewport.offsetMax = new Vector2(-12f, searchSource != null ? -97f : -47f);
            if (RectMask != null) viewport.gameObject.AddComponent(RectMask);
            _content = Child(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            if (Grid != null)
            {
                var grid = Traverse.Create(_content.gameObject.AddComponent(Grid));
                grid.Property("cellSize").SetValue(new Vector2(84f, 112f));
                grid.Property("spacing").SetValue(new Vector2(8f, 10f));
            }
            if (Fitter != null)
            {
                var fitter = _content.gameObject.AddComponent(Fitter);
                var mode = Fitter.GetNestedType("FitMode");
                if (mode != null) Traverse.Create(fitter).Property("verticalFit").SetValue(Enum.ToObject(mode, 2)); // PreferredSize
            }
            var scroll = Traverse.Create(viewport.gameObject.AddComponent(ScrollRect!));
            scroll.Property("content").SetValue(_content);
            scroll.Property("viewport").SetValue(viewport);
            scroll.Property("horizontal").SetValue(false);
            scroll.Property("vertical").SetValue(true);
            scroll.Property("scrollSensitivity").SetValue(30f);
            var movement = ScrollRect!.GetNestedType("MovementType");
            if (movement != null) scroll.Property("movementType").SetValue(Enum.ToObject(movement, 2)); // Clamped

            var empty = CloneInactive(nameText.gameObject, viewport);
            var emptyRect = (RectTransform)empty.transform;
            emptyRect.anchorMin = new Vector2(0f, 1f);
            emptyRect.anchorMax = new Vector2(1f, 1f);
            emptyRect.pivot = new Vector2(0.5f, 1f);
            emptyRect.anchoredPosition = new Vector2(0f, -8f);
            emptyRect.sizeDelta = new Vector2(0f, 24f);
            _empty = Text(empty.transform);
            SetText(empty.transform, "No skins match your search.");
            Center(_empty);

            _window = window;
        }

        /// <summary>One cell per visible skin of the current species: its row swatch, cloned bigger, with its name underneath.</summary>
        private static void Fill(object menu)
        {
            if (_content == null) return;
            foreach (var cell in Cells) if (cell.Root != null) UnityEngine.Object.Destroy(cell.Root);
            Cells.Clear();
            var template = Traverse.Create(menu).Field("templateSkinToggle").GetValue() as Component;
            var nameText = Traverse.Create(menu).Field("skinNameText").GetValue() as Component;
            var data = Traverse.Create(menu).Property("CurrentSelectedAnimalData").GetValue();
            var skins = data == null ? null : Traverse.Create(data).Field("skinsData").GetValue() as IList;
            if (template == null || nameText == null || skins == null) return;
            var group = (IList)Activator.CreateInstance(AccessTools.Field(ToggleUtil!, "toggleGroup").FieldType);
            foreach (var index in Visible(menu))
            {
                var skin = skins[index];
                var name = Traverse.Create(skin).Field("skinName").GetValue() as string ?? $"Skin {index}";
                var root = new GameObject("Skin " + index, typeof(RectTransform)) { layer = _content.gameObject.layer };
                root.transform.SetParent(_content, false);

                var toggle = (RectTransform)UnityEngine.Object.Instantiate(template.gameObject, root.transform, false).transform; // the template is inactive, so is the clone
                toggle.anchorMin = new Vector2(0f, 1f);
                toggle.anchorMax = new Vector2(1f, 1f);
                toggle.pivot = new Vector2(0.5f, 1f);
                toggle.anchoredPosition = Vector2.zero;
                toggle.sizeDelta = new Vector2(0f, 84f);
                if (toggle.childCount > 0 && toggle.GetChild(0).GetComponent(Image!) is Component thumbnail)
                    Traverse.Create(thumbnail).Property("sprite").SetValue(Traverse.Create(skin).Field("skinThumbnail").GetValue());
                var util = toggle.GetComponent(ToggleUtil!);
                var toggleComponent = Traverse.Create(util).Field("ownT").GetValue() as Component ?? toggle.GetComponent(AccessTools.Field(ToggleUtil!, "ownT").FieldType);
                AccessTools.Field(ToggleUtil!, "toggleGroup").SetValue(util, group);
                var click = new UnityEvent();
                var chosen = index;
                click.AddListener(() => Select(chosen));
                AccessTools.Field(ToggleUtil!, "OnClick").SetValue(util, click);
                toggle.gameObject.SetActive(true);
                group.Add(toggleComponent);

                var label = (RectTransform)CloneInactive(nameText.gameObject, root.transform).transform;
                label.anchorMin = new Vector2(0f, 0f);
                label.anchorMax = new Vector2(1f, 0f);
                label.pivot = new Vector2(0.5f, 0f);
                label.anchoredPosition = Vector2.zero;
                label.sizeDelta = new Vector2(0f, 26f);
                SetText(label, name);
                Center(Text(label));
                label.gameObject.SetActive(true);

                Cells.Add(new Cell(index, name, SkinsModule.ModNameOf(skin), root, util));
            }
            Filter();
            Highlight(menu);
        }

        private static void Filter()
        {
            var shown = 0;
            foreach (var cell in Cells)
            {
                var match = SkinSearch.Matches(_query, cell.Name, cell.ModName);
                cell.Root.SetActive(match);
                if (match) shown++;
            }
            if (_empty != null) _empty.gameObject.SetActive(Cells.Count > 0 && shown == 0);
            if (_count != null) Traverse.Create(_count).Property("text").SetValue(shown == Cells.Count ? $"{Cells.Count} skins" : $"{shown} of {Cells.Count}");
        }

        private static void Highlight(object menu)
        {
            var animal = Traverse.Create(menu).Property("CurrentPreviewVirtualAnimal").GetValue();
            var current = animal == null ? -1 : Traverse.Create(animal).Field("skinIdx").GetValue<int>();
            var cell = Cells.FirstOrDefault(c => c.Index == current);
            if (cell != null) AccessTools.Method(ToggleUtil!, "ToggleOn")?.Invoke(cell.Toggle, new object[] { false });
        }

        /// <summary>Selects a skin as a click on its row swatch would.</summary>
        private static void Select(int index)
        {
            var menu = _menu;
            if (menu == null) return;
            try
            {
                if (Traverse.Create(menu).Field("skinToggles").GetValue() is IList row && index < row.Count && row[index] is Component swatch)
                    AccessTools.Method(ToggleUtil!, "ToggleOn")?.Invoke(swatch.GetComponent(ToggleUtil!), new object[] { false });
                AccessTools.Method(menu.GetType(), "SelectSkin", new[] { typeof(int) })?.Invoke(menu, new object[] { index });
                AccessTools.Method(menu.GetType(), "SetCreationRect", new[] { typeof(bool) })?.Invoke(menu, new object[] { false });
                NurseryScroll.Ensure(menu, rebuild: true); // scrolls the row to the chosen skin
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The skin could not be selected from the All skins window: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>Row indices of the skins the player can pick (unlocked; hidden stand-ins excluded).</summary>
        private static List<int> Visible(object menu)
        {
            var result = new List<int>();
            if (Traverse.Create(menu).Field("skinToggles").GetValue() is IList row)
                for (var i = 0; i < row.Count; i++)
                    if (row[i] is Component toggle && toggle != null && toggle.gameObject.activeSelf) result.Add(i);
            return result;
        }

        // ---- helpers ----

        /// <summary>Clones a game object inactive, without its localisation components (they would overwrite our texts).</summary>
        private static GameObject CloneInactive(GameObject source, Transform parent)
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

        private static RectTransform Child(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
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

        private static void OnClick(GameObject button, Action action)
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
        private static void SetIcon(Transform button, Sprite sprite)
        {
            for (var i = 0; i < button.childCount; i++)
                if (button.GetChild(i).GetComponent(Image!) is Component icon)
                {
                    Traverse.Create(icon).Property("sprite").SetValue(sprite);
                    return;
                }
        }

        private static Component? Text(Transform? transform) =>
            transform == null ? null : transform.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().FullName?.StartsWith("TMPro.") == true);

        private static void SetText(Transform? transform, string text)
        {
            var component = Text(transform);
            if (component != null) Traverse.Create(component).Property("text").SetValue(text);
        }

        private static void Center(Component? text)
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

        private static void SetColor(Component graphic, Color color) => Traverse.Create(graphic).Property("color").SetValue(color);

        /// <summary>A 3×3 grid of squares, drawn once.</summary>
        private static Sprite GridIcon()
        {
            if (_gridIcon != null) return _gridIcon;
            const int size = 32, cell = 8, gap = 4;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                int cx = x - 2, cy = y - 2;
                var inside = cx >= 0 && cy >= 0 && cx < 3 * cell + 2 * gap && cy < 3 * cell + 2 * gap && cx % (cell + gap) < cell && cy % (cell + gap) < cell;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Tyrant all skins icon", filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _gridIcon = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _gridIcon;
        }
    }
}
