using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;

namespace Tyrant.Framework
{
    /// <summary>
    /// The in-game look editor's wiring: the per-animal looks, which follow the save file (UserData/Tyrant/Looks/&lt;save&gt;.json,
    /// never the save itself): read from the loaded save's file when a park has loaded, written to each save's own file when
    /// the game saves (Save As and autosaves included), and deleted with the save; pruned of animals that are gone only in the
    /// file being loaded or written. Also the window's tick. The game's hooks only queue their news,
    /// handled on the next frame, and every handler is wrapped: an exception is logged once and turns the editor off, never the game.
    /// </summary>
    internal static class LookEditorModule
    {
        private static readonly Type? Game = AccessTools.TypeByName("PrehistoricKingdom.Game");
        private static readonly Type? Core = AccessTools.TypeByName("Utility.GameCore");
        private static readonly Type? AnimalType = AccessTools.TypeByName("PrehistoricKingdom.IAnimal");
        private static readonly Type? SceneLoader = AccessTools.TypeByName("PrehistoricKingdom.PersistentSceneLoader");

        private static readonly object Gate = new object();
        private static readonly List<ulong> Removed = new List<ulong>(); // OnAnimalRemoved, handled in Tick
        private static readonly List<string> Saved = new List<string>(); // OnSaveCompleted's paths, handled in Tick
        private static readonly List<string> SavesRemoved = new List<string>(); // OnSaveRemoved's paths, handled in Tick (menus too)
        private static readonly List<(object Animal, int Tries)> PlacedNew = new List<(object, int)>(); // OnAnimalPlacedNew's animals, handled in Tick
        private static readonly HashSet<ulong> Gone = new HashSet<ulong>(); // removed since the park was loaded or saved: dropped on its save
        private static readonly HashSet<string> WriteWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // looks files a write failed for
        private static bool _loadedNews;
        private static bool _disabled;
        private static bool _started; // Start ran (the framework loaded): until then Tick does nothing

        private static object? _gui; // the park's InterfaceManager: a new one is a new park
        private static bool _parkRead;

        /// <summary>The park's per-animal looks, or null outside a park.</summary>
        public static AnimalLooks? Looks { get; private set; }

        /// <summary>
        /// Looks saved in the Nursery for the next animal created ("Next created"): in memory only, dropped with the park; the
        /// animal placed to match takes its look into <see cref="Looks"/> (and this save's file).
        /// </summary>
        public static PendingLooks Pending { get; } = new PendingLooks();

        /// <summary>How many frames a new animal is looked at again while its id or material cannot be read yet.</summary>
        private const int PlacedTries = 120;

        /// <summary>True when the current save's looks file had problems: it is then never written over (the player can fix it).</summary>
        private static bool _looksUnreadable;

        /// <summary>The save the park was loaded from or last saved to (whose looks file is in use); null for a park never saved.</summary>
        private static string? _savePath;

        public static bool Disabled => _disabled;

        /// <summary>True when the current save's looks file can be written (a park that was saved, a file without problems).</summary>
        public static bool LooksWritable => Looks != null && LooksPath != null && !_looksUnreadable;

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
                Subscribe(Core, "OnLoadingProcessComplete", nameof(OnLoadingProcessComplete), null); // Game's event forwards to GameCore's
                Subscribe(AccessTools.TypeByName("PrehistoricKingdom.SaveLoadManager"), "OnSaveCompleted", nameof(OnSaveCompleted), null);
                Subscribe(AccessTools.TypeByName("PrehistoricKingdom.SaveLoadManager"), "OnSaveRemoved", nameof(OnSaveRemoved), null);
                Subscribe(AccessTools.TypeByName("PrehistoricKingdom.AnimalManager"), "OnAnimalRemoved", nameof(OnAnimalRemoved), null);
                SubscribePlaced();
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
                    // GameCore clears its listeners after each invocation and on shutdown: listen again for every park.
                    if (gui != null)
                    {
                        Subscribe(Core, "OnLoadingProcessComplete", nameof(OnLoadingProcessComplete), null);
                        SubscribePlaced(); // AnimalManager.StaticInit clears its events: listen again for every park
                    }
                }
                if (gui == null)
                {
                    SavesDeleted(); // the load menu deletes saves too
                    return;
                }
                bool loaded;
                List<ulong> removed;
                List<string> saved;
                List<(object Animal, int Tries)> placed;
                lock (Gate)
                {
                    loaded = _loadedNews;
                    _loadedNews = false;
                    removed = Removed.ToList();
                    Removed.Clear();
                    saved = Saved.ToList();
                    Saved.Clear();
                    placed = PlacedNew.ToList();
                    PlacedNew.Clear();
                }
                // The game's event (once per park), or should it be missed the game saying it is fully loaded.
                if (!_parkRead && (loaded || Property(Core, "IsFullyLoaded") is true)) ParkLoaded();
                if (removed.Count > 0 && _parkRead) AnimalsRemoved(removed); // removals queued while loading belong to the old park
                if (_parkRead) foreach (var path in saved) ParkSaved(path);
                if (_parkRead) foreach (var (animal, tries) in placed) AnimalPlaced(animal, tries);
                SavesDeleted(); // after the saves: a save converted to a new one copies its images before the old one goes
                LookEditorWindow.Tick(gui, _parkRead);
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>The name of the save in use (its looks file); null outside a park and for a park never saved.</summary>
        public static string? SaveName => AnimalLooks.SaveNameOf(_savePath);

