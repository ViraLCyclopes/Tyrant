using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;
using static Tyrant.Framework.GameUi;

namespace Tyrant.Framework
{
    /// <summary>
    /// The look editor's entry points: a Tyrant "T" button right of the Nursery's animation list (it edits the preview animal)
    /// and one in the header of the park's and the vivarium's animal info panels (it edits that animal). Each is a clone of a
    /// small game button of that screen, placed once per park when the screen first shows; a screen whose pieces are not found
    /// logs once, with a short dump of what is there. Opened from the Nursery, the window follows its preview animal.
    /// </summary>
    internal static partial class LookEditorWindow
    {
        private const float Gap = 6f;
        private const string DetachedMessage = "The Nursery shows another look now; Save or Reset this one to follow it.";

        /// <summary>The InterfaceManager fields of the animal info panels (park, vivarium) that get a button.</summary>
        private static readonly string[] Panels = { "AnimalInfoGUI", "VivariumAnimalInfoGUI" };

        private static GameObject? _nurseryButton;
        private static readonly Dictionary<string, GameObject> PanelButtons = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> PanelShown = new Dictionary<string, int>(StringComparer.Ordinal); // actions shown when placed beside them (ActionsShown)
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal); // placement news, once a game run
        private static readonly HashSet<string> Broken = new HashSet<string>(StringComparer.Ordinal); // placements that threw: not tried again this park
        private static readonly Dictionary<(Type, string), MemberInfo?> Members = new Dictionary<(Type, string), MemberInfo?>();
        private static float _nextButtonTry, _nextNurseryCheck;

        // ---- following the Nursery ----
        private static bool _awaitPreview; // opened from the Nursery while its animal was loading
        private static bool _nurseryNews; // the Nursery rebuilt its creation panel (SkinPatches): look at the preview now
        private static bool _detached; // the Nursery shows another look (or closed) and the player kept editing this one
        private static bool _closedKept; // the Nursery closed under a draft with unsaved changes, which was kept
        private static AnimalContext? _declined; // that other look (not asked about again)

        /// <summary>The Nursery's creation panel changed (skin, sex, species): the window looks at its preview animal now.</summary>
        public static void NurseryChanged() => _nurseryNews = true;

        // ---- the buttons ----

        /// <summary>Places the buttons of the screens that show (a few times a second until each is in).</summary>
        private static void EnsureButtons(object gui)
        {
            if (Time.unscaledTime < _nextButtonTry || Button == null || Image == null) return;
            _nextButtonTry = Time.unscaledTime + 0.25f;
            if (_nurseryButton == null && NurseryMenu(gui) is Component nursery && nursery != null && nursery.gameObject.activeInHierarchy)
                _nurseryButton = TryPlace("nursery", nursery, () => NurseryButton(nursery));
            foreach (var field in Panels)
            {
                if (!(Read(gui, field, PanelName(field) + " has no Tyrant button.") is Component panel) || panel == null || !panel.gameObject.activeInHierarchy) continue;
                if (PanelButtons.TryGetValue(field, out var placed) && placed != null)
                {
                    // Placed beside the actions (no layout group): an action shown or hidden since (Sell on a dead animal) moves it.
                    if (PanelShown.TryGetValue(field, out var shown) && shown != ActionsShown(panel))
                        TryPlace(field, panel, () => Arrange((RectTransform)placed.transform, panel, field));
                    continue;
                }
                if (TryPlace(field, panel, () => PanelButton(panel, field)) is GameObject button) PanelButtons[field] = button;
            }
        }

        /// <summary>InterfaceManager.managementGUI.NurseryGUIManagerV2 (a missing member is logged once).</summary>
        private static Component? NurseryMenu(object gui) =>
            Read(Read(gui, "managementGUI", "the Nursery has no Tyrant button."), "NurseryGUIManagerV2", "the Nursery has no Tyrant button.") as Component;

        private static string PanelName(string field) => field == Panels[0] ? "The animal info panel" : "The vivarium animal info panel";

