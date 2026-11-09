using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using Tyrant.Framework.Core;
using UnityEngine;
using static Tyrant.Framework.GameUi;

namespace Tyrant.Framework
{
    /// <summary>
    /// The in-game look editor: a Tyrant button in a placed animal's info panel, or beside the Nursery's animation list, opens
    /// a floating window (drag it by its title, resize it from its corner, minimise or close it; where it is and its size are
    /// kept in MelonPreferences) that edits the fur and iridescence of the animal it was opened on, for that animal, its whole
    /// skin or its whole species. Opened from the Nursery it follows the preview animal. Every change is a live preview
    /// (<see cref="MaterialsModule.SetLive"/>) until Save. Built from the game's uGUI by reflection, with the game's font; the
    /// buttons are in LookEditorEntries.cs, the frame in LookEditorFrame.cs and the tab contents in LookEditorTabs.cs.
    /// </summary>
    internal static partial class LookEditorWindow
    {
        private const float MinWidth = 300f, MinHeight = 360f, TitleHeight = 28f;
        private const float Unplaced = -1000000f; // EditorX before the window was first placed (far below any x a drag can reach)
        private const string ButtonName = "TyrantLooksButton";
        private static readonly Color Idle = new Color(0.18f, 0.20f, 0.22f, 1f);
        private static readonly Color Faint = new Color(1f, 1f, 1f, 0.5f);

        // ---- kept between sessions ----
        private static MelonPreferences_Category? _prefs;
        private static MelonPreferences_Entry<float>? _x, _y, _w, _h;

        // ---- the game's pieces and ours (rebuilt in every park) ----
        private static object? _gui;
        private static GameObject? _canvas, _window, _main, _modal;
        private static RectTransform? _content;
        private static Component? _font, _inputTemplate, _name, _info, _status;
        private static readonly Dictionary<LookScope, (GameObject Button, Component Label)> ScopeButtons = new Dictionary<LookScope, (GameObject, Component)>();
        private static (GameObject Button, Component Label) _irisTab, _furTab, _export, _exportColours, _reload, _openFolder, _reset, _save;
        private static Sprite? _icon;
        private static bool _minimised, _shapingOpen, _onFurTab;
        private static bool _centre; // the window was just shown: Place puts it in the middle of the screen
        private static string _message = "";
        private static string? _title, _details; // the header, read when the animal was chosen (the Nursery's may be gone since)

        // ---- what is being edited ----
        private static EditorEntry _entry = EditorEntry.Park;
        private static object? _animal;
        private static AnimalContext? _context;
        private static LookScope _scope = LookScope.Animal;
        private static LookDraft? _draft;
        private static SaveTarget? _target;
        private static MaterialOverride? _below;
        private static bool? _lastOn, _lastGrow;
        private static FurAge? _lastAge;
        private static string _furNote = ""; // the status line's fur note when Tick last looked
        private static object? _previewShown; // the Nursery preview a Next created draft shows on (MaterialsModule.SetPreviewLook)
        private static List<object> _matching = new List<object>();
        private static readonly IridescenceSwitch Switch = new IridescenceSwitch();
        private static readonly FurGrowSwitch FurSwitch = new FurGrowSwitch(); // remembers the length Grow fur seeded, for Off
        private static readonly Dictionary<PresetKind, string?> PresetPick = new Dictionary<PresetKind, string?>(); // the Preset row's species per tab
        private static (PresetKind Kind, string Species)? _presetWaiting; // Apply pressed while that species' material still loads

        /// <summary>The draft's exports in UserData/Tyrant/Masks; a save into a mod copies them under their fixed names.</summary>
        private static readonly ExportedFiles Exports = new ExportedFiles();

        private static (string? Species, string? Skin, string? Animal) _names; // the display names exports are named after (ReadHeader)

        private static bool IsOpen => _window != null && _window.activeSelf;

        public static void Start()
        {
            _prefs = MelonPreferences.CreateCategory("Tyrant");
            _x = _prefs.CreateEntry("EditorX", Unplaced);
            _y = _prefs.CreateEntry("EditorY", 96f);
            _w = _prefs.CreateEntry("EditorW", 360f);
            _h = _prefs.CreateEntry("EditorH", 600f);
            RemoveOldPending();
            SpeciesLooks.Loaded += species => Safe(() => SpeciesLoaded(species))();
        }