        /// <summary>The current save's looks file, or null when the park was never saved.</summary>
        public static string? LooksPath => AnimalLooks.PathFor(FrameworkMod.TyrantDirectory, SaveName);

        /// <summary>
        /// The save the park was loaded from: PersistentSceneLoader.AssociatedSaveMetadata.Path (set by LoadGameDelayed), or null for
        /// a new game (a new scenario may carry an older save's metadata, which is not this park's).
        /// </summary>
        private static string? LoadedSavePath()
        {
            if (SceneLoader == null) return null;
            if (AccessTools.Field(SceneLoader, "IsNewGame")?.GetValue(null) is true) return null;
            var metadata = Property(SceneLoader, "AssociatedSaveMetadata");
            return metadata == null ? null : AccessTools.Field(metadata.GetType(), "Path")?.GetValue(metadata) as string;
        }

        /// <summary>Game.AnimalPreviewComponent: the Nursery's preview (its currentAnimalPreview); null between parks.</summary>
        public static object? PreviewComponent()
        {
            if (Game != null && Getter(Game, "AnimalPreviewComponent") == null && Warned.Add("AnimalPreviewComponent"))
                FrameworkMod.Log.Warning("Game.AnimalPreviewComponent was not found (game updated?); the look editor cannot open on the Nursery's animal.");
            return Property(Game, "AnimalPreviewComponent");
        }

        public static bool IsAnimal(object? value) =>
            value != null && AnimalType != null && AnimalType.IsInstanceOfType(value) && !(value is UnityEngine.Object unity && unity == null);

        /// <summary>
        /// Writes the store after the editor saved one animal's look; false (and a log line) when it could not be. Looks of animals
        /// removed since the park was loaded or saved are still in it: the park save on disk still has those animals.
        /// </summary>
        public static bool WriteLooks()
        {
            var path = LooksPath;
            if (Looks == null || path == null) return false;
            if (_looksUnreadable)
            {
                FrameworkMod.Log.Warning($"\"{path}\" has problems (see above); fix or delete it before saving looks for single animals.");
                return false;
            }
            Looks.WriteFor(FrameworkMod.TyrantDirectory, SaveName!);
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
            Gone.Clear();
            lock (Gate) Removed.Clear(); // ids of the park that was left
            _savePath = LoadedSavePath();
            var path = LooksPath;
            var problems = new List<string>();
            Looks = path == null ? new AnimalLooks() : AnimalLooks.Read(path, problems);
            foreach (var problem in problems) FrameworkMod.Log.Warning(problem);
            _looksUnreadable = problems.Count > 0;
            if (Prune(out var alive) > 0) Write(SaveName, alive); // only the loaded save's file, against its own animals
            MaterialsModule.SetAnimalLooks(Looks);
            if (Looks.Count > 0)
            {
                FrameworkMod.Log.Msg($"{Looks.Count} single-animal look(s) for save \"{SaveName}\".");
                MaterialsModule.ReapplyIds(Looks.Ids.ToList(), keywords: true); // the animals were set up before the looks were read
            }
        }

        /// <summary>
        /// The game saved the park to <paramref name="savePath"/>, which is now the save in use: the looks of animals removed
        /// since, and of animals no longer in it, go, and the store is written to that save's own file (images of the save it came
        /// from copied over). Other saves' files are never touched. A failure only logs: the park is saved, the editor stays on.
        /// </summary>
        private static void ParkSaved(string savePath)
        {
            if (Looks == null) return;
            var before = LooksPath;
            _savePath = savePath;
            var dropped = 0;
            foreach (var id in Gone)
                if (Looks.Get(id) != null)
                {
                    Looks.Set(id, null);
                    dropped++;
                }
            Gone.Clear();
            if (dropped > 0) FrameworkMod.Log.Msg($"{dropped} look(s) of animals removed from the park were dropped.");
            Prune(out var alive);
            if (_looksUnreadable)
            {
                // The file with problems is kept for the player to fix; a new save gets what could be read.
                if (string.Equals(before, LooksPath, StringComparison.OrdinalIgnoreCase)) return;
                FrameworkMod.Log.Warning($"Save \"{SaveName}\" gets the looks that could be read from \"{before}\"; the ones with problems stay there.");
                _looksUnreadable = false;
            }
            Write(SaveName, alive);
            MaterialsModule.SetAnimalLooks(Looks); // the store may point at the new save's images
            LookEditorWindow.SaveChanged();
        }