        /// <summary>A placement that throws (an unexpected screen) is logged once and left out; the editor and the game go on.</summary>
        private static GameObject? TryPlace(string key, Component screen, Func<GameObject?> place)
        {
            if (Broken.Contains(key)) return null;
            try
            {
                return place();
            }
            catch (Exception ex)
            {
                Broken.Add(key);
                Once(key + "-failed", true, () => $"The Tyrant looks button could not be placed in {screen.name} (game updated?): {ex.GetBaseException().Message}. Screen: " + Hierarchy(screen.transform, 1));
                return null;
            }
        }

        private static void DestroyButtons()
        {
            if (_nurseryButton != null) UnityEngine.Object.Destroy(_nurseryButton);
            foreach (var button in PanelButtons.Values)
                if (button != null) UnityEngine.Object.Destroy(button);
            _nurseryButton = null;
            PanelButtons.Clear();
            PanelShown.Clear();
            Broken.Clear();
            _nextButtonTry = 0f;
        }

        /// <summary>Right of NurseryMenuV2.animationDropdown ("Fidget"): a clone of the gallery filter menu's close button.</summary>
        private static GameObject? NurseryButton(Component nursery)
        {
            if (!(Read(nursery, "animationDropdown") is Component dropdown) || dropdown == null || !(dropdown.transform is RectTransform anchor) || anchor.parent == null)
            {
                Once("nursery", true, () => "The Nursery's animation list was not found (game updated?); the Nursery has no Tyrant button. Nursery: " + Hierarchy(nursery.transform, 1));
                return null;
            }
            var template = nursery.transform.Find(SkinBrowser.CloseButtonPath);
            if (template == null)
                Once("nursery-template", true, () => "The Nursery's filter close button was not found (game updated?); its Tyrant button is a plain one. Beside the list: " + Hierarchy(anchor.parent, 1));
            var button = IconButton(template == null ? null : template.gameObject, anchor.parent, Safe(() => OpenOn(NurseryPreview(), EditorEntry.Nursery)));
            var laidOut = PlaceBeside(button, anchor, anchor.rect.height > 1f ? anchor.rect.height : 32f, right: true);
            button.gameObject.SetActive(true);
            Once("nursery-placed", false, $"The Tyrant looks button is in the Nursery, right of {anchor.name} (in {anchor.parent.name}{(laidOut ? ", laid out by its group" : "")}).");
            return button.gameObject;
        }

        /// <summary>The bottom row's buttons of the info panels ("Actions Panel"): AnimalInfomenuManager's, VivariumAnimalInfomenuManager's sellButton.</summary>
        private static readonly string[] Actions = { "moveButton", "sellButton" };

        /// <summary>
        /// At the bottom of an info panel, in its actions row (move, sell): the row's last child when a layout group places them,
        /// else right of the right-most action (left of the left-most when that runs off the panel). Without that row, the panel's
        /// bottom-right corner, 8 px in. The header's previous/next arrow is the template, so it looks like the panel's own
        /// small buttons (the header's close X covered it there).
        /// </summary>
        private static GameObject? PanelButton(Component panel, string field)
        {
            var template = Read(panel, "nextButton") as Component ?? Read(panel, "previousButton") as Component;
            var actions = ActionRow(panel);
            var button = IconButton(template != null ? template.gameObject : null, actions.Count > 0 ? actions[0].parent : panel.transform, Safe(() => OpenOnPanel(panel)));
            Arrange(button, panel, field);
            button.gameObject.SetActive(true);
            return button.gameObject;
        }

        /// <summary>The panel's action buttons that share the first one's parent (the bottom row); empty when there are none.</summary>
        private static List<RectTransform> ActionRow(Component panel)
        {
            var actions = Actions.Select(name => Read(panel, name) as Component)
                .Select(c => c != null ? c.transform as RectTransform : null).Where(r => r != null && r.parent != null).Select(r => r!).ToList();
            var row = actions.Count > 0 ? actions[0].parent : null;
            return actions.Where(a => a.parent == row).ToList();
        }

        /// <summary>
        /// Which actions of the row show, to tell when the button needs placing again: bit i set when the row's i-th action is
        /// active, and the row's size above bit 16. Read as <see cref="ActionRow"/> does, without a list or a string (it runs a
        /// few times a second per panel).
        /// </summary>
        private static int ActionsShown(Component panel)
        {
            Transform? row = null;
            int count = 0, shown = 0;
            foreach (var name in Actions)
            {
                if (!(Read(panel, name) is Component action) || action == null || !(action.transform is RectTransform rect) || rect.parent == null) continue;
                if (row == null) row = rect.parent;
                else if (rect.parent != row) continue;
                if (rect.gameObject.activeSelf) shown |= 1 << count;
                count++;
            }
            return shown | count << 16;
        }

