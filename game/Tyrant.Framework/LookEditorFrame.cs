using System;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Tyrant.Framework.Core;
using UnityEngine;
using static Tyrant.Framework.GameUi;

namespace Tyrant.Framework
{
    /// <summary>
    /// The look editor's frame: the window (title to drag, corner to resize, minimise and X), its place kept in
    /// MelonPreferences, the "Discard changes?" box, and the header and footer texts.
    /// </summary>
    internal static partial class LookEditorWindow
    {
        // ---- the window frame ----

        private static void Build(object gui)
        {
            var board = Traverse.Create(gui).Field("mainBoardGUI").GetValue() as Component;
            _font = board == null ? null : Traverse.Create(board).Field("staffAmtTxt").GetValue() as Component;
            if (_font == null || Image == null || Button == null)
            {
                FrameworkMod.Log.Warning("The game's top bar text was not found (game updated?); the look editor cannot open.");
                return;
            }
            var canvasObject = new GameObject("TyrantLookEditorCanvas", typeof(RectTransform)) { layer = board!.gameObject.layer };
            try
            {
                _inputTemplate = InputTemplate(canvasObject.transform);
                var canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 120; // over the game's panels
                var boardCanvas = Traverse.Create(gui).Field("MainBoardCanvas").GetValue() as Canvas;
                CopyScaler(boardCanvas != null ? boardCanvas.rootCanvas : null, canvasObject); // the game's UI scale
                if (Raycaster != null) canvasObject.AddComponent(Raycaster);

                var window = Child(canvasObject.transform, "TyrantLookEditor", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(360f, 600f));
                SetColor(window.gameObject.AddComponent(Image), GameUi.Panel);
                var strip = Child(window, "Strip", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 4f));
                SetColor(strip.gameObject.AddComponent(Image), Green);

                // Title: drag the window by it; minimise and close on the right.
                var title = Child(window, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(0f, TitleHeight - 4f));
                SetColor(title.gameObject.AddComponent(Image), new Color(0f, 0f, 0f, 0.15f));
                var caption = Label(title, _font, "<b>Tyrant</b>  looks", 13f);
                ((RectTransform)caption.transform).offsetMin = new Vector2(10f, 0f);
                OnPointer(title.gameObject, "Drag", Safe<object>(data => Move(PointerDelta(data))));
                OnPointer(title.gameObject, "EndDrag", Safe<object>(_ => SavePrefs()));
                var minimise = FlatButton(title, _font, "-", Idle, Safe(Minimise));
                Place((RectTransform)minimise.Button.transform, 1f, 1f, -50f, -28f, 2f, -2f);
                var close = FlatButton(title, _font, "X", Idle, Safe(RequestClose));
                Place((RectTransform)close.Button.transform, 1f, 1f, -26f, -4f, 2f, -2f);

                var main = Child(window, "Main", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                main.offsetMax = new Vector2(0f, -TitleHeight);
                _name = Label(TopRow(main, 4f, 20f), _font, "", 14f);
                _info = Label(TopRow(main, 24f, 16f), _font, "", 11f);
                Traverse.Create(_info).Property("color").SetValue(Faint);

                var scopes = TopRow(main, 44f, 24f);
                var names = new[] { LookScope.Animal, LookScope.Skin, LookScope.Species }.Select(s => (s, EditorScopes.Label(EditorEntry.Park, s))).ToArray(); // Header names them for the entry
                for (var i = 0; i < names.Length; i++)
                {
                    var scope = names[i].Item1;
                    var button = FlatButton(scopes, _font, names[i].Item2, Idle, Safe(() => ChooseScope(scope)));
                    Place((RectTransform)button.Button.transform, i / 3f, (i + 1) / 3f, i == 0 ? 0f : 2f, i == 2 ? 0f : -2f);
                    ScopeButtons[scope] = button;
                }
                var tabs = TopRow(main, 74f, 24f);
                _irisTab = FlatButton(tabs, _font, "Iridescence", Idle, Safe(() => ChooseTab(fur: false)));
                Place((RectTransform)_irisTab.Button.transform, 0f, 0.5f, 0f, -2f);
                _furTab = FlatButton(tabs, _font, "Fur", Idle, Safe(() => ChooseTab(fur: true)));
                Place((RectTransform)_furTab.Button.transform, 0.5f, 1f, 2f, 0f);

                var body = Child(main, "Body", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                body.offsetMin = new Vector2(0f, 120f);
                body.offsetMax = new Vector2(0f, -104f);
                _content = ScrollView(body, "Viewport");

                _status = Label(BottomRow(main, 90f, 26f), _font, "", 10f);
                Traverse.Create(_status).Property("color").SetValue(Faint);
                Traverse.Create(_status).Property("enableWordWrapping").SetValue(true); // two lines: where it saves, and why "This animal" is off
                var exports = BottomRow(main, 62f, 24f);
                _export = FlatButton(exports, _font, "Export mask PNG", Idle, Safe(ExportMask));
                Place((RectTransform)_export.Button.transform, 0f, 0.5f, 0f, -2f); // the whole row on the Fur tab (Header)
                _exportColours = FlatButton(exports, _font, "Export colours PNG", Idle, Safe(ExportColours));
                Place((RectTransform)_exportColours.Button.transform, 0.5f, 1f, 2f, 0f);
                var images = BottomRow(main, 34f, 24f);
                _reload = FlatButton(images, _font, "Reload images", Idle, Safe(ReloadImages));
                Place((RectTransform)_reload.Button.transform, 0f, 0.5f, 0f, -2f);
                _openFolder = FlatButton(images, _font, "Open folder", Idle, Safe(OpenMasksFolder));
                Place((RectTransform)_openFolder.Button.transform, 0.5f, 1f, 2f, 0f);
                var lower = BottomRow(main, 6f, 24f);
                _reset = FlatButton(lower, _font, "Reset", Idle, Safe(Reset));
                Place((RectTransform)_reset.Button.transform, 0f, 0.5f, 0f, -2f);
                _save = FlatButton(lower, _font, "Save", Green, Safe(Save));
                Place((RectTransform)_save.Button.transform, 0.5f, 1f, 2f, 0f);

                // Resize from the bottom-right corner.
                var grip = Child(main, "Grip", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(14f, 14f));
                SetColor(grip.gameObject.AddComponent(Image), new Color(1f, 1f, 1f, 0.25f));
                OnPointer(grip.gameObject, "Drag", Safe<object>(data => Resize(PointerDelta(data))));
                OnPointer(grip.gameObject, "EndDrag", Safe<object>(_ => SavePrefs()));

                main.gameObject.SetActive(!_minimised); // as Place sizes the window
                window.gameObject.SetActive(false);
                _canvas = canvasObject;
                _window = window.gameObject;
                _main = main.gameObject;
            }
            catch
            {
                UnityEngine.Object.Destroy(canvasObject); // never leave a half-built window on screen
                _content = null;
                throw;
            }
        }

        private static RectTransform TopRow(RectTransform parent, float top, float height)
        {
            var row = Child(parent, "Row", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -top), new Vector2(0f, height));
            row.offsetMin = new Vector2(10f, row.offsetMin.y);
            row.offsetMax = new Vector2(-10f, row.offsetMax.y);
            return row;
        }