        /// <summary>Drops looks whose animal is not in the park (never on an empty read); returns how many, and the animals read.</summary>
        private static int Prune(out List<ulong> alive)
        {
            alive = new List<ulong>();
            if (Looks == null) return 0;
            alive = MaterialsModule.LoadedIds();
            if (Looks.Count == 0 || alive.Count == 0) return 0; // no animals read (or none yet): nothing is dropped on a guess
            var pruned = Looks.Prune(alive);
            if (pruned > 0) FrameworkMod.Log.Msg($"{pruned} look(s) of animals no longer in the park were dropped.");
            return pruned;
        }

        /// <summary>The store as this save's file (never a file with problems); a failure logs once per file and changes nothing else.</summary>
        private static void Write(string? saveName, List<ulong> alive)
        {
            if (Looks == null || saveName == null || _looksUnreadable) return;
            try
            {
                Looks.WriteFor(FrameworkMod.TyrantDirectory, saveName, alive);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ManifestException)
            {
                if (WriteWarned.Add(saveName)) FrameworkMod.Log.Warning($"The single-animal looks of save \"{saveName}\" could not be written: {ex.Message}");
            }
        }

        /// <summary>Saves the game deleted: their looks file and images go (the save in use is then none until the next save).</summary>
        private static void SavesDeleted()
        {
            List<string> paths;
            lock (Gate)
            {
                if (SavesRemoved.Count == 0) return;
                paths = SavesRemoved.ToList();
                SavesRemoved.Clear();
            }
            foreach (var path in paths)
            {
                if (!(AnimalLooks.SaveNameOf(path) is string name)) continue;
                if (string.Equals(AnimalLooks.PathFor(FrameworkMod.TyrantDirectory, name), LooksPath, StringComparison.OrdinalIgnoreCase))
                {
                    _savePath = null; // the store stays in memory and goes to the next save's file
                    _looksUnreadable = false;
                    LookEditorWindow.SaveChanged();
                }
                try
                {
                    AnimalLooks.DeleteSave(FrameworkMod.TyrantDirectory, name);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    FrameworkMod.Log.Warning($"The single-animal looks of deleted save \"{name}\" could not be removed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Removed animals (sold or destroyed) keep their look in the store and its file until the park is saved: loading the
        /// earlier save brings them back with their look. Their look is never drawn again, as they are gone.
        /// </summary>
        private static void AnimalsRemoved(List<ulong> ids)
        {
            foreach (var id in ids) Gone.Add(id);
            LookEditorWindow.AnimalsRemoved(ids);
        }

        /// <summary>
        /// A new animal was placed (created in the Nursery or bought into a vivarium): the look kept for its species, skin and
        /// sex (<see cref="Pending"/>) becomes its own, in this save's looks file. An animal whose id or material cannot be read
        /// yet is looked at again for a few frames.
        /// </summary>
        private static void AnimalPlaced(object animal, int tries)
        {
            if (Pending.IsEmpty || Looks == null || !IsAnimal(animal)) return;
            var context = MaterialsModule.Describe(animal);
            if (context == null || !(context.PersistentId is ulong id))
            {
                if (tries < PlacedTries) lock (Gate) PlacedNew.Add((animal, tries + 1));
                return;
            }
            if (!LooksWritable)
            {
                if (Warned.Add("pending-unwritable"))
                    FrameworkMod.Log.Warning($"The look saved for the next {context.Species} is kept: this save's looks file cannot be written (save the park, or fix the file).");
                return;
            }
            if (!(Pending.Take(context.Species, context.SkinKey, context.Sex) is { } look))
            {
                // A mod skin may be put on the new animal a little after it is placed: look again for a few frames.
                if (tries < PlacedTries) lock (Gate) PlacedNew.Add((animal, tries + 1));
                return;
            }
            var before = Looks.Get(id);
            var map = before != null ? new Dictionary<string, MaterialOverride>(before, StringComparer.Ordinal) : new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
            foreach (var pair in look) map[pair.Key] = LookFiles.Relative(pair.Value, FrameworkMod.TyrantDirectory); // like the editor's own saves
            Looks.Set(id, map);
            bool written;
            try
            {
                written = WriteLooks(); // also hands the store to the materials
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ManifestException)
            {
                FrameworkMod.Log.Warning($"The look saved for the next {context.Species} could not be written: {ex.Message}");
                written = false;
            }
            if (!written)
            {
                Looks.Set(id, before);
                MaterialsModule.SetAnimalLooks(Looks);
                foreach (var pair in look) Pending.Set(context.Species, context.SkinKey, context.Sex, pair.Key, pair.Value); // kept for the next one
                return;
            }
            MaterialsModule.Reapply(animal, keywords: true);
            FrameworkMod.Log.Msg($"The look saved in the Nursery went to the new {context.Species} ({id}).");
            LookEditorWindow.PendingTaken(context);
        }

        private static void SubscribePlaced()
        {
            var manager = AccessTools.TypeByName("PrehistoricKingdom.AnimalManager");
            Subscribe(manager, "OnAnimalPlacedNew", nameof(OnAnimalPlacedNew), null);
            Subscribe(manager, "OnVivariumAnimalPlacedNew", nameof(OnAnimalPlacedNew), null);
        }

        private static void LeavePark()
        {
            if (_gui == null) return;
            LookEditorWindow.LeavePark();
            Pending.Clear(); // never saved: a look for the next animal belongs to this park visit
            Looks = null;
            _looksUnreadable = false;
            _savePath = null;
            _parkRead = false;
            Gone.Clear();
            lock (Gate)
            {
                Removed.Clear();
                Saved.Clear();
                PlacedNew.Clear();
                _loadedNews = false;
            }
            MaterialsModule.SetAnimalLooks(null);
        }

        // ---- the game's events (they only queue; Tick handles them on the main thread) ----

        private static void OnLoadingProcessComplete()
        {
            lock (Gate) _loadedNews = true;
        }

        private static void OnSaveCompleted(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath)) return;
            lock (Gate) Saved.Add(savePath);
        }

        private static void OnSaveRemoved(object metadata)
        {
            try
            {
                if (metadata != null && AccessTools.Field(metadata.GetType(), "Path")?.GetValue(metadata) is string path && path.Length > 0)
                    lock (Gate) SavesRemoved.Add(path);
            }
            catch (Exception)
            {
                // a save whose path cannot be read keeps its looks file (harmless: nothing reads it again)
            }
        }

        // AnimalManager.OnAnimalPlacedNew (Animal) and OnVivariumAnimalPlacedNew (VivariumAnimal), from AnimalPlacementPipeline.
        private static void OnAnimalPlacedNew(object animal)
        {
            if (animal == null || Pending.IsEmpty) return;
            lock (Gate) PlacedNew.Add((animal, 0));
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
                // an id that cannot be read keeps its look until the park is saved or loaded with the animal gone (then it is pruned)
            }
        }

        /// <summary>PersistentID.uniqueID of an animal with a valid one.</summary>
        public static ulong? PersistentId(object animal)
        {
            var type = animal.GetType();
            if (!(Getter(type, "HasValidPersistentID")?.GetValue(animal, null) is true)) return null;
            var id = Getter(type, "PersistentID")?.GetValue(animal, null);
            return id != null && AccessTools.Field(id.GetType(), "uniqueID")?.GetValue(id) is ulong unique ? unique : (ulong?)null;
        }

        /// <summary>Adds one of our static handlers to a static game event (a parameter of a reference type binds to object).</summary>
        private static void Subscribe(Type? type, string eventName, string handler, object? target)
        {
            var info = type?.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (info == null)
            {
                if (Warned.Add(eventName)) FrameworkMod.Log.Warning($"{type?.Name ?? "?"}.{eventName} was not found (game updated?); the look editor works without it.");
                return;
            }
            var method = AccessTools.Method(typeof(LookEditorModule), handler);
            var callback = Delegate.CreateDelegate(info.EventHandlerType, method);
            info.RemoveEventHandler(target, callback); // never twice
            info.AddEventHandler(target, callback);
        }

        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<(Type, string), PropertyInfo?> Getters = new Dictionary<(Type, string), PropertyInfo?>();

        /// <summary>A property looked up once per type (Tick reads some every frame).</summary>
        private static PropertyInfo? Getter(Type type, string name)
        {
            if (!Getters.TryGetValue((type, name), out var property)) Getters[(type, name)] = property = AccessTools.Property(type, name);
            return property;
        }

        /// <summary>A static property of the game; null while it cannot be read (e.g. its instance is between parks).</summary>
        private static object? Property(Type? type, string name)
        {
            try
            {
                return type == null ? null : Getter(type, name)?.GetValue(null, null);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }
    }
}