        /// <summary>Once a frame in a park: the Tyrant buttons, an animal that went away, the Nursery's preview animal.</summary>
        public static void Tick(object gui, bool parkReady)
        {
            _gui = gui;
            if (!parkReady) return;
            EnsureButtons(gui);
            if (!IsOpen) return;
            Place(); // keeps it on screen when the resolution changes
            if (_entry == EditorEntry.Nursery) FollowNursery(gui);
            else if (!Alive(_animal)) AnimalGone("The animal is gone; open the editor from another one.");
            // The graphics setting can change while the window is open: the status line's fur note follows (a cached bool read).
            var note = FurNote();
            if (note != _furNote)
            {
                _furNote = note;
                Footer();
            }
        }

        /// <summary>The save in use changed (saved, or deleted): "This animal" may have become available or not.</summary>
        public static void SaveChanged()
        {
            if (IsOpen) Rebuild();
        }

        /// <summary>The park is gone with every object of ours in it; unsaved edits are dropped.</summary>
        public static void LeavePark()
        {
            ClearLive(reapply: false);
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
            DestroyButtons();
            _canvas = _window = _main = _modal = null;
            _content = null;
            _gui = _font = _inputTemplate = _name = _info = _status = null;
            _minimised = false; // the next park's window opens whole
            ScopeButtons.Clear();
            Forget();
            _entry = EditorEntry.Park;
        }

        public static void AnimalsRemoved(List<ulong> ids)
        {
            _matching.RemoveAll(a => a is UnityEngine.Object unity && unity == null);
            if (_entry == EditorEntry.Park && _context?.PersistentId is ulong id && ids.Contains(id)) AnimalGone("The animal is gone; open the editor from another one.");
        }

        /// <summary>After a failure: unsaved edits go, the window and button are removed.</summary>
        public static void Disable()
        {
            try
            {
                ClearLive(reapply: true);
            }
            finally
            {
                if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
                DestroyButtons();
                _canvas = _window = _main = null;
                _minimised = false;
                Forget();
            }
        }

        /// <summary>Wraps a UI callback: a failure logs once and turns the editor off, never the game.</summary>
        private static Action Safe(Action action) => () =>
        {
            if (LookEditorModule.Disabled) return;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LookEditorModule.Fail(ex);
            }
        };

        private static Action<T> Safe<T>(Action<T> action) => value => Safe(() => action(value))();

        // ---- opening and closing ----

        /// <summary>
        /// A Tyrant button was clicked: the window opens on that animal (null: the Nursery's, still loading), after "Discard
        /// changes?" when another one has unsaved edits. The same button again closes it.
        /// </summary>
        private static void OpenOn(object? animal, EditorEntry entry)
        {
            var same = entry == _entry && (entry == EditorEntry.Nursery ? _animal != null || _awaitPreview : animal != null && ReferenceEquals(animal, _animal));
            if (IsOpen && same && _modal == null)
            {
                RequestClose();
                return;
            }
            if (_modal != null || !ShowWindow()) return;
            Confirm(() => Target(animal, entry));
        }

        private static void Target(object? animal, EditorEntry entry)
        {
            ClearLive(reapply: true);
            Forget();
            _entry = entry;
            _scope = EditorScopes.Preferred(entry); // checked against the animal in Select
            if (animal != null)
            {
                Select(animal);
                return;
            }
            _awaitPreview = true; // FollowNursery picks it up once loaded
            _message = "The Nursery's animal is still loading.";
            Rebuild();
        }

        private static bool ShowWindow()
        {
            if (_gui == null) return false;
            if (_window == null) Build(_gui);
            if (_window == null) return false;
            if (_window.activeSelf) return true;
            _window.SetActive(true);
            _centre = true; // placed in the middle once the canvas has its size
            Place();
            try
            {
                Directory.CreateDirectory(MasksDirectory); // where the player's own images go
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                FrameworkMod.Log.Warning($"\"{MasksDirectory}\" could not be made: {ex.Message}");
            }
            return true;
        }

        private static void RequestClose() => Confirm(Hide);

        private static void Hide()
        {
            ClearLive(reapply: true);
            _draft?.Reset();
            Exports.Forget();
            _awaitPreview = false;
            if (_window != null) _window.SetActive(false);
        }

        /// <summary>
        /// Runs <paramref name="then"/> at once, or after "Discard changes?" when the draft has unsaved changes; <paramref name="keep"/>
        /// runs when the player keeps editing instead.
        /// </summary>
        private static void Confirm(Action then, Action? keep = null)
        {
            if (_draft == null || !_draft.IsDirty)
            {
                then();
                return;
            }
            ShowModal("Discard changes?", "The changes to this look are not saved.", () =>
            {
                ClearLive(reapply: true);
                _draft?.Reset();
                Switch.Forget();
                FurSwitch.Forget();
                Exports.Forget();
                then();
            }, keep);
        }

        // ---- the animal ----

