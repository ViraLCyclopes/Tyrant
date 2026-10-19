using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;

namespace Tyrant.Framework
{
    /// <summary>
    /// The in-game look editor's wiring: the park's per-animal looks (UserData/Tyrant/Looks/&lt;park&gt;.json, never the save),
    /// read when a park has loaded and pruned of animals that are gone when it loads or saves; the game's selection; and the
    /// window's tick. The game's hooks only queue their news, handled on the next frame, and every handler is wrapped: an
    /// exception is logged once and turns the editor off, never the game.
    /// </summary>
    internal static class LookEditorModule
    {
        private static readonly Type? Game = AccessTools.TypeByName("PrehistoricKingdom.Game");
        private static readonly Type? Core = AccessTools.TypeByName("Utility.GameCore");
        private static readonly Type? SimManager = AccessTools.TypeByName("PrehistoricKingdom.SimManager");
        private static readonly Type? AnimalType = AccessTools.TypeByName("PrehistoricKingdom.IAnimal");

        private static readonly object Gate = new object();
        private static readonly List<ulong> Removed = new List<ulong>(); // OnAnimalRemoved, handled in Tick
        private static bool _loadedNews, _savedNews, _selectionNews;
        private static bool _disabled;
        private static bool _started; // Start ran (the framework loaded): until then Tick does nothing

        private static object? _gui; // the park's InterfaceManager: a new one is a new park
        private static bool _parkRead;
        private static object? _selection; // the SelectionHandler we listen to
        private static Delegate? _selectionHandler;

        /// <summary>The park's per-animal looks, or null outside a park.</summary>
        public static AnimalLooks? Looks { get; private set; }

        /// <summary>True when the park's looks file had problems: it is then never written over (the player can fix it).</summary>
        private static bool _looksUnreadable;

        public static bool Disabled => _disabled;

