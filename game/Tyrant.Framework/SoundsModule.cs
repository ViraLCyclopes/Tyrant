using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;

namespace Tyrant.Framework
{
    /// <summary>
    /// Sound replacements in the game: every FMOD event the game creates passes RuntimeManager.CreateInstance(GUID); for one a
    /// mod replaces (for everyone, or for the species/skin of the animal playing it) the game's instance is muted and the mod's
    /// file plays in its place (SoundReplacer). Game and FMOD types are reached by reflection and FMOD's C functions.
    /// </summary>
    internal static class SoundsModule
    {
        private static SoundTable Table = SoundTable.Empty;
        private static SoundReplacer? Replacer;
        private static readonly Random Random = new Random();
        private static readonly Dictionary<IntPtr, string> PathByDescription = new Dictionary<IntPtr, string>();
        private static readonly HashSet<string> Failures = new HashSet<string>();
        private static FieldInfo? InstanceHandle;

        public static bool HasSounds => Table.Count > 0;

        public static void Start(IReadOnlyList<LoadedMod> mods, HarmonyLib.Harmony harmony)
        {
            var warnings = new List<string>();
            Table = SoundTable.Build(mods, warnings);
            foreach (var warning in warnings) FrameworkMod.Log.Warning(warning);
            if (Table.Count == 0) return;
            var manager = AccessTools.TypeByName("FMODUnity.RuntimeManager");
            var guid = AccessTools.TypeByName("FMOD.GUID");
            var create = manager == null || guid == null ? null : AccessTools.Method(manager, "CreateInstance", new[] { guid });
            if (create == null)
            {
                FrameworkMod.Log.Warning("Sound replacements are off: FMOD's RuntimeManager.CreateInstance(GUID) was not found (a game update?).");
                Table = SoundTable.Empty;
                return;
            }
            harmony.Patch(create, postfix: new HarmonyMethod(typeof(SoundsModule), nameof(AfterCreateInstance)));
            Replacer = new SoundReplacer(new FmodSoundEngine(), message => FrameworkMod.Log.Warning(message));
            var modCount = 0;
            foreach (var mod in mods) if (mod.Manifest.Sounds.Count > 0) modCount++;
            FrameworkMod.Log.Msg($"Replacing {Table.Count} sound(s) ({Table.UniqueCount} unique) from {modCount} mod(s).");
        }

        public static void Tick()
        {
            if (Replacer == null) return;
            try
            {
                Replacer.Tick();
            }
            catch (Exception ex)
            {
                Once("tick", "Sound replacements: " + ex.Message);
            }
        }

        /// <summary>Harmony postfix on RuntimeManager.CreateInstance(GUID); __result is the boxed FMOD.Studio.EventInstance.</summary>
        private static void AfterCreateInstance(object __result)
        {
            if (Replacer == null || __result == null) return;
            try
            {
                InstanceHandle ??= AccessTools.Field(__result.GetType(), "handle");
                var instance = (IntPtr)(InstanceHandle?.GetValue(__result) ?? IntPtr.Zero);
                if (instance == IntPtr.Zero) return;
                var path = PathOf(instance);
                if (path == null || !Table.Mentions(path)) return;

                string? species = null, skin = null;
                var maturity = 1f;
                if (Table.HasUnique(path) && AnimalPlaying(path) is { } animal)
                {
                    species = AnimalInfo.Species(animal);
                    skin = AnimalInfo.SkinKey(animal, species);
                    maturity = AnimalInfo.Maturity(animal);
                }
                var choice = Table.Choose(path, species, skin);
                if (choice == null) return;
                var pitch = species == null ? 1f : AgePitch.For(choice.AgePitch, maturity);
                var ui = path.StartsWith("event:/User Interface", StringComparison.OrdinalIgnoreCase);
                var music = path.StartsWith("event:/Music", StringComparison.OrdinalIgnoreCase);
                if (Replacer.Start(instance, path, choice, pitch, ui, music, Random) && Failures.Add("first:" + path))
                    FrameworkMod.Log.Msg($"Replaced {path}{(species == null ? "" : $" for {species}")} ({choice.ModId}).");
            }
            catch (Exception ex)
            {
                Once("hook", "Sound replacements: " + ex.Message); // never break the game's audio
            }
        }