        /// <summary>Edits this animal: the scope chosen stays when the animal offers it, else the entry's default.</summary>
        private static void Select(object animal, bool keepMessage = false)
        {
            ClearLive(reapply: true);
            _animal = animal;
            _context = MaterialsModule.Describe(animal);
            _detached = false;
            _closedKept = false;
            _declined = null;
            _awaitPreview = false;
            if (!keepMessage) _message = "";
            ReadHeader();
            if (_context != null && !Available(_scope)) _scope = EditorScopes.Default(_entry, _context, LookEditorModule.SaveName);
            NewDraft();
            Rebuild();
        }

        private static void AnimalGone(string message)
        {
            CloseModal(); // its "Discard changes?" is moot: the draft goes
            ClearLive(reapply: true); // the others of its skin or species show their saved look again
            Forget();
            _message = message;
            if (IsOpen) Rebuild();
        }

        private static void Forget()
        {
            _animal = null;
            _context = null;
            _draft = null;
            _target = null;
            _below = null;
            _title = _details = null;
            _names = default;
            _detached = false;
            _closedKept = false;
            _declined = null;
            _awaitPreview = false;
            _matching = new List<object>();
            _loadedIds = null;
            _presetWaiting = null;
            Switch.Forget();
            FurSwitch.Forget();
            Exports.Forget();
        }

        /// <summary>False for a game object that was destroyed (a Nursery preview, a sold animal); a VivariumAnimal is a plain object.</summary>
        private static bool Alive(object? animal) => !(animal is UnityEngine.Object unity && unity == null);

        private static bool Available(LookScope scope) => EditorScopes.Offers(_entry, scope, _context, LookEditorModule.SaveName);

        private static void ChooseScope(LookScope scope)
        {
            if (scope == _scope || !Available(scope)) return;
            Confirm(() =>
            {
                ClearLive(reapply: true);
                _scope = scope;
                NewDraft();
                Rebuild();
            });
        }

        /// <summary>A draft of what is saved for the animal at the current scope, where it would save, and who it reaches.</summary>
        private static void NewDraft()
        {
            _presetWaiting = null; // a preset waits for the draft it was asked for
            Switch.Forget();
            FurSwitch.Forget();
            Exports.Forget(); // an export belongs to the draft it was made for (its file stays in Masks)
            if (_animal == null || _context == null)
            {
                _draft = null;
                return;
            }
            var c = _context;
            _draft = new LookDraft(_scope, SavedBlock(c));
            _lastOn = OwnOn();
            _lastGrow = _draft.Current.Fur?.Grow;
            _lastAge = _draft.Current.Fur?.Age;
            try
            {
                _target = SaveTargets.For(_scope, c.Species, c.ModSkin, c.VanillaSkinKey, c.CustomSpeciesModId != null, c.CustomSpeciesModId, MaterialsModule.Table, c.MaterialName);
            }
            catch (ArgumentException ex)
            {
                _target = null;
                _message = ex.Message;
            }
            if (_detached || !Alive(_animal))
            {
                // The Nursery shows another look (or its animal went): nothing to read from it until the window follows it.
                _matching = new List<object>();
                _loadedIds = null;
                _below = null;
                return;
            }
            _matching = MaterialsModule.AnimalsMatching(_scope, _animal).ToList(); // once per draft: a slider move only re-applies (and adds newcomers)
            MatchingRead();
            _below = MaterialsModule.Below(_animal, _scope); // never a higher layer: the draft's scope and above are left out
            if (NextCreated) PushLive(keywords: true); // the look kept for the next animal is in memory only: the preview shows it from the draft
        }

        /// <summary>"This animal" in the Nursery: the look armed for the next animal incubated from the preview (<see cref="LookEditorModule.Pending"/>).</summary>
        private static bool NextCreated => _entry == EditorEntry.Nursery && _scope == LookScope.Animal;

        /// <summary>
        /// The look armed for the next animal (Next created) was bound to an incubation just started. A clean draft of the same
        /// species, skin and sex starts again (nothing is armed now); a draft with unsaved changes stays, and the status line says
        /// its starting point is no longer waiting.
        /// </summary>
        public static void PendingTaken(string species, string? skinKey, string? sex, string? name)
        {
            if (!IsOpen || !NextCreated || _draft == null || _context == null) return;
            if (_context.Species != species || !string.Equals(_context.SkinKey, skinKey, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_context.Sex, sex, StringComparison.Ordinal))
                return;
            var bound = EditorScopes.BoundTo(name, species);
            if (_draft.IsDirty)
            {
                _message = bound + " Your changes here are not armed for the next one until you Save them.";
                Footer();
                return;
            }
            NewDraft();
            _message = bound;
            Rebuild();
        }