        public static void Start()
        {
            if (!MaterialsModule.Active)
            {
                FrameworkMod.Log.Warning("The look editor is off: mod materials could not be hooked (see above).");
                _disabled = true;
                return;
            }
            try
            {
                LookEditorWindow.Start();
                Subscribe(Game, "OnLoadingProcessComplete", nameof(OnLoadingProcessComplete), null);
                Subscribe(AccessTools.TypeByName("PrehistoricKingdom.SaveLoadManager"), "OnSaveCompleted", nameof(OnSaveCompleted), null);
                Subscribe(AccessTools.TypeByName("PrehistoricKingdom.AnimalManager"), "OnAnimalRemoved", nameof(OnAnimalRemoved), null);
                _started = true;
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>Once a frame (FrameworkMod.OnUpdate).</summary>
        public static void Tick()
        {
            if (_disabled || !_started) return;
            try
            {
                var gui = InPark() ? Property(Game, "GUI") : null;
                if (gui is UnityEngine.Object unity && unity == null) gui = null; // destroyed with its scene
                if (!ReferenceEquals(gui, _gui))
                {
                    LeavePark();
                    _gui = gui;
                }
                if (gui == null) return;
                ListenToSelection();

                bool loaded, saved, selection;
                List<ulong> removed;
                lock (Gate)
                {
                    (loaded, saved, selection) = (_loadedNews, _savedNews, _selectionNews);
                    _loadedNews = _savedNews = _selectionNews = false;
                    removed = Removed.ToList();
                    Removed.Clear();
                }
                // The game's event (again: another park loaded under the same interface), or should it be missed the game saying
                // it is fully loaded.
                if (loaded || (!_parkRead && Property(Core, "IsFullyLoaded") is true))
                {
                    if (_parkRead) LookEditorWindow.LeavePark();
                    ParkLoaded();
                }
                if (removed.Count > 0) AnimalsRemoved(removed);
                if (saved && _parkRead) ParkSaved();
                LookEditorWindow.Tick(gui, _parkRead, selection);
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>The park's name (its looks file); null outside a park.</summary>
        public static string? ParkName => SimManager == null ? null : AccessTools.Field(SimManager, "parkName")?.GetValue(null) as string;

        /// <summary>The park's looks file, or null when the park has no name.</summary>
        public static string? LooksPath => ParkName is string park ? AnimalLooks.PathFor(FrameworkMod.TyrantDirectory, park) : null;

        /// <summary>The single selected object when it is an animal (park or vivarium); null otherwise.</summary>
        public static object? SelectedAnimal()
        {
            var handler = Property(Game, "Selection");
            var selected = handler == null ? null : AccessTools.Property(handler.GetType(), "SingleSelected")?.GetValue(handler, null);
            return IsAnimal(selected) ? selected : null;
        }

        public static bool IsAnimal(object? value) =>
            value != null && AnimalType != null && AnimalType.IsInstanceOfType(value) && !(value is UnityEngine.Object unity && unity == null);

        /// <summary>Writes the store after the editor saved one animal's look; false (and a log line) when it could not be.</summary>
        public static bool WriteLooks()
        {
            var path = LooksPath;
            if (Looks == null || path == null) return false;
            if (_looksUnreadable)
            {
                FrameworkMod.Log.Warning($"\"{path}\" has problems (see above); fix or delete it before saving looks for single animals.");
                return false;
            }
            Looks.Write(path);
            MaterialsModule.SetAnimalLooks(Looks);
            return true;
        }

        /// <summary>Logs the first failure and turns the editor off (its window and button go away); the game goes on.</summary>
        public static void Fail(Exception ex)
        {
            if (_disabled) return;
            _disabled = true;
            FrameworkMod.Log.Error("The Tyrant look editor failed and is off until the game restarts; nothing was saved by this step: " + ex);
            try
            {
                LookEditorWindow.Disable();
            }
            catch (Exception)
            {
                // the window is as broken as it is; the game goes on
            }
        }

        // ---- park ----

        private static bool InPark() => Game != null && Property(Game, "HasInstance") is true;

        private static void ParkLoaded()
        {
            _parkRead = true;
            var path = LooksPath;
            var problems = new List<string>();
            Looks = path == null ? new AnimalLooks() : AnimalLooks.Read(path, problems);
            foreach (var problem in problems) FrameworkMod.Log.Warning(problem);
            _looksUnreadable = problems.Count > 0;
            Prune(path);
            MaterialsModule.SetAnimalLooks(Looks);
            if (Looks.Count > 0)
            {
                FrameworkMod.Log.Msg($"{Looks.Count} single-animal look(s) for this park.");
                MaterialsModule.ReapplyIds(Looks.Ids.ToList(), keywords: true); // the animals were set up before the looks were read
            }
            LookEditorWindow.ParkLoaded();
        }

        private static void ParkSaved()
        {
            if (Looks == null) return;
            if (Prune(LooksPath)) MaterialsModule.SetAnimalLooks(Looks);
        }

        /// <summary>Drops looks whose animal is gone and writes the file when any were; never on an empty or unreadable park.</summary>
        private static bool Prune(string? path)
        {
            if (Looks == null || Looks.Count == 0) return false;
            var alive = MaterialsModule.LoadedIds();
            if (alive.Count == 0) return false; // no animals read (or none yet): nothing is dropped on a guess
            var pruned = Looks.Prune(alive);
            if (pruned == 0) return false;
            FrameworkMod.Log.Msg($"{pruned} look(s) of animals no longer in the park were dropped.");
            if (path != null && !_looksUnreadable) Looks.Write(path);
            return true;
        }

        private static void AnimalsRemoved(List<ulong> ids)
        {
            if (Looks == null) return;
            var changed = false;
            foreach (var id in ids)
                if (Looks.Get(id) != null)
                {
                    Looks.Set(id, null); // in memory: the file follows on the next park save
                    changed = true;
                }
            if (changed) MaterialsModule.SetAnimalLooks(Looks);
            LookEditorWindow.AnimalsRemoved(ids);
        }

        private static void LeavePark()
        {
            if (_gui == null) return;
            LookEditorWindow.LeavePark();
            Looks = null;
            _looksUnreadable = false;
            _parkRead = false;
            _selection = null;
            _selectionHandler = null;
            MaterialsModule.SetAnimalLooks(null);
        }

        // ---- the game's events (they only queue; Tick handles them on the main thread) ----

        private static void OnLoadingProcessComplete()
        {
            lock (Gate) _loadedNews = true;
        }

        private static void OnSaveCompleted(string savePath)
        {
            lock (Gate) _savedNews = true;
        }

        private static void OnAnimalRemoved(object animal)
        {
            try
            {
                var id = PersistentId(animal);
                if (id != null) lock (Gate) Removed.Add(id.Value);
            }
            catch (Exception)
            {
                // an id that cannot be read keeps its look until the park is saved or loaded (then it is pruned)
            }
        }

        private static void OnSelectionChange()
        {
            lock (Gate) _selectionNews = true;
        }

        /// <summary>PersistentID.uniqueID of an animal with a valid one.</summary>
        public static ulong? PersistentId(object animal)
        {
            var type = animal.GetType();
            if (!(AccessTools.Property(type, "HasValidPersistentID")?.GetValue(animal, null) is true)) return null;
            var id = AccessTools.Property(type, "PersistentID")?.GetValue(animal, null);
            return id != null && AccessTools.Field(id.GetType(), "uniqueID")?.GetValue(id) is ulong unique ? unique : (ulong?)null;
        }

        /// <summary>Game.Selection is made per park: listen to the current one's OnSelectionChange.</summary>
        private static void ListenToSelection()
        {
            var selection = Property(Game, "Selection");
            if (selection == null || ReferenceEquals(selection, _selection)) return;
            var changed = selection.GetType().GetEvent("OnSelectionChange");
            if (changed == null)
            {
                _selection = selection;
                FrameworkMod.Log.Warning("SelectionHandler.OnSelectionChange was not found (game updated?); the look editor follows the selection when it opens.");
                return;
            }
            _selectionHandler ??= Delegate.CreateDelegate(changed.EventHandlerType, AccessTools.Method(typeof(LookEditorModule), nameof(OnSelectionChange)));
            changed.AddEventHandler(selection, _selectionHandler);
            _selection = selection;
            OnSelectionChange();
        }

        /// <summary>Adds one of our static handlers to a static game event (a parameter of a reference type binds to object).</summary>
        private static void Subscribe(Type? type, string eventName, string handler, object? target)
        {
            var info = type?.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (info == null)
            {
                FrameworkMod.Log.Warning($"{type?.Name ?? "?"}.{eventName} was not found (game updated?); the look editor works without it.");
                return;
            }
            var method = AccessTools.Method(typeof(LookEditorModule), handler);
            var callback = Delegate.CreateDelegate(info.EventHandlerType, method);
            info.RemoveEventHandler(target, callback); // never twice
            info.AddEventHandler(target, callback);
        }

        /// <summary>A static property of the game; null while it cannot be read (e.g. its instance is between parks).</summary>
        private static object? Property(Type? type, string name)
        {
            try
            {
                return type == null ? null : AccessTools.Property(type, name)?.GetValue(null, null);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }
    }
}