        private static RectTransform BottomRow(RectTransform parent, float bottom, float height)
        {
            var row = Child(parent, "Row", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, bottom), new Vector2(0f, height));
            row.offsetMin = new Vector2(10f, row.offsetMin.y);
            row.offsetMax = new Vector2(-24f, row.offsetMax.y); // clear of the resize grip
            return row;
        }

        /// <summary>Spans a rect from x0 to x1 of its row (fractions), with pixel insets, full height (optionally inset).</summary>
        private static void Place(RectTransform rect, float x0, float x1, float left, float right, float bottom = 0f, float top = 0f)
        {
            rect.anchorMin = new Vector2(x0, 0f);
            rect.anchorMax = new Vector2(x1, 1f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        private static void CopyScaler(Component? source, GameObject target)
        {
            if (Scaler == null) return;
            var scaler = target.AddComponent(Scaler);
            var from = source == null ? null : source.GetComponent(Scaler);
            if (from == null) return;
            foreach (var name in new[] { "uiScaleMode", "referenceResolution", "screenMatchMode", "matchWidthOrHeight", "scaleFactor", "referencePixelsPerUnit" })
            {
                var property = AccessTools.Property(Scaler, name);
                if (property != null && property.CanWrite) property.SetValue(scaler, property.GetValue(from, null), null);
            }
        }

        private static Type? _nurseryMenuType;
        private static bool _nurseryMenuTypeRead;

        /// <summary>
        /// The template of the typed-value boxes: a copy of the Nursery's species search box made once per window, kept inactive
        /// under the window's canvas (it goes with it), so the game's own box is never touched again; null: they are built by
        /// reflection.
        /// </summary>
        private static Component? InputTemplate(Transform holder)
        {
            if (!_nurseryMenuTypeRead)
            {
                _nurseryMenuTypeRead = true; // looked up once a game run: a type lookup is slow
                _nurseryMenuType = AccessTools.TypeByName("PrehistoricKingdom.NurseryMenuV2");
            }
            if (_nurseryMenuType == null) return null;
            foreach (var found in Resources.FindObjectsOfTypeAll(_nurseryMenuType))
                if (found is Component menu && menu != null && menu.gameObject.scene.IsValid()
                    && Traverse.Create(menu).Field("gallerySearchInputField").GetValue() is Component field && field != null)
                {
                    var copy = CloneInactive(field.gameObject, holder); // inactive, and stays so: only its clones show
                    copy.name = "TyrantInputTemplate";
                    var input = copy.GetComponent(field.GetType());
                    if (input != null) return input;
                    UnityEngine.Object.Destroy(copy);
                    return null;
                }
            return null;
        }

        private static float Scale => _canvas != null && _canvas.GetComponent<Canvas>() is Canvas canvas && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;

        private static Vector2 CanvasSize => _canvas == null ? new Vector2(1920f, 1080f) : ((RectTransform)_canvas.transform).rect.size;

        private static void Move(Vector2 delta)
        {
            if (_x == null || _y == null) return;
            _x.Value += delta.x / Scale;
            _y.Value -= delta.y / Scale;
            Place();
        }

        private static void Resize(Vector2 delta)
        {
            if (_w == null || _h == null) return;
            _w.Value += delta.x / Scale;
            _h.Value -= delta.y / Scale;
            Place();
        }

        /// <summary>Puts the window where the preferences say, kept on screen and no smaller than its minimum.</summary>
        private static void Place()
        {
            if (_window == null || _x == null || _y == null || _w == null || _h == null) return;
            var screen = CanvasSize;
            if (screen.x >= 320f && screen.y >= 240f) // a canvas made this frame has no size yet: placed on the next one
            {
                Keep(_w, Mathf.Clamp(_w.Value, MinWidth, Mathf.Max(MinWidth, screen.x)));
                Keep(_h, Mathf.Clamp(_h.Value, MinHeight, Mathf.Max(MinHeight, screen.y)));
                if (_centre || _x.Value <= Unplaced / 2f) // it opens in the middle of the screen; a drag moves it from there
                {
                    _centre = false;
                    Keep(_x, (screen.x - _w.Value) / 2f);
                    Keep(_y, Mathf.Max(0f, (screen.y - _h.Value) / 2f));
                }
                Keep(_x, Mathf.Clamp(_x.Value, 60f - _w.Value, Mathf.Max(0f, screen.x - 60f)));
                Keep(_y, Mathf.Clamp(_y.Value, 0f, Mathf.Max(0f, screen.y - TitleHeight)));
            }
            var rect = (RectTransform)_window.transform;
            rect.anchoredPosition = new Vector2(_x.Value, -_y.Value);
            rect.sizeDelta = new Vector2(_w.Value, _minimised ? TitleHeight : _h.Value);
        }

        private static void Keep(MelonPreferences_Entry<float> entry, float value)
        {
            if (entry.Value != value) entry.Value = value;
        }

        private static void Minimise()
        {
            _minimised = !_minimised;
            if (_main != null) _main.SetActive(!_minimised);
            Place();
        }

        private static void SavePrefs()
        {
            try
            {
                _prefs?.SaveToFile(false);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("The look editor's place could not be saved: " + ex.GetBaseException().Message);
            }
        }

        private static void ChooseTab(bool fur)
        {
            _onFurTab = fur;
            Rebuild();
        }

        // ---- the "Discard changes?" box ----

        private static void ShowModal(string question, string detail, Action discard, Action? keep = null)
        {
            if (_window == null || _font == null) return;
            CloseModal();
            var shade = Child(_window.transform, "Modal", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            SetColor(shade.gameObject.AddComponent(Image!), new Color(0f, 0f, 0f, 0.55f)); // catches every click under it
            var box = Child(shade, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 112f));
            SetColor(box.gameObject.AddComponent(Image!), GameUi.Panel);
            var strip = Child(box, "Strip", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 4f));
            SetColor(strip.gameObject.AddComponent(Image!), Green);
            Label(TopRow(box, 12f, 22f), _font, "<b>" + question + "</b>", 14f, "Center");
            var text = Label(TopRow(box, 36f, 30f), _font, detail, 11f, "Center");
            Traverse.Create(text).Property("enableWordWrapping").SetValue(true);
            var buttons = Child(box, "Buttons", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(-20f, 26f));
            var yes = FlatButton(buttons, _font, "Discard", new Color(0.62f, 0.22f, 0.2f, 1f), Safe(() =>
            {
                CloseModal();
                discard();
            }));
            Place((RectTransform)yes.Button.transform, 0f, 0.5f, 0f, -3f);
            var stay = FlatButton(buttons, _font, "Keep editing", Idle, Safe(() =>
            {
                CloseModal();
                keep?.Invoke();
            }));
            Place((RectTransform)stay.Button.transform, 0.5f, 1f, 3f, 0f);
            _modal = shade.gameObject;
        }

        private static void CloseModal()
        {
            if (_modal != null) UnityEngine.Object.Destroy(_modal);
            _modal = null;
        }

        // ---- the header and the footer ----

        /// <summary>
        /// The header's texts, read from the animal (again on every redraw while it is there, so a new name shows); a kept
        /// Nursery draft keeps the texts of its look. A read that fails (an animal being disposed) keeps the earlier texts.
        /// </summary>
        private static void ReadHeader()
        {
            if (_animal == null || _context == null)
            {
                _title = _details = null;
                _names = default;
                return;
            }
            try
            {
                var name = AccessTools.Property(_animal.GetType(), "Name")?.GetValue(_animal, null) as string;
                var data = AccessTools.Property(_animal.GetType(), "BaseData")?.GetValue(_animal, null);
                var species = data == null ? null : AccessTools.Property(data.GetType(), "DisplayName")?.GetValue(data, null) as string;
                var skinData = AccessTools.Property(_animal.GetType(), "SkinData")?.GetValue(_animal, null);
                var skin = skinData == null ? null : AccessTools.Field(skinData.GetType(), "skinName")?.GetValue(skinData) as string;
                _names = (species, skin, name);
                _title = "<b>" + (string.IsNullOrEmpty(name) ? species ?? _context.Species : name) + "</b>" + (_entry == EditorEntry.Nursery ? "  (Nursery)" : "");
                var variant = _context.Variant == "normal" ? "" : " (" + _context.Variant + ")";
                _details = $"{species ?? _context.Species} · {skin ?? "?"}{variant} · {_context.Source}";
            }
            catch (System.Reflection.TargetInvocationException)
            {
                // the texts read before stay
            }
        }

        private static void Header()
        {
            if (_name == null || _info == null) return;
            if (!_detached && Alive(_animal)) ReadHeader(); // a park animal renamed while the window is open
            if (_context == null || _title == null)
            {
                SetText(_name.transform, "No animal");
                SetText(_info.transform, "Open the editor from an animal's info panel or the Nursery.");
            }
            else
            {
                SetText(_name.transform, _title);
                SetText(_info.transform, _details ?? "");
            }
            foreach (var pair in ScopeButtons)
            {
                var available = Available(pair.Key);
                SetColor(pair.Value.Button.GetComponent(Image!), pair.Key == _scope && available ? Green : Idle);
                SetInteractable(pair.Value.Button.GetComponent(Button!), available);
                SetText(pair.Value.Label.transform, EditorScopes.Label(_entry, pair.Key)); // "Next created" in the Nursery
                Traverse.Create(pair.Value.Label).Property("color").SetValue(available ? Color.white : Faint);
            }
            SetColor(_irisTab.Button.GetComponent(Image!), _onFurTab ? Idle : Green);
            SetColor(_furTab.Button.GetComponent(Image!), _onFurTab ? Green : Idle);
            _exportColours.Button.SetActive(!_onFurTab); // the shimmer colours are the Iridescence tab's
            Place((RectTransform)_export.Button.transform, 0f, _onFurTab ? 1f : 0.5f, 0f, _onFurTab ? 0f : -2f);
        }

        private static void Footer()
        {
            if (_status == null) return;
            var editing = _draft != null && _animal != null;
            var dirty = _draft?.IsDirty == true;
            var why = editing ? EditorScopes.WhyNot(_entry, LookScope.Animal, _context, LookEditorModule.SaveName) : null; // "This animal" is off
            SetText(_status.transform, _message.Length > 0 ? _message : editing ? "Saves to " + TargetName() + "." + (why == null ? "" : " " + why) + FurNote() : "");
            SetText(_save.Label.transform, dirty ? "Save *" : "Save");
            SetInteractable(_save.Button.GetComponent(Button!), dirty && _target != null);
            SetInteractable(_reset.Button.GetComponent(Button!), dirty);
            SetInteractable(_export.Button.GetComponent(Button!), editing && _target != null);
            SetInteractable(_exportColours.Button.GetComponent(Button!), editing && _target != null);
            SetInteractable(_reload.Button.GetComponent(Button!), true);
            SetInteractable(_openFolder.Button.GetComponent(Button!), true);
            SetColor(_save.Button.GetComponent(Image!), dirty && _target != null ? Green : Idle);
        }
    }
}
