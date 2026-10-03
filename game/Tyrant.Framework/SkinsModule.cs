using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MelonLoader.Utils;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object x, object y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>
    /// Adds the mods' skins to their species once the game's animal database exists: each is a copy of its base skin appended
    /// at its permanent number (skin-slots.json); numbers whose mod is gone get a hidden stand-in (a copy of skin 0).
    /// </summary>
    internal static class SkinsModule
    {
        private sealed class Added
        {
            public Added(string modId, SkinEntry entry, string directory)
            {
                ModId = modId;
                Entry = entry;
                Directory = directory;
            }

            public string ModId { get; }
            public SkinEntry Entry { get; }
            public string Directory { get; }
        }

        private static readonly MethodInfo CloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static readonly Dictionary<object, Added> AddedSkins = new Dictionary<object, Added>(ReferenceComparer.Instance);
        private static readonly HashSet<object> StandIns = new HashSet<object>(ReferenceComparer.Instance);
        private static readonly Dictionary<object, HashSet<int>> StandInIndices = new Dictionary<object, HashSet<int>>(ReferenceComparer.Instance);
        private static List<LoadedMod> _mods = new List<LoadedMod>();
        private static SkinNumbers _numbers = new SkinNumbers();
        private static bool _canSave = true;
        private static bool _done;
        private static Type? _persistentData;
        private static Func<object?>? _instance;
        private static Func<object?>? _animals;

        private static string NumbersPath => Path.Combine(MelonEnvironment.UserDataDirectory, "Tyrant", SkinNumbers.FileName);

        /// <summary>
        /// True when any mod adds skins or skin-slots.json numbers any: even with every skin mod gone, saved animals may wear
        /// those numbers and need their stand-ins and the clamp.
        /// </summary>
        public static bool HasSkins => _mods.Count > 0 || _numbers.HasAny;

        public static void Prepare(IEnumerable<LoadedMod> mods)
        {
            _mods = mods.Where(m => m.Manifest.Skins.Count > 0).ToList();
            var path = NumbersPath;
            try
            {
                _numbers = SkinNumbers.Parse(File.Exists(path) ? File.ReadAllText(path) : null);
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
            {
                FrameworkMod.Log.Error($"{SkinNumbers.FileName} could not be read ({ex.Message}); added skins are numbered for this session only and the file is left alone.");
                _numbers = new SkinNumbers();
                _canSave = false;
            }
        }

        /// <summary>Called every frame until the animal database exists; then adds the skins once.</summary>
        public static void Tick()
        {
            if (_done || !HasSkins) return;
            var database = AnimalDatabase();
            if (database == null) return;
            _done = true;
            try
            {
                Inject(database);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Error("Added skins could not be set up; only vanilla skins are available: " + ex);
            }
        }

        public static bool IsAdded(object? skin) => skin != null && AddedSkins.ContainsKey(skin);

        public static bool IsStandIn(object? skin) => skin != null && StandIns.Contains(skin);

        public static bool IsStandInIndex(object? speciesData, int index) =>
            speciesData != null && StandInIndices.TryGetValue(speciesData, out var indices) && indices.Contains(index);

        public static bool TryGet(object? skinData, out string modId, out SkinEntry entry, out string directory)
        {
            if (skinData != null && AddedSkins.TryGetValue(skinData, out var added))
            {
                (modId, entry, directory) = (added.ModId, added.Entry, added.Directory);
                return true;
            }
            (modId, entry, directory) = ("", null!, "");
            return false;
        }

        /// <summary>PKPersistentData.Animals once PKPersistentData.Instance exists; members are looked up once, not every frame.</summary>
        private static object? AnimalDatabase()
        {
            if (_persistentData == null)
            {
                _persistentData = AccessTools.TypeByName("PrehistoricKingdom.PKPersistentData");
                _instance = _persistentData == null ? null : StaticGetter(_persistentData, "Instance");
                _animals = _persistentData == null ? null : StaticGetter(_persistentData, "Animals");
                if (_instance == null || _animals == null)
                {
                    FrameworkMod.Log.Error("PKPersistentData.Instance/Animals was not found (game updated?); added skins are off.");
                    _done = true;
                    return null;
                }
            }
            try
            {
                return _instance!() == null ? null : _animals!();
            }
            catch (Exception)
            {
                return null; // not ready yet (its getter throws while the reference is unset)
            }
        }

        private static Func<object?>? StaticGetter(Type type, string name)
        {
            var property = AccessTools.Property(type, name);
            if (property != null) return () => property.GetValue(null, null);
            var field = AccessTools.Field(type, name);
            return field == null ? (Func<object?>?)null : () => field.GetValue(null);
        }

        private static void Inject(object database)
        {
            var numbers = _numbers;
            var get = AccessTools.Method(database.GetType(), "Get", new[] { typeof(string) });
            var messages = new List<string>();
            var changed = false;
            var installed = _mods.SelectMany(m => m.Manifest.Skins.Select(s => (Mod: m, Skin: s)))
                .GroupBy(x => x.Skin.Species, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            // Species with numbers but no installed skins are set up too: their saved animals need the stand-ins.
            foreach (var species in numbers.SpeciesToSetUp(installed.Keys))
            {
                var group = installed.TryGetValue(species, out var skins) ? skins : new List<(LoadedMod Mod, SkinEntry Skin)>();
                try
                {
                    changed |= SetUp(species, group, get, database, numbers, messages);
                }
                catch (Exception ex)
                {
                    FrameworkMod.Log.Error($"{species}: added skins could not be set up ({ex.GetBaseException().Message}); it keeps the skins added so far.");
                }
            }
            foreach (var message in messages) FrameworkMod.Log.Warning(message);
            if (changed && _canSave)
            {
                try
                {
                    var path = NumbersPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, numbers.ToJson());
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    FrameworkMod.Log.Error($"{SkinNumbers.FileName} could not be saved ({ex.Message}); new skins may get other numbers next time.");
                }
            }
        }

        private static bool SetUp(string species, List<(LoadedMod Mod, SkinEntry Skin)> group, MethodInfo? get, object database, SkinNumbers numbers, List<string> messages)
        {
            object? data;
            try
            {
                data = get?.Invoke(database, new object[] { species });
            }
            catch (Exception)
            {
                data = null; // some databases throw on unknown ids instead of returning null
            }
            if (data == null)
            {
                foreach (var x in group) FrameworkMod.Log.Warning($"{x.Skin.Key(x.Mod.Manifest.Id)}: species \"{species}\" is not in the game; the skin is skipped.");
                return false;
            }
            if (!(Traverse.Create(data).Field("skinsData").GetValue() is IList list) || list.Count == 0) return false;
            var vanilla = list.Count;
            var changed = numbers.Assign(species, vanilla, group.Select(x => x.Skin.Key(x.Mod.Manifest.Id)), messages);
            var byKey = new Dictionary<string, (LoadedMod Mod, SkinEntry Skin)>(StringComparer.Ordinal);
            foreach (var x in group) byKey[x.Skin.Key(x.Mod.Manifest.Id)] = x;
            var standIns = new HashSet<int>();
            int added = 0, reserved = 0, removed = 0;
            foreach (var slot in SkinLayout.For(numbers.Of(species), vanilla))
            {
                object? skin = null;
                if (slot.Key != null && byKey.TryGetValue(slot.Key, out var x))
                {
                    try
                    {
                        skin = MakeSkin(list, vanilla, x.Mod, x.Skin);
                    }
                    catch (Exception ex)
                    {
                        FrameworkMod.Log.Warning($"{slot.Key}: the skin could not be made ({ex.GetBaseException().Message}); it is skipped.");
                        skin = null;
                    }
                    if (skin != null)
                    {
                        AddedSkins[skin] = new Added(x.Mod.Manifest.Id, x.Skin, x.Mod.Directory);
                        added++;
                    }
                }
                if (skin == null)
                {
                    skin = Clone(list[0]); // hidden in the Nursery; saved animals wearing this number look like skin 0
                    if (slot.Reserved) reserved++;
                    else
                    {
                        Traverse.Create(skin).Field("skinName").SetValue("(skin from a removed mod)");
                        removed++;
                    }
                    StandIns.Add(skin);
                    standIns.Add(slot.Number);
                }
                list.Add(skin); // its index is slot.Number: the layout is contiguous from the vanilla count
            }
            if (standIns.Count > 0) StandInIndices[data] = standIns;
            FrameworkMod.Log.Msg($"{species}: {added} skin(s) added{(removed > 0 ? $", {removed} kept as stand-ins for removed mods" : "")}{(reserved > 0 ? $", {reserved} reserved position(s) hidden" : "")}.");
            return changed;
        }

        private static object? MakeSkin(IList list, int vanilla, LoadedMod mod, SkinEntry entry)
        {
            var key = entry.Key(mod.Manifest.Id);
            object? baseSkin = null;
            if (int.TryParse(entry.Base, out var index)) baseSkin = index >= 0 && index < vanilla ? list[index] : null;
            else
                for (var i = 0; i < vanilla; i++)
                    if (string.Equals(Traverse.Create(list[i]).Field("skinName").GetValue() as string, entry.Base, StringComparison.OrdinalIgnoreCase))
                        baseSkin = list[i];
            if (baseSkin == null)
            {
                FrameworkMod.Log.Warning($"{key}: base skin \"{entry.Base}\" is not one of {entry.Species}'s skins; the skin is skipped.");
                return null;
            }
            var skin = Clone(baseSkin);
            Traverse.Create(skin).Field("skinName").SetValue(entry.Name);
            var thumbnail = Thumbnail(mod, entry);
            if (thumbnail != null) Traverse.Create(skin).Field("skinThumbnail").SetValue(thumbnail);
            return skin;
        }

        private static object Clone(object skin) => CloneMethod.Invoke(skin, null);

        /// <summary>The mod's thumbnail, or a 128 px centre crop of its first diffuse; null keeps the base skin's.</summary>
        private static Sprite? Thumbnail(LoadedMod mod, SkinEntry entry)
        {
            var file = entry.Thumbnail
                       ?? (entry.Male != null && entry.Male.TryGetValue("diffuse", out var male) ? male : null)
                       ?? (entry.Female != null && entry.Female.TryGetValue("diffuse", out var female) ? female : null);
            if (file == null) return null;
            try
            {
                var path = Path.Combine(mod.Directory, file);
                if (!ModPaths.IsInside(path, mod.Directory) || !File.Exists(path)) return null;
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!decoded.LoadImage(File.ReadAllBytes(path), false)) return null;
                    var pixels = decoded.GetPixels32();
                    var rgba = new byte[pixels.Length * 4];
                    for (var k = 0; k < pixels.Length; k++)
                    {
                        rgba[k * 4] = pixels[k].r;
                        rgba[k * 4 + 1] = pixels[k].g;
                        rgba[k * 4 + 2] = pixels[k].b;
                        rgba[k * 4 + 3] = pixels[k].a;
                    }
                    const int size = 128;
                    var thumb = Pixels.Thumbnail(rgba, decoded.width, decoded.height, size);
                    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = entry.Key(mod.Manifest.Id) + " thumbnail" };
                    texture.LoadRawTextureData(thumb);
                    texture.Apply(false, true);
                    return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                }
                finally
                {
                    UnityEngine.Object.Destroy(decoded);
                }
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"{entry.Key(mod.Manifest.Id)}: its thumbnail could not be made ({ex.Message}); the base skin's is shown.");
                return null;
            }
        }
    }
}
