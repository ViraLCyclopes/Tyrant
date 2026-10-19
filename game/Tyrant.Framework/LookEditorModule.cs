using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        private static readonly List<(object Animal, ulong VirtualId, int Tries)> PlacedNew = new List<(object, ulong, int)>(); // placed animals with a bound look, handled in Tick
        private static readonly List<object> Incubated = new List<object>(); // OnAnimalIncubationStart's virtual animals, handled in Tick
        private static readonly List<ulong> Mismatched = new List<ulong>(); // bindings an animal of another kind was placed under, dropped in Tick
        private static ConditionalWeakTable<object, StrongBox<ulong>> PlacedFrom = new ConditionalWeakTable<object, StrongBox<ulong>>(); // animal being placed -> its virtual animal's id
        private static readonly HashSet<string> ParkWarned = new HashSet<string>(StringComparer.Ordinal); // once-only warnings, again in the next park
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
        /// Looks armed in the Nursery for the next animal incubated ("Next created"): in memory only, dropped with the park; the
        /// incubation started to match binds its look to that animal in <see cref="Looks"/> (and this save's file).
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
                List<(object Animal, ulong VirtualId, int Tries)> placed;
                List<object> incubated;
                List<ulong> mismatched;
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
                    incubated = Incubated.ToList();
                    Incubated.Clear();
                    mismatched = Mismatched.ToList();
                    Mismatched.Clear();
                }
                // The game's event (once per park), or should it be missed the game saying it is fully loaded.
                if (!_parkRead && (loaded || Property(Core, "IsFullyLoaded") is true)) ParkLoaded();
                if (removed.Count > 0 && _parkRead) AnimalsRemoved(removed); // removals queued while loading belong to the old park
                // Placements and incubations first: a save right after must keep (or prune) what they changed.
                if (_parkRead) foreach (var virtualId in mismatched) BindingMismatched(virtualId);
                if (_parkRead) foreach (var (animal, virtualId, tries) in placed) AnimalPlaced(animal, virtualId, tries);
                if (_parkRead) foreach (var virtualAnimal in incubated) IncubationStarted(virtualAnimal);
                if (_parkRead) foreach (var path in saved) ParkSaved(path);
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
            // Only the loaded save's file, against its own animals. Bindings of virtual animals this save does not have (bound
            // after its last save, then left unsaved) go too: the game hands their ids out again (it counts on from the highest
            // one loaded), and the held animals are already back by now (Game.Load).
            var pruned = Prune(out var alive);
            if (PruneIncubating() + pruned > 0) Write(SaveName, alive);
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
            PruneIncubating();
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

        /// <summary>
        /// Drops the looks bound to virtual animals the park no longer has (an incubation cancelled, a held animal sold), never
        /// when the virtual animals cannot be read. One whose animal is being placed (queued) is kept. A binding stays after its
        /// animal was placed until this runs on the next park save (the placement may be left unsaved); returns how many went.
        /// </summary>
        private static int PruneIncubating()
        {
            if (Looks == null || Looks.IncubatingCount == 0 || !(VirtualIds() is { } ids)) return 0;
            lock (Gate) ids.AddRange(PlacedNew.Select(p => p.VirtualId));
            var pruned = Looks.PruneIncubating(ids);
            if (pruned > 0) FrameworkMod.Log.Msg($"{pruned} Nursery look(s) of animals no longer incubating or held were dropped.");
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
        /// An incubation was started from the Nursery (NurseryMenuV2.IncubateAnimal): a look armed for its species, skin and sex
        /// (<see cref="Pending"/>) is bound to that very animal, by its VirtualAnimal.UniqueID and with the species, skin and sex
        /// it was made for, in this save's looks file ("incubating"), and disarmed. It goes on the animal when that animal is
        /// placed (<see cref="AnimalPlaced"/>).
        /// </summary>
        private static void IncubationStarted(object virtualAnimal)
        {
            if (Pending.IsEmpty || Looks == null) return;
            if (!(MaterialsModule.DescribeVirtual(virtualAnimal) is { } va) || va.Id == 0 || !Pending.Has(va.Species, va.SkinKey, va.Sex)) return;
            if (!LooksWritable)
            {
                if (ParkWarned.Add("pending-unwritable"))
                    FrameworkMod.Log.Warning($"The look saved for the next {va.Species} stays armed: this save's looks file cannot be written (see above).");
                return;
            }
            var look = Pending.Take(va.Species, va.SkinKey, va.Sex)!;
            var materials = look.ToDictionary(p => p.Key, p => LookFiles.Relative(p.Value, FrameworkMod.TyrantDirectory), StringComparer.Ordinal);
            Looks.SetIncubating(va.Id, new IncubatingLook(va.Species, va.SkinKey, va.Sex, materials));
            if (TryWrite($"The look for the new {va.Species}") != Written.Yes)
            {
                Looks.SetIncubating(va.Id, null);
                foreach (var pair in look) Pending.Set(va.Species, va.SkinKey, va.Sex, pair.Key, pair.Value); // armed again
                return;
            }
            FrameworkMod.Log.Msg($"The look saved in the Nursery is bound to the {va.Species} now incubating ({va.Name}, virtual animal {va.Id}).");
            LookEditorWindow.PendingTaken(va.Species, va.SkinKey, va.Sex, va.Name);
        }

        /// <summary>
        /// A new animal was placed from the virtual animal <paramref name="virtualId"/>, which matched its binding (checked in
        /// the placement itself, see <see cref="BeforeFinalizePlacement"/>): the bound look becomes the placed animal's own, in
        /// this save's looks file. No other animal ever takes it. The binding stays until the next park save drops it (its virtual
        /// animal is gone by then): loading the save again without saving brings the animal back to the holder with its look
        /// still bound. While the animal's id cannot be read yet it is looked at again for a few frames; while the looks file
        /// cannot be written (or a write failed on the disk), again later.
        /// </summary>
        private static void AnimalPlaced(object animal, ulong virtualId, int tries)
        {
            if (Looks == null || !IsAnimal(animal) || !(Looks.GetIncubating(virtualId) is { } bound)) return;
            if (!LooksWritable)
            {
                if (ParkWarned.Add("placed-unwritable"))
                    FrameworkMod.Log.Warning("A placed animal's Nursery look waits: this save's looks file cannot be written (see above).");
                lock (Gate) PlacedNew.Add((animal, virtualId, tries)); // the binding stays; tried again on the next frames
                return;
            }
            if (!(PersistentId(animal) is ulong id))
            {
                if (tries < PlacedTries) lock (Gate) PlacedNew.Add((animal, virtualId, tries + 1));
                return;
            }
            var before = Looks.Get(id);
            var map = before != null ? new Dictionary<string, MaterialOverride>(before, StringComparer.Ordinal) : new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
            foreach (var pair in bound.Materials) map[pair.Key] = pair.Value; // stored UserData/Tyrant-relative already
            Looks.Set(id, map);
            var written = TryWrite("A placed animal's Nursery look");
            if (written != Written.Yes)
            {
                Looks.Set(id, before);
                MaterialsModule.SetAnimalLooks(Looks);
                if (written == Written.Retry) lock (Gate) PlacedNew.Add((animal, virtualId, tries)); // the disk may let it later
                return;
            }
            MaterialsModule.Reapply(animal, keywords: true);
            FrameworkMod.Log.Msg($"The look bound in the Nursery went to the placed animal {id} (virtual animal {virtualId}).");
        }

        /// <summary>A virtual animal placed with an id that has a binding for another animal (the id was handed out again): the binding goes.</summary>
        private static void BindingMismatched(ulong virtualId)
        {
            if (Looks == null || Looks.GetIncubating(virtualId) == null) return;
            var bound = Looks.GetIncubating(virtualId)!;
            Looks.SetIncubating(virtualId, null);
            FrameworkMod.Log.Msg($"The Nursery look bound to virtual animal {virtualId} was for a {bound.Species}; an animal placed under that id now is another one, so the look was dropped.");
            if (LooksWritable) TryWrite("The dropped Nursery look");
        }

        private enum Written { Yes, Retry, No }

        /// <summary>
        /// <see cref="WriteLooks"/>, with a failure logged: Retry when the file cannot be written now (a disk or access error, or
        /// a file with problems), No when the store itself is refused (a look a read would refuse: writing again cannot help).
        /// </summary>
        private static Written TryWrite(string what)
        {
            try
            {
                return WriteLooks() ? Written.Yes : Written.Retry; // also hands the store to the materials
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                FrameworkMod.Log.Warning($"{what} could not be written: {ex.Message}");
                return Written.Retry;
            }
            catch (ManifestException ex)
            {
                FrameworkMod.Log.Warning($"{what} could not be written: {ex.Message}");
                return Written.No;
            }
        }

        /// <summary>
        /// Prefix on AnimalPlacementPipeline.TryFinalizePlacement(animalBase, baseAnimalData, virtualAnimal, ...): it resets the
        /// animal's VirtualAnimalID and destroys the virtual animal before OnAnimalPlacedNew fires, so the virtual animal is read
        /// here, and only while some look is bound. Its species, skin and sex must still be the binding's (an id the game handed
        /// out again belongs to another animal); a mismatch drops the binding instead.
        /// </summary>
        private static void BeforeFinalizePlacement(object[] __args)
        {
            try
            {
                if (Looks == null || Looks.IncubatingCount == 0 || __args.Length < 3 || __args[0] == null || __args[2] == null) return;
                if (!(MaterialsModule.DescribeVirtual(__args[2]) is { } va) || va.Id == 0 || !(Looks.GetIncubating(va.Id) is { } bound)) return;
                if (!bound.Matches(va.Species, va.SkinKey, va.Sex))
                {
                    lock (Gate) Mismatched.Add(va.Id);
                    return;
                }
                PlacedFrom.Remove(__args[0]);
                PlacedFrom.Add(__args[0], new StrongBox<ulong>(va.Id));
            }
            catch (Exception)
            {
                // a placement that cannot be read gives no Nursery look; the game goes on
            }
        }

        /// <summary>The virtual animals in the park (AnimalManager.VirtualAnimalManager.currentVirtualAnimals); null when they cannot be read.</summary>
        private static List<ulong>? VirtualIds()
        {
            try
            {
                var manager = Property(Game, "AnimalManager");
                var virtuals = manager == null ? null : Getter(manager.GetType(), "VirtualAnimalManager")?.GetValue(manager, null);
                return virtuals == null || !(AccessTools.Field(virtuals.GetType(), "currentVirtualAnimals")?.GetValue(virtuals) is IDictionary map)
                    ? null
                    : map.Keys.OfType<ulong>().ToList();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The placement prefix (<see cref="BeforeFinalizePlacement"/>); without it, bound looks are kept until pruned.</summary>
        public static void Patch(HarmonyLib.Harmony harmony)
        {
            try
            {
                var pipeline = AccessTools.TypeByName("PrehistoricKingdom.AnimalPlacementPipeline");
                var finalize = pipeline == null ? null : AccessTools.Method(pipeline, "TryFinalizePlacement");
                if (finalize == null)
                {
                    FrameworkMod.Log.Warning("AnimalPlacementPipeline.TryFinalizePlacement was not found (game updated?); a look bound to an incubating animal never reaches it.");
                    return;
                }
                harmony.Patch(finalize, prefix: new HarmonyMethod(typeof(LookEditorModule), nameof(BeforeFinalizePlacement)));
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"The placement hook for Nursery looks could not be set: {ex.GetBaseException().Message}");
            }
        }

        private static void SubscribePlaced()
        {
            var manager = AccessTools.TypeByName("PrehistoricKingdom.AnimalManager");
            Subscribe(manager, "OnAnimalPlacedNew", nameof(OnAnimalPlacedNew), null);
            Subscribe(manager, "OnVivariumAnimalPlacedNew", nameof(OnAnimalPlacedNew), null);
            Subscribe(manager, "OnAnimalIncubationStart", nameof(OnAnimalIncubationStart), null);
        }

        private static void LeavePark()
        {
            if (_gui == null) return;
            LookEditorWindow.LeavePark();
            Pending.Clear(); // armed looks are never saved: they belong to this park visit (bound ones are in the looks file)
            Looks = null;
            _looksUnreadable = false;
            _savePath = null;
            _parkRead = false;
            Gone.Clear();
            ParkWarned.Clear();
            PlacedFrom = new ConditionalWeakTable<object, StrongBox<ulong>>();
            lock (Gate)
            {
                Removed.Clear();
                Saved.Clear();
                PlacedNew.Clear();
                Incubated.Clear();
                Mismatched.Clear();
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

        // AnimalManager.OnAnimalPlacedNew (Animal) and OnVivariumAnimalPlacedNew (VivariumAnimal), fired right after the placement
        // (PlaceAnimalAction.FinalizeAction, AnimalPlacementPipeline's spawns): only an animal placed from a virtual animal with a
        // bound look (read by BeforeFinalizePlacement) is queued.
        private static void OnAnimalPlacedNew(object animal)
        {
            if (animal == null || !PlacedFrom.TryGetValue(animal, out var from)) return;
            PlacedFrom.Remove(animal);
            lock (Gate) PlacedNew.Add((animal, from.Value, 0));
        }

        // AnimalManager.OnAnimalIncubationStart (VirtualAnimal), from NurseryMenuV2.IncubateAnimal after VirtualAnimal.Create.
        private static void OnAnimalIncubationStart(object virtualAnimal)
        {
            if (virtualAnimal == null || Pending.IsEmpty) return;
            lock (Gate) Incubated.Add(virtualAnimal);
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