        private static string? PathOf(IntPtr instance)
        {
            if (FmodNative.FMOD_Studio_EventInstance_GetDescription(instance, out var description) != FmodNative.Ok) return null;
            if (PathByDescription.TryGetValue(description, out var cached)) return cached;
            var path = FmodNative.ReadPath(description, FmodNative.FMOD_Studio_EventDescription_GetPath);
            if (path != null) PathByDescription[description] = path;
            return path;
        }

        // Game.AudioManager.AnimalAudioEvents (a VList<AnimalAudioEvent>): the entry being started for this path holds the animal.
        private static PropertyInfo? AudioManagerProperty;
        private static FieldInfo? EventsField, ItemsField, AnimalField, DataField, PlayingField, InstField, AudioEventField, InstHandleField;
        private static PropertyInfo? CountProperty;

        /// <summary>The animal whose queued sound is being started now: playing, no instance yet, the same event.</summary>
        private static object? AnimalPlaying(string path)
        {
            var game = AccessTools.TypeByName("PrehistoricKingdom.Game") ?? AccessTools.TypeByName("Game");
            AudioManagerProperty ??= game == null ? null : AccessTools.Property(game, "AudioManager");
            var manager = AudioManagerProperty?.GetValue(null, null);
            if (manager == null) return null;
            EventsField ??= AccessTools.Field(manager.GetType(), "AnimalAudioEvents");
            var list = EventsField?.GetValue(manager);
            if (list == null) return null;
            CountProperty ??= AccessTools.Property(list.GetType(), "Count");
            if (ItemsField == null)
                foreach (var field in AccessTools.GetDeclaredFields(list.GetType()))
                    if (field.FieldType.IsArray) { ItemsField = field; break; }
            if (CountProperty == null || ItemsField == null) return null;
            var count = (int)CountProperty.GetValue(list, null);
            if (!(ItemsField.GetValue(list) is Array items)) return null;
            for (var i = 0; i < count && i < items.Length; i++)
            {
                var entry = items.GetValue(i);
                if (entry == null) continue;
                var type = entry.GetType();
                PlayingField ??= AccessTools.Field(type, "isPlaying");
                InstField ??= AccessTools.Field(type, "eventInst");
                DataField ??= AccessTools.Field(type, "eventData");
                AnimalField ??= AccessTools.Field(type, "animal");
                if (PlayingField == null || InstField == null || DataField == null || AnimalField == null) return null;
                if (!(PlayingField.GetValue(entry) is bool playing) || !playing) continue;
                var inst = InstField.GetValue(entry);
                InstHandleField ??= inst == null ? null : AccessTools.Field(inst.GetType(), "handle");
                if (inst != null && InstHandleField != null && (IntPtr)InstHandleField.GetValue(inst) != IntPtr.Zero) continue;
                var data = DataField.GetValue(entry);
                AudioEventField ??= data == null ? null : AccessTools.Field(data.GetType(), "AudioEvent");
                if (data == null || !string.Equals(AudioEventField?.GetValue(data) as string, path, StringComparison.OrdinalIgnoreCase)) continue;
                return AnimalField.GetValue(entry);
            }
            return null;
        }

        private static void Once(string key, string message)
        {
            if (Failures.Add(key)) FrameworkMod.Log.Warning(message);
        }
    }

    /// <summary>An animal's species, skin key and age, read by reflection (as ModelModule does).</summary>
    internal static class AnimalInfo
    {
        public static string? Species(object animal)
        {
            var data = AccessTools.Property(animal.GetType(), "BaseData")?.GetValue(animal, null);
            return data == null ? null : Traverse.Create(data).Field("speciesID").GetValue() as string;
        }

        /// <summary>"&lt;mod&gt;/&lt;skin&gt;" for a Tyrant skin, else "&lt;species&gt;/&lt;skinName&gt;" for a vanilla one.</summary>
        public static string? SkinKey(object animal, string? species)
        {
            var skinData = AccessTools.Property(animal.GetType(), "SkinData")?.GetValue(animal, null);
            if (skinData == null) return null;
            if (SkinsModule.TryGet(skinData, out var modId, out var entry, out _)) return entry.Key(modId);
            var name = Traverse.Create(skinData).Field("skinName").GetValue() as string;
            return species == null || string.IsNullOrEmpty(name) ? null : species + "/" + name;
        }

        public static float Maturity(object animal) =>
            AccessTools.Property(animal.GetType(), "Maturity01")?.GetValue(animal, null) is float maturity ? maturity : 1f;
    }
}