        /// <summary>Places the panel's button (see <see cref="PanelButton"/>); a placement beside the actions remembers which showed.</summary>
        private static GameObject? Arrange(RectTransform button, Component panel, string field)
        {
            var where = PanelName(field);
            var actions = ActionRow(panel);
            var row = actions.Count > 0 ? actions[0].parent : null;
            PanelShown.Remove(field);
            string place;
            if (row == null)
            {
                Once(field, true, () => $"{where}'s action buttons were not found (game updated?); its Tyrant button sits in its bottom-right corner. Panel: " + Hierarchy(panel.transform, 1));
                Corner(button, panel.transform, 28f);
                place = "in the bottom-right corner of " + panel.name;
            }
            else
            {
                var height = actions.Max(a => a.rect.height);
                var size = height > 1f ? height : 28f;
                if (HasLayoutGroup(row))
                {
                    button.SetParent(row, false);
                    button.localScale = actions[0].localScale;
                    button.localRotation = Quaternion.identity;
                    button.SetAsLastSibling();
                    SizeInLayout(button, size);
                    place = $"at the end of {row.name} (laid out by its group)";
                }
                else
                {
                    // A hidden action (Sell on a dead animal) does not count: the T would sit in the gap it leaves.
                    var shown = actions.Where(a => a.gameObject.activeSelf).ToList();
                    if (shown.Count == 0) shown = actions;
                    var rightMost = shown.OrderByDescending(a => Edge(a, right: true)).First();
                    PlaceBeside(button, rightMost, size, right: true);
                    place = $"right of {rightMost.name} (in {row.name})";
                    if (panel.transform is RectTransform bounds && !Inside(button, bounds))
                    {
                        var leftMost = shown.OrderBy(a => Edge(a, right: false)).First();
                        PlaceBeside(button, leftMost, size, right: false); // the panel's edge is right of the row
                        place = $"left of {leftMost.name} (in {row.name})";
                    }
                    PanelShown[field] = ActionsShown(panel);
                }
            }
            Once(field + "-placed", false, $"{where} has the Tyrant looks button {place}.");
            return button.gameObject;
        }

        /// <summary>The bottom-right corner of <paramref name="parent"/>, 8 px in, <paramref name="size"/> square (never laid out by a group).</summary>
        private static void Corner(RectTransform button, Transform parent, float size)
        {
            if (button.parent != parent) button.SetParent(parent, false);
            button.SetAsLastSibling();
            if (LayoutElement != null) Traverse.Create(button.GetComponent(LayoutElement) ?? button.gameObject.AddComponent(LayoutElement)).Property("ignoreLayout").SetValue(true);
            button.localScale = Vector3.one;
            button.localRotation = Quaternion.identity;
            button.anchorMin = button.anchorMax = button.pivot = new Vector2(1f, 0f);
            button.sizeDelta = new Vector2(size, size);
            button.anchoredPosition = new Vector2(-8f, 8f);
        }