        /// <summary>The block saved for this scope (files full): the animal's look, or the table's own entry of the skin or species.</summary>
        private static MaterialOverride? SavedBlock(AnimalContext c)
        {
            switch (_scope)
            {
                case LookScope.Animal:
                    if (NextCreated) // files are full already
                        return LookEditorModule.Pending.Get(c.Species, c.SkinKey, c.Sex) is { } next && next.TryGetValue(c.MaterialName, out var pending) ? pending : null;
                    var look = c.PersistentId is ulong id ? LookEditorModule.Looks?.Get(id) : null;
                    return look != null && look.TryGetValue(c.MaterialName, out var block) ? MaterialTable.WithFullPaths(block, FrameworkMod.TyrantDirectory) : null;
                case LookScope.Skin:
                    return c.ModSkin != null
                        ? MaterialsModule.Table.Block(MaterialOwnerKind.Skin, c.SkinKey, c.MaterialName)
                        : MaterialsModule.Table.Block(MaterialOwnerKind.VanillaSkin, c.VanillaSkinKey, c.MaterialName);
                default:
                    return MaterialsModule.Table.Block(MaterialOwnerKind.Species, c.Species, c.MaterialName);
            }
        }

        // ---- live preview ----

        /// <summary>After an edit: the draft goes live on every animal it reaches; <paramref name="rebuild"/> redraws the rows.</summary>
        private static void Changed(bool rebuild = false)
        {
            if (_draft == null || _context == null) return;
            var on = OwnOn();
            var grow = _draft.Current.Fur?.Grow;
            var age = _draft.Current.Fur?.Age;
            // The shader variant changes only when iridescence or grown fur turns on or off (an age grows fur too).
            var keywords = on != _lastOn || grow != _lastGrow || age != _lastAge;
            _lastOn = on;
            _lastGrow = grow;
            _lastAge = age;
            PushLive(keywords);
            if (rebuild) Rebuild();
            else Footer();
        }

        /// <summary>The draft's own on (a strength of any variant, or the baby strength, above 0); null when it sets neither.</summary>
        private static bool? OwnOn()
        {
            var iridescence = _draft?.Current.Iridescence;
            var strength = iridescence?.Strength;
            if (strength == null && iridescence?.BabyStrength == null) return null;
            return iridescence!.BabyStrength > 0 || (strength != null && LookEditing.Variants.Any(v => LookEditing.StrengthFor(strength, v) > 0));
        }

        private static void PushLive(bool keywords)
        {
            if (NextCreated)
            {
                if (_draft == null || _context == null || _animal == null || _detached || !Alive(_animal)) return;
                MaterialsModule.SetPreviewLook(_animal, _context.MaterialName, _draft.Current); // clean too: what was saved waits in memory only
                _previewShown = _animal;
                MaterialsModule.Reapply(_animal, keywords);
                return;
            }
            if (_draft == null || _context == null || !(_context.KeyFor(_scope) is string key)) return;
            MaterialsModule.SetLive(_scope, key, _context.MaterialName, _draft.IsDirty ? _draft.Current : null); // a clean draft is the saved look
            if (_scope != LookScope.Animal && Time.unscaledTime - _matchingCheckedAt >= 1f) NewcomersJoin(); // at most once a second
            foreach (var animal in _matching)
                if (Alive(animal)) MaterialsModule.Reapply(animal, keywords);
        }

        /// <summary>Takes the draft's live layer away (the saved look shows again).</summary>
        private static void ClearLive(bool reapply)
        {
            if (_previewShown is object shownOn) // whatever the scope or entry is by now
            {
                _previewShown = null;
                MaterialsModule.SetPreviewLook(null, null, null);
                if (reapply && Alive(shownOn)) MaterialsModule.Reapply(shownOn, keywords: true);
            }
            if (_draft == null || _context == null || !(_context.KeyFor(_scope) is string key)) return;
            MaterialsModule.SetLive(_scope, key, _context.MaterialName, null);
            if (!reapply || !_draft.IsDirty) return;
            // An animal of the look that came into the park since the draft started wore the live look from its first frame (the
            // material hook): it shows the saved look again too, before the sweep destroys the live look's textures.
            if (_scope != LookScope.Animal && !JoinMatching() && MaterialsModule.LoadedIds() is { } ids)
                MaterialsModule.ReapplyIds(ids, keywords: true); // no animal left to tell the look by: every animal, to be sure
            foreach (var animal in _matching)
                if (Alive(animal)) MaterialsModule.Reapply(animal, keywords: true);
        }

        // ---- the animals a whole skin or species draft reaches ----

        private static HashSet<ulong>? _loadedIds; // the park's animals when _matching was last read whole
        private static float _matchingCheckedAt = float.NegativeInfinity;

