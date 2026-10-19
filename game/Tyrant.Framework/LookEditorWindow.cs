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
        private static (GameObject Button, Component Label) _irisTab, _furTab, _export, _reload, _reset, _save;
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
        private static bool? _lastOn;
        private static List<object> _matching = new List<object>();
        private static readonly IridescenceSwitch Switch = new IridescenceSwitch();

        /// <summary>Exported masks waiting for Save, outside every mod (UserData/Tyrant/Looks/.pending).</summary>
        private static StagedFiles? _stagedFiles;

        private static StagedFiles Staged => _stagedFiles ??= new StagedFiles(Path.Combine(FrameworkMod.TyrantDirectory, "Looks", ".pending"));

        private static bool IsOpen => _window != null && _window.activeSelf;

        public static void Start()
        {
            _prefs = MelonPreferences.CreateCategory("Tyrant");
            _x = _prefs.CreateEntry("EditorX", Unplaced);
            _y = _prefs.CreateEntry("EditorY", 96f);
            _w = _prefs.CreateEntry("EditorW", 360f);
            _h = _prefs.CreateEntry("EditorH", 600f);
            Staged.Clean(); // exports an earlier run left unsaved
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
                _canvas = _window = null;
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
            Staged.Discard();
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
                Staged.Discard();
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
            _detached = false;
            _closedKept = false;
            _declined = null;
            _awaitPreview = false;
            _matching = new List<object>();
            Switch.Forget();
            Staged.Discard();
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
            Switch.Forget();
            Staged.Discard(); // an export belongs to the draft it was made for
            if (_animal == null || _context == null)
            {
                _draft = null;
                return;
            }
            var c = _context;
            _draft = new LookDraft(_scope, SavedBlock(c));
            _lastOn = OwnOn();
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
                _below = null;
                return;
            }
            _matching = MaterialsModule.AnimalsMatching(_scope, _animal).ToList(); // once per draft: a slider move only re-applies
            _below = MaterialsModule.Below(_animal, _scope); // never a higher layer: the draft's scope and above are left out
        }

        /// <summary>The block saved for this scope (files full): the animal's look, or the table's own entry of the skin or species.</summary>
        private static MaterialOverride? SavedBlock(AnimalContext c)
        {
            switch (_scope)
            {
                case LookScope.Animal:
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
            var keywords = on != _lastOn; // the shader variant changes only when iridescence turns on or off
            _lastOn = on;
            PushLive(keywords);
            if (rebuild) Rebuild();
            else Footer();
        }

        private static bool? OwnOn()
        {
            var strength = _draft?.Current.Iridescence?.Strength;
            if (strength == null) return null;
            return LookEditing.Variants.Any(v => LookEditing.StrengthFor(strength, v) > 0);
        }

        private static void PushLive(bool keywords)
        {
            if (_draft == null || _context == null || !(_context.KeyFor(_scope) is string key)) return;
            MaterialsModule.SetLive(_scope, key, _context.MaterialName, _draft.IsDirty ? _draft.Current : null); // a clean draft is the saved look
            foreach (var animal in _matching)
                if (Alive(animal)) MaterialsModule.Reapply(animal, keywords);
        }

        /// <summary>Takes the draft's live layer away (the saved look shows again).</summary>
        private static void ClearLive(bool reapply)
        {
            if (_draft == null || _context == null || !(_context.KeyFor(_scope) is string key)) return;
            MaterialsModule.SetLive(_scope, key, _context.MaterialName, null);
            if (!reapply || !_draft.IsDirty) return;
            foreach (var animal in _matching)
                if (Alive(animal)) MaterialsModule.Reapply(animal, keywords: true);
        }

        // ---- the buttons at the bottom ----

        private static void Save()
        {
            if (_draft == null || _context == null || _target == null) return;
            var c = _context;
            var target = _target; // NewDraft below picks the target again
            var current = Staged.Final(_draft.Current); // exports under their fixed names (iridescence-mask.png, fur-mask.png)
            // Never write what a load rejects: the block is read back as the loader reads it first (a species has no mask "from").
            var skinLevel = _target.Kind == SaveTargetKind.AnimalLooks || _target.Owner?.Kind != MaterialOwnerKind.Species;
            if (MaterialOverrides.Problem(new Dictionary<string, MaterialOverride>(StringComparer.Ordinal) { [c.MaterialName] = current }, "This look", skinLevel) is string problem)
            {
                _message = "Not saved: " + problem;
                Footer();
                return;
            }
            var missing = new List<string>();
            var committed = false;
            string where;
            try
            {
                if (_target.Kind == SaveTargetKind.AnimalLooks)
                {
                    if (!(c.PersistentId is ulong id) || LookEditorModule.Looks == null || !LookEditorModule.LooksWritable)
                    {
                        _message = LookEditorModule.Looks != null && c.PersistentId != null && LookEditorModule.SaveName != null
                            ? "Not saved: this save's looks file has problems (see the MelonLoader log)."
                            : "This animal's look cannot be saved: save the park first (its looks follow the save), or the animal has no id.";
                        Footer();
                        return;
                    }
                    Staged.Commit(_draft.Current); // before the store is prepared again, so the new image is the one read
                    committed = true;
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
                    Staged.Commit(_draft.Current); // before the reload, so the new image is the one read
                    committed = true;
                    var block = LookFiles.Adopt(current, modDir, MaterialFolders.FolderOf(owner), missing); // the mod keeps its own copy of outside images
                    MaterialFolders.SaveOwner(modDir, owner, new Dictionary<string, MaterialOverride>(StringComparer.Ordinal) { [c.MaterialName] = block });
                    where = TargetName();
                }
            }
            catch (Exception ex) when (ex is ManifestException || ex is IOException || ex is UnauthorizedAccessException)
            {
                FrameworkMod.Log.Warning($"The look could not be saved: {ex.Message}");
                if (committed) PointAtFinal(current); // the exports are at their fixed names now
                _message = "Not saved: " + ex.Message;
                Footer();
                return;
            }
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
            FrameworkMod.Log.Msg($"Look of {c.MaterialName} ({_scope}) saved to {where}.");
            Rebuild();
        }

        /// <summary>After the exports moved to their fixed names: the draft names them there too.</summary>
        private static void PointAtFinal(MaterialOverride final)
        {
            if (_draft == null) return;
            if (final.Iridescence?.Mask is MaskSource mask && mask.File != _draft.Current.Iridescence?.Mask?.File) _draft.SetMask(mask);
            if (final.Fur?.Mask is MaskSource fur && fur.File != _draft.Current.Fur?.Mask?.File) _draft.SetFurMask(fur, infant: false);
            PushLive(keywords: false);
        }

        private static void Reset()
        {
            if (_draft == null) return;
            _draft.Reset();
            Switch.Forget();
            Staged.Discard();
            _message = "";
            Changed(rebuild: true);
        }

        /// <summary>Re-reads every image (a PNG edited outside the game), then shows the edit on top again.</summary>
        private static void ReloadImages()
        {
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

        /// <summary>Writes the mask the animal shows now as a grey PNG into the save target's folder, and points the draft at it.</summary>
        private static void ExportMask()
        {
            if (_animal == null || _draft == null || _target == null) return;
            if (_detached || !Alive(_animal))
            {
                _message = "The Nursery shows another look now: Save or Reset first, then export from the animal it shows.";
                Footer();
                return;
            }
            var texture = MaterialsModule.EffectiveMask(_animal, fur: _onFurTab, infant: false);
            if (texture == null)
            {
                _message = _onFurTab ? "This animal has no fur mask to export." : "This animal shows no iridescence mask to export.";
                Footer();
                return;
            }
            var folder = TargetFolder(create: false); // Vanilla Looks is made by Save, not by an export
            if (folder == null)
            {
                _message = "There is nowhere to export this look's mask.";
                Footer();
                return;
            }
            // Staged until Save: the fixed name may be the saved look's own mask, which Reset or Discard must keep.
            var file = Staged.Stage(Path.Combine(folder, _onFurTab ? LookEditing.FurMaskFile : LookEditing.IridescenceMaskFile));
            try
            {
                WriteGreyPng(texture, file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _message = "Not exported: " + ex.Message;
                Footer();
                return;
            }
            if (_onFurTab) _draft.SetFurMask(new MaskSource { File = file }, infant: false);
            else _draft.SetMask(new MaskSource { File = file });
            _message = "Exported " + (_onFurTab ? LookEditing.FurMaskFile : LookEditing.IridescenceMaskFile) + "; Save makes it the default, Reset drops it.";
            Changed(rebuild: true);
        }

        /// <summary>The texture's red channel as a grey PNG (read like a mask made from a skin map, so compressed maps read too).</summary>
        private static void WriteGreyPng(Texture source, string file)
        {
            var grey = MaterialsModule.ReadGrey(source, 'r', invert: false, mipmaps: false);
            try
            {
                File.WriteAllBytes(file, grey.EncodeToPNG()); // a new staged name each export: nothing is overwritten
            }
            finally
            {
                UnityEngine.Object.Destroy(grey);
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
            if (_target == null || _context == null) return null;
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
            switch (_target.Kind)
            {
                case SaveTargetKind.AnimalLooks:
                    return "this save's looks";
                case SaveTargetKind.CreateVanillaLooks:
                    return VanillaLooks.Name + (File.Exists(Path.Combine(FrameworkMod.ModsDirectory, ModCatalog.VanillaLooksId, ModManifest.FileName)) ? "" : " (new)");
                default:
                    var mod = MaterialsModule.Mods.FirstOrDefault(m => m.Manifest.Id == _target.ModId)?.Manifest;
                    return mod == null || string.IsNullOrEmpty(mod.Name) ? _target.ModId ?? "?" : mod.Name;
            }
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