        /// <summary>A rect's left or right edge on screen.</summary>
        private static float Edge(RectTransform rect, bool right)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return right ? corners[2].x : corners[0].x;
        }

        private static bool HasLayoutGroup(Transform t) => t.GetComponents<Component>().Any(c => c != null && IsLayoutGroup(c.GetType()));

        /// <summary>Its LayoutElement: <paramref name="size"/> square, never stretched.</summary>
        private static void SizeInLayout(RectTransform button, float size)
        {
            if (LayoutElement == null) return;
            var element = Traverse.Create(button.GetComponent(LayoutElement) ?? button.gameObject.AddComponent(LayoutElement));
            element.Property("ignoreLayout").SetValue(false);
            foreach (var name in new[] { "minWidth", "preferredWidth", "minHeight", "preferredHeight" }) element.Property(name).SetValue(size);
            element.Property("flexibleWidth").SetValue(0f);
            element.Property("flexibleHeight").SetValue(0f);
        }

        private static void OpenOnPanel(Component panel)
        {
            var animal = Read(panel, "refAnimal", "its Tyrant button opens nothing."); // AnimalInfomenuManager's Animal, VivariumAnimalInfomenuManager's VivariumAnimal
            if (LookEditorModule.IsAnimal(animal)) OpenOn(animal, EditorEntry.Park);
        }

        /// <summary>
        /// A small button with the "T" icon, made inactive under <paramref name="parent"/>: a clone of <paramref name="template"/>
        /// (its tooltip, texts and listeners dropped, its sounds kept) when it is a button, else a plain one.
        /// </summary>
        private static RectTransform IconButton(GameObject? template, Transform parent, Action click)
        {
            RectTransform rect;
            if (template != null && template.GetComponent(Button!) != null)
            {
                var clone = CloneInactive(template, parent);
                rect = (RectTransform)clone.transform;
                foreach (var extra in clone.GetComponentsInChildren<Component>(true).Where(c => c != null && IsForeign(c)).ToList())
                    UnityEngine.Object.DestroyImmediate(extra); // the original's tooltip and hover actions
                foreach (var component in clone.GetComponentsInChildren<Component>(true).Where(c => c != null))
                {
                    var type = component.GetType();
                    if (type.FullName?.StartsWith("TMPro.") == true) Traverse.Create(component).Property("text").SetValue("");
                    else if (type.Name == "GUISoundEvents") AccessTools.Field(type, "showTooltip")?.SetValue(component, false); // the original's tooltip
                }
                var hasIcon = Enumerable.Range(0, rect.childCount).Any(i => rect.GetChild(i).GetComponent(Image!) != null);
                if (hasIcon) SetIcon(rect, Icon());
                else if (clone.GetComponent(Image!) is Component own) Traverse.Create(own).Property("sprite").SetValue(Icon());
            }
            else
            {
                rect = Child(parent, ButtonName, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(32f, 32f));
                rect.gameObject.SetActive(false);
                var background = rect.gameObject.AddComponent(Image!);
                SetColor(background, GameUi.Panel);
                var button = rect.gameObject.AddComponent(Button!);
                Traverse.Create(button).Property("targetGraphic").SetValue(background);
                var icon = Child(rect, "Icon", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-6f, -6f));
                var image = icon.gameObject.AddComponent(Image!);
                Traverse.Create(image).Property("sprite").SetValue(Icon());
                Traverse.Create(image).Property("raycastTarget").SetValue(false);
            }
            rect.name = ButtonName;
            OnClick(rect.gameObject, click);
            return rect;
        }

        /// <summary>
        /// Puts the button beside <paramref name="anchor"/> (a sibling), centred on it, <paramref name="size"/> square. In a layout
        /// group it is the next (or previous) child and the group places it; returns true then.
        /// </summary>
        private static bool PlaceBeside(RectTransform button, RectTransform anchor, float size, bool right)
        {
            var parent = anchor.parent;
            if (button.parent != parent) button.SetParent(parent, false);
            button.localScale = anchor.localScale;
            button.localRotation = Quaternion.identity;
            button.SetSiblingIndex(Mathf.Clamp(anchor.GetSiblingIndex() + (right ? 1 : 0), 0, parent.childCount - 1));
            var anchorElement = LayoutElement == null ? null : anchor.GetComponent(LayoutElement);
            var ignored = anchorElement != null && Traverse.Create(anchorElement).Property("ignoreLayout").GetValue() is true;
            if (!ignored && HasLayoutGroup(parent))
            {
                SizeInLayout(button, size);
                return true;
            }
            // Anchored on the anchor's right (or left) edge and its vertical middle, whatever its own anchors are.
            var x = right ? anchor.anchorMax.x : anchor.anchorMin.x;
            button.anchorMin = button.anchorMax = new Vector2(x, (anchor.anchorMin.y + anchor.anchorMax.y) / 2f);
            button.pivot = new Vector2(right ? 0f : 1f, 0.5f);
            button.sizeDelta = new Vector2(size, size);
            button.anchoredPosition = new Vector2(right ? anchor.offsetMax.x + Gap : anchor.offsetMin.x - Gap, (anchor.offsetMin.y + anchor.offsetMax.y) / 2f);
            return false;
        }

        /// <summary>True when the button lies within the panel's width on screen.</summary>
        private static bool Inside(RectTransform button, RectTransform panel)
        {
            var mine = new Vector3[4];
            var its = new Vector3[4];
            button.GetWorldCorners(mine);
            panel.GetWorldCorners(its);
            return mine[0].x >= its[0].x - 0.5f && mine[2].x <= its[2].x + 0.5f;
        }

        private static bool IsLayoutGroup(Type? type)
        {
            for (; type != null; type = type.BaseType)
                if (type.FullName == "UnityEngine.UI.LayoutGroup") return true;
            return false;
        }

        private static bool IsForeign(Component c)
        {
            var name = c.GetType().Name;
            return name.Contains("Tooltip") || name == "EventTrigger" || name == "ToggleUtil";
        }

        private static string Hierarchy(Transform t, int depth)
        {
            var parts = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
            var line = $"{t.name} [{string.Join(", ", parts)}]";
            if (depth >= 2 || t.childCount == 0) return line;
            return line + " { " + string.Join("; ", Enumerable.Range(0, t.childCount).Select(i => Hierarchy(t.GetChild(i), depth + 1))) + " }";
        }

        /// <summary>A "T", drawn once.</summary>
        private static Sprite Icon() =>
            _icon != null ? _icon : _icon = DrawIcon(32, (x, y) => (y >= 23 && y <= 27 && x >= 5 && x <= 26) || (x >= 14 && x <= 17 && y >= 4 && y <= 27), "Tyrant looks icon");

        private static void Once(string key, bool warning, string text) => Once(key, warning, () => text);

        /// <summary>Logs once a game run; the text (a hierarchy dump, say) is only made then.</summary>
        private static void Once(string key, bool warning, Func<string> text)
        {
            if (!Logged.Add(key)) return;
            if (warning) FrameworkMod.Log.Warning(text());
            else FrameworkMod.Log.Msg(text());
        }

        // ---- the Nursery's preview animal ----

        /// <summary>The Nursery's preview animal (<see cref="NurseryPreview"/>), for the materials to write it again; null outside a park.</summary>
        public static object? NurseryAnimal() => LookEditorModule.Looks == null ? null : NurseryPreview();

        /// <summary>Game.AnimalPreviewComponent.currentAnimalPreview once loaded; null while it loads or when there is none.</summary>
        private static object? NurseryPreview()
        {
            var component = LookEditorModule.PreviewComponent();
            if (component == null || Read(component, "IsLoadingAnimal", NoPreview) is true) return null;
            var animal = Read(component, "currentAnimalPreview", NoPreview);
            return LookEditorModule.IsAnimal(animal) ? animal : null;
        }

        private const string NoPreview = "the look editor cannot open on the Nursery's animal.";

        /// <summary>
        /// Once a frame while the window is open on the Nursery: a closed Nursery is like the animal going away, except that a
        /// draft with unsaved changes is kept on its look (Save still writes it); a new preview of the same look (a new sex) is
        /// followed quietly; another skin or species is followed after "Discard changes?" when the draft has unsaved changes
        /// (keeping them leaves the window on its look until it is saved or reset).
        /// </summary>
        private static void FollowNursery(object gui)
        {
            var nursery = NurseryMenu(gui);
            // Closing the management menu only fades it (the Nursery stays active); a missing flag leaves the Nursery's own state.
            var shown = nursery != null && nursery.gameObject.activeInHierarchy
                && !(Read(gui, "isManagementMenuOpen", "a Nursery closed with its menu may look open to the look editor.") is false);
            if (!shown)
            {
                if (_animal == null && !_awaitPreview) return;
                var message = EditorScopes.NurseryClosed(_draft?.IsDirty == true, out var keep);
                if (!keep) AnimalGone(_closedKept && _message.Length > 0 && _message != ClosedKeptMessage ? _message : message); // "Saved to …" stays
                else if (!_closedKept) KeepClosed(message);
                return;
            }
            if (_closedKept)
            {
                _closedKept = false; // open again: an ordinary kept draft, which follows the preview once it shows its look
                if (_message == ClosedKeptMessage) _message = DetachedMessage;
            }
            if (_animal == null && !_awaitPreview) return; // gone: the Nursery's button opens it again
            var component = LookEditorModule.PreviewComponent();
            var current = Read(component, "currentAnimalPreview", NoPreview);
            if (_animal != null && !ReferenceEquals(current, _animal) && _matching.Count > 0 && ReferenceEquals(_matching[0], _animal))
                _matching.RemoveAt(0); // the Nursery disposed of it: never re-applied again
            if (_modal != null || current == null || !LookEditorModule.IsAnimal(current) || Read(component, "IsLoadingAnimal", NoPreview) is true) return; // the new one loads
            if (!_nurseryNews && Time.unscaledTime < _nextNurseryCheck) return;
            _nurseryNews = false;
            _nextNurseryCheck = Time.unscaledTime + 0.25f; // without the Nursery's news (no skin mods: not hooked), a few times a second
            if (_awaitPreview)
            {
                Select(current);
                return;
            }
            var context = MaterialsModule.Describe(current);
            if (context == null) return;
            if (EditorScopes.SameLook(context, _context, _scope)) // a Next created look is per sex
            {
                if (_detached || !ReferenceEquals(current, _animal) || context.Variant != _context!.Variant) Repoint(current, context);
                return;
            }
            if (_draft == null || !_draft.IsDirty)
            {
                Select(current, keepMessage: _detached); // after a save made while it showed another look, "Saved to …" stays
                return;
            }
            if (_detached && EditorScopes.SameLook(context, _declined, _scope)) return; // the player kept editing
            Confirm(FollowNow, () => Detach(current, context));
        }

        private static readonly string ClosedKeptMessage = EditorScopes.NurseryClosed(dirty: true, out _);

        /// <summary>The Nursery closed under unsaved changes: they stay, on their look, with no preview animal to show them on.</summary>
        private static void KeepClosed(string message)
        {
            CloseModal(); // a question about the Nursery's next animal is moot now
            _closedKept = true;
            _detached = true;
            _declined = null;
            _awaitPreview = false;
            var preview = Read(LookEditorModule.PreviewComponent(), "currentAnimalPreview");
            _matching.RemoveAll(a => ReferenceEquals(a, _animal) || ReferenceEquals(a, preview));
            _message = message;
            Rebuild();
        }

        /// <summary>After "Discard": the preview the Nursery shows by now (or the next one to load).</summary>
        private static void FollowNow()
        {
            if (NurseryPreview() is object now) Select(now);
            else _awaitPreview = true;
        }

        /// <summary>The same look on another (or a changed) preview animal: the draft stays and shows on it.</summary>
        private static void Repoint(object current, AnimalContext context)
        {
            _animal = current;
            _context = context;
            _detached = false;
            _declined = null;
            if (_message == DetachedMessage || _message == ClosedKeptMessage) _message = "";
            ReadHeader();
            _matching = MaterialsModule.AnimalsMatching(_scope, current).ToList();
            _below = MaterialsModule.Below(current, _scope);
            if (NextCreated) PushLive(keywords: true); // the draft is kept by the preview object: it moves to the new one
            else MaterialsModule.Reapply(current, keywords: true); // the draft's live edit on the new animal
            Rebuild();
        }

        /// <summary>"Keep editing": the draft stays on its look; the preview, which shows another one, is left alone.</summary>
        private static void Detach(object current, AnimalContext context)
        {
            _detached = true;
            _declined = context;
            _matching.RemoveAll(a => ReferenceEquals(a, current) || ReferenceEquals(a, _animal));
            _message = DetachedMessage;
            Footer();
        }

        // ---- reflection ----

        /// <summary>
        /// A field or property of a game object, looked up once per type; null when missing, destroyed or not readable now. With
        /// <paramref name="missing"/> (what is lost then), a member the type does not have is logged once (a game update renamed it).
        /// </summary>
        private static object? Read(object? target, string name, string? missing = null)
        {
            if (target == null || (target is UnityEngine.Object unity && unity == null)) return null;
            var type = target.GetType();
            if (!Members.TryGetValue((type, name), out var member))
                Members[(type, name)] = member = (MemberInfo?)AccessTools.Field(type, name) ?? AccessTools.Property(type, name);
            if (member == null && missing != null) Once("member|" + type.FullName + "." + name, true, $"{type.Name}.{name} was not found (game updated?); {missing}");
            try
            {
                return member is FieldInfo field ? field.GetValue(target) : member is PropertyInfo property ? property.GetValue(target, null) : null;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }
    }
}