        /// <summary>A new <see cref="_matching"/> was read whole (<see cref="MaterialsModule.AnimalsMatching"/>): the park's animals now are its baseline.</summary>
        private static void MatchingRead()
        {
            _matchingCheckedAt = Time.unscaledTime;
            _loadedIds = _scope == LookScope.Animal || !(MaterialsModule.LoadedIds() is { } ids) ? null : new HashSet<ulong>(ids);
        }

        /// <summary>
        /// While a whole skin or species is edited: an animal that came into the park since (placed, hatched) joins the ones a
        /// slider move re-applies. Only the park's ids are read unless one is new.
        /// </summary>
        private static void NewcomersJoin()
        {
            _matchingCheckedAt = Time.unscaledTime;
            if (!(MaterialsModule.LoadedIds() is { } ids)) return;
            if (_loadedIds != null && ids.All(_loadedIds.Contains))
            {
                if (ids.Count != _loadedIds.Count) _loadedIds = new HashSet<ulong>(ids); // only some left
                return;
            }
            _loadedIds = new HashSet<ulong>(ids);
            JoinMatching();
        }

        /// <summary>
        /// Adds the park's animals of the draft's look that <see cref="_matching"/> does not have yet (it keeps its order: the
        /// Nursery's preview stays first); false when no animal is left to tell the look by.
        /// </summary>
        private static bool JoinMatching()
        {
            var selector = !_detached && Alive(_animal) ? _animal : _matching.FirstOrDefault(Alive);
            if (selector == null) return false;
            // A kept draft (detached) never reaches the Nursery's preview: it shows another look.
            var preview = _detached ? Read(LookEditorModule.PreviewComponent(), "currentAnimalPreview") : null;
            var known = new HashSet<object>(_matching, ReferenceComparer.Instance);
            foreach (var animal in MaterialsModule.AnimalsMatching(_scope, selector))
                if (!(_detached && (ReferenceEquals(animal, _animal) || ReferenceEquals(animal, preview))) && known.Add(animal)) _matching.Add(animal);
            return true;
        }

        // ---- the buttons at the bottom ----

        private static void Save()
        {
            if (_draft == null || _context == null || _target == null) return;
            var c = _context;
            var target = _target; // NewDraft below picks the target again
            // Into a mod, this draft's exports take their fixed names (iridescence-mask.png, …) in the owner's folder; a look of one
            // animal (or the next one) names them in Masks.
            var exportFolder = NextCreated || _target.Kind == SaveTargetKind.AnimalLooks ? null : TargetFolder(create: false);
            var current = exportFolder == null ? _draft.Current : Exports.Final(_draft.Current, exportFolder);
            // Never write what a load rejects: the block is read back as the loader reads it first (a species has no mask "from").
            var skinLevel = _target.Kind == SaveTargetKind.AnimalLooks || _target.Owner?.Kind != MaterialOwnerKind.Species;
            if (MaterialOverrides.Problem(new Dictionary<string, MaterialOverride>(StringComparer.Ordinal) { [c.MaterialName] = current }, "This look", skinLevel) is string problem)
            {
                _message = "Not saved: " + problem;
                Footer();
                return;
            }
            var missing = new List<string>();
            string where;
            try
            {
                if (NextCreated)
                {
                    // Armed in memory for the next incubation from this preview: that animal is bound to it in this save's looks file.
                    if (!LookEditorModule.LooksWritable)
                    {
                        _message = "Not saved: this save's looks file cannot be written (save the park once, or see the MelonLoader log).";
                        Footer();
                        return;
                    }
                    LookEditorModule.Pending.Set(c.Species, c.SkinKey, c.Sex, c.MaterialName, current);
                    where = TargetName();
                }
                else if (_target.Kind == SaveTargetKind.AnimalLooks)
                {
                    if (!(c.PersistentId is ulong id) || LookEditorModule.Looks == null || !LookEditorModule.LooksWritable)
                    {
                        _message = LookEditorModule.Looks != null && c.PersistentId != null && LookEditorModule.SaveName != null
                            ? "Not saved: this save's looks file has problems (see the MelonLoader log)."
                            : "This animal's look cannot be saved: save the park first (its looks follow the save), or the animal has no id.";
                        Footer();
                        return;
                    }
                    var looks = LookEditorModule.Looks;
                    var map = looks.Get(id) is { } existing ? new Dictionary<string, MaterialOverride>(existing, StringComparer.Ordinal) : new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
                    var block = LookFiles.Relative(current, FrameworkMod.TyrantDirectory); // UserData/Tyrant-relative, like "Masks/x.png"
                    if (block.IsEmpty) map.Remove(c.MaterialName);
                    else map[c.MaterialName] = block;
                    var before = looks.Get(id);
                    looks.Set(id, map);
                    bool written;
                    try
                    {
                        written = LookEditorModule.WriteLooks();
                    }
                    catch
                    {
                        looks.Set(id, before);
                        throw;
                    }
                    if (!written)
                    {
                        looks.Set(id, before);
                        _message = "Not saved: this save's looks file has problems (see the MelonLoader log).";
                        Footer();
                        return;
                    }
                    where = "this save's looks";
                }
                else
                {
                    var owner = _target.Owner!;
                    var modDir = ModDirectory(create: true)!;
                    // Before the reload, so the new image is the one read; the images it replaces come back if the rest fails.
                    var committed = exportFolder != null ? Exports.Commit(_draft.Current, exportFolder) : null;
                    try
                    {
                        var block = LookFiles.Adopt(current, modDir, MaterialFolders.FolderOf(owner), missing); // the mod keeps its own copy of outside images
                        MaterialFolders.SaveOwner(modDir, owner, new Dictionary<string, MaterialOverride>(StringComparer.Ordinal) { [c.MaterialName] = block });
                    }
                    catch
                    {
                        committed?.Undo();
                        throw;
                    }
                    committed?.Keep();
                    where = TargetName();
                }
            }
            catch (Exception ex) when (ex is ManifestException || ex is IOException || ex is UnauthorizedAccessException)
            {
                FrameworkMod.Log.Warning($"The look could not be saved: {ex.Message}");
                FilesWritten(); // some may have been
                _message = "Not saved: " + ex.Message;
                Footer();
                return;
            }
            FilesWritten();
            ClearLive(reapply: false);
            if (_target.Kind != SaveTargetKind.AnimalLooks) MaterialsModule.Reload(MaterialsModule.Mods); // re-reads the folders, re-applies the park
            if (_animal != null && !_detached && Alive(_animal)) MaterialsModule.Reapply(_animal, keywords: true); // the Nursery preview is not in the park's lists
            _draft.MarkSaved();
            NewDraft(); // from what was saved (images a mod adopted have their new place)
            // Vanilla Looks turned off (or broken) at the start is never loaded behind the player's back: the save does not show.
            var vanillaOff = (target.Kind == SaveTargetKind.CreateVanillaLooks || target.ModId == ModCatalog.VanillaLooksId)
                && MaterialsModule.Mods.All(m => m.Manifest.Id != ModCatalog.VanillaLooksId);
            _message = (vanillaOff
                    ? $"Saved into {VanillaLooks.Name}, but it is turned off or broken, so the look does not show (see the MelonLoader log)."
                    : "Saved to " + where + ".")
                + (missing.Count == 0 ? "" : " Left out (missing): " + string.Join(", ", missing.Select(Path.GetFileName)) + ".");
            if (NextCreated) _message = EditorScopes.SavedForNext(c.Species);
            FrameworkMod.Log.Msg($"Look of {c.MaterialName} ({_scope}) saved to {where}.");
            Rebuild();
        }

        private static void Reset()
        {
            if (_draft == null) return;
            _presetWaiting = null;
            _draft.Reset();
            Switch.Forget();
            FurSwitch.Forget();
            Exports.Forget();
            _message = "";
            Changed(rebuild: true);
        }

        /// <summary>Re-reads every image (a PNG edited outside the game), then shows the edit on top again.</summary>
        private static void ReloadImages()
        {
            FilesWritten(); // the player may have added or changed files: the folders are read again
            if (LookEditorModule.Looks is AnimalLooks looks)
            {
                foreach (var id in looks.Ids.ToList()) looks.Set(id, new Dictionary<string, MaterialOverride>(looks.Get(id)!, StringComparer.Ordinal)); // prepared again
                MaterialsModule.SetAnimalLooks(looks);
            }
            MaterialsModule.Reload(MaterialsModule.Mods);
            if (_animal != null && !_detached && Alive(_animal))
            {
                MaterialsModule.Reapply(_animal, keywords: true);
                _below = MaterialsModule.Below(_animal, _scope); // the table was read again
            }
            PushLive(keywords: true);
            _message = "Images read again.";
            Rebuild();
        }

        /// <summary>
        /// Writes the mask the animal shows now as a grey PNG (all white for Everywhere, for no mask at all, and while grown fur
        /// covers the whole body; all black, at the same size, for a fur slot the fur age leaves bare) into UserData/Tyrant/Masks under a new name, and points the draft at it.
        /// </summary>
        private static void ExportMask()
        {
            if (_animal == null || _draft == null || _target == null || _context == null) return;
            if (_detached || !Alive(_animal))
            {
                _message = "The Nursery shows another look now: Save or Reset first, then export from the animal it shows.";
                Footer();
                return;
            }
            var texture = MaterialsModule.EffectiveMask(_animal, fur: _onFurTab, infant: false, out var white, out var black, out var taken);
            // The game's images never go into files: a species' mask stays a reference (the draft's, or the one the animal wears).
            if (!_onFurTab && (taken ?? (_draft.Current.Iridescence?.Mask ?? _below?.Iridescence?.Mask)?.Species) is string species)
            {
                _message = SpeciesLooks.NameOf(species) + "'s mask stays a reference and is not exported; pick Channel or PNG to make one of your own.";
                Footer();
                return;
            }
            if (texture == null && white == null)
            {
                _message = _onFurTab ? "This animal has no fur mask to export." : "This animal shows no iridescence mask to export.";
                Footer();
                return;
            }
            if (NextCreated)
            {
                _message = "Export mask PNG needs a placed animal: Save, incubate and place the animal, then export from its own panel.";
                Footer();
                return;
            }
            var kind = _onFurTab ? ExportKind.FurMask : ExportKind.IridescenceMask;
            if (!(TryExport(kind, () => white is Vector2Int size ? SolidPng(size, white: !black) : GreyPng(texture!)) is string file)) return;
            if (_onFurTab) _draft.SetFurMask(new MaskSource { File = file }, infant: false);
            else _draft.SetMask(new MaskSource { File = file });
            Changed(rebuild: true);
        }

        /// <summary>Writes the shimmer colours shown (Colours or a thin film) as a ramp strip PNG into Masks, and points the draft at it.</summary>
        private static void ExportColours()
        {
            if (_draft == null || _context == null || _target == null) return;
            var ramp = _draft.Current.Iridescence?.Ramp ?? _below?.Iridescence?.Ramp;
            var problem = MaskExports.ColoursFrom(ramp, out var stops);
            if (problem != ColoursExport.Ok)
            {
                _message = problem == ColoursExport.Species
                    ? SpeciesLooks.NameOf(ramp!.Species!) + "'s colours stay a reference and are not exported; pick Colours or Thin film to export your own."
                    : problem == ColoursExport.File
                        ? "These colours are a PNG already: edit it, then Reload images."
                        : "The game's colours stay the game's; pick Colours or Thin film to export your own.";
                Footer();
                return;
            }
            if (!(TryExport(ExportKind.Colours, () => RampPng(stops!)) is string file)) return;
            _draft.SetRamp(new RampSource { File = file });
            Changed(rebuild: true);
        }

        /// <summary>
        /// Writes an export into UserData/Tyrant/Masks under its readable name (a new number when it is taken: nothing there is
        /// ever overwritten) and says so on the status line; null, with the reason there, when it could not be written.
        /// </summary>
        private static string? TryExport(ExportKind kind, Func<byte[]> png)
        {
            try
            {
                Directory.CreateDirectory(MasksDirectory);
                var name = MaskExports.FileName(_scope, _names.Species ?? _context!.Species, _names.Skin, _names.Animal, kind);
                var file = Path.Combine(MasksDirectory, MaskExports.Unique(name, Directory.GetFiles(MasksDirectory)));
                var bytes = png();
                using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes, 0, bytes.Length);
                FilesWritten();
                Exports.Add(file, kind);
                _message = "Exported " + Path.GetFileName(file) + " to UserData/Tyrant/Masks. Edit it, then Reload images.";
                return file;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _message = "Not exported: " + ex.Message;
                Footer();
                return null;
            }
        }

        /// <summary>An all-white (fur or shimmer everywhere) or all-black (no fur at that age) PNG of this size, to paint on.</summary>
        private static byte[] SolidPng(Vector2Int size, bool white) =>
            Png(size.x, size.y, MaskExports.SolidPixels(size.x, size.y, white), linear: true);

        /// <summary>The texture's red channel as a grey PNG (read like a mask made from a skin map, so compressed maps read too).</summary>
        private static byte[] GreyPng(Texture source)
        {
            var grey = MaterialsModule.ReadGrey(source, 'r', invert: false, mipmaps: false);
            try
            {
                return grey.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.Destroy(grey);
            }
        }

        /// <summary>The colours as a ramp strip, the same pixels the shader samples from them.</summary>
        private static byte[] RampPng(IReadOnlyList<Rgb> stops) =>
            Png(MaskExports.RampWidth, MaskExports.RampHeight, IridescenceRamp.Pixels(stops, MaskExports.RampWidth, MaskExports.RampHeight), linear: false);

        private static byte[] Png(int width, int height, byte[] rgba, bool linear)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            try
            {
                texture.SetPixelData(rgba, 0);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>Opens UserData/Tyrant/Masks in File Explorer (made first when it is missing).</summary>
        private static void OpenMasksFolder()
        {
            try
            {
                var folder = Path.GetFullPath(MasksDirectory);
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                _message = "The Masks folder could not be opened: " + ex.Message;
                Footer();
            }
        }

        /// <summary>Older versions staged exports in UserData/Tyrant/Looks/.pending until Save; what they left there goes.</summary>
        private static void RemoveOldPending()
        {
            var folder = Path.Combine(FrameworkMod.TyrantDirectory, "Looks", ".pending");
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // tried again on the next start
            }
        }

        // ---- where things go ----

        private static string MasksDirectory => Path.Combine(FrameworkMod.TyrantDirectory, "Masks");

        /// <summary>The save target's mod folder (created for Vanilla Looks when <paramref name="create"/>); null when it has none.</summary>
        private static string? ModDirectory(bool create)
        {
            if (_target == null) return null;
            if (_target.Kind == SaveTargetKind.CreateVanillaLooks)
                return create ? VanillaLooks.EnsureMod(FrameworkMod.ModsDirectory) : Path.Combine(FrameworkMod.ModsDirectory, ModCatalog.VanillaLooksId);
            if (_target.Kind != SaveTargetKind.Mod) return null;
            var mod = MaterialsModule.Mods.FirstOrDefault(m => m.Manifest.Id == _target.ModId);
            if (mod != null) return mod.Directory;
            if (create) throw new IOException($"the mod '{_target.ModId}' is not loaded");
            return null;
        }

        /// <summary>The folder exports go to and PNGs are offered from: the owner's material folder, or this animal's folder under Looks.</summary>
        private static string? TargetFolder(bool create)
        {
            if (_target == null || _context == null || NextCreated) return null; // the next animal has no folder until it exists
            if (_target.Kind == SaveTargetKind.AnimalLooks)
            {
                var folder = AnimalLooks.FolderFor(FrameworkMod.TyrantDirectory, LookEditorModule.SaveName);
                if (folder == null || _context.PersistentId == null) return null;
                return Path.Combine(folder, _context.PersistentId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            var modDir = ModDirectory(create);
            return modDir == null ? null : Path.Combine(modDir, MaterialFolders.FolderOf(_target.Owner!).Replace('/', Path.DirectorySeparatorChar));
        }

        private static string TargetName()
        {
            if (_target == null) return "nowhere";
            if (NextCreated && _context != null) return $"the next {_context.Species} you incubate";
            switch (_target.Kind)
            {
                case SaveTargetKind.AnimalLooks:
                    return "this save's looks";
                case SaveTargetKind.CreateVanillaLooks:
                    return VanillaLooks.Name + (VanillaLooksThere() ? "" : " (new)");
                default:
                    var mod = MaterialsModule.Mods.FirstOrDefault(m => m.Manifest.Id == _target.ModId)?.Manifest;
                    return mod == null || string.IsNullOrEmpty(mod.Name) ? _target.ModId ?? "?" : mod.Name;
            }
        }

        private static float _vanillaLooksReadAt = float.NegativeInfinity;
        private static bool _vanillaLooksThere;

        /// <summary>
        /// True when the Vanilla Looks mod has its manifest on disk: looked at again at most once a second (the status line asks
        /// on every slider move), and at once after a save (<see cref="FilesWritten"/>).
        /// </summary>
        private static bool VanillaLooksThere()
        {
            var now = Time.unscaledTime;
            if (now < _vanillaLooksReadAt || now - _vanillaLooksReadAt >= 1f)
            {
                _vanillaLooksThere = File.Exists(Path.Combine(FrameworkMod.ModsDirectory, ModCatalog.VanillaLooksId, ModManifest.FileName));
                _vanillaLooksReadAt = now;
            }
            return _vanillaLooksThere;
        }

        /// <summary>The editor wrote files (a save, an export): what it remembered of the folders is looked at again.</summary>
        private static void FilesWritten()
        {
            _vanillaLooksReadAt = float.NegativeInfinity;
        }

        /// <summary>The PNGs offered for a ramp or mask: the target's folder and UserData/Tyrant/Masks, plus the one in use.</summary>
        private static List<string> Pngs(string? current)
        {
            var folders = new List<string> { MasksDirectory };
            if (TargetFolder(create: false) is string folder) folders.Insert(0, folder);
            var files = LookFiles.Pngs(folders).ToList();
            if (current != null && !files.Any(f => string.Equals(Path.GetFullPath(f), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))) files.Insert(0, current);
            return files;
        }
    }
}
