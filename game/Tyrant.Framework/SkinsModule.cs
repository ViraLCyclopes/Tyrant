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
        private static bool _done;

        public static bool HasSkins => _mods.Count > 0;

        public static void Prepare(IEnumerable<LoadedMod> mods) => _mods = mods.Where(m => m.Manifest.Skins.Count > 0).ToList();

        /// <summary>Called every frame until the animal database exists; then adds the skins once.</summary>
        public static void Tick()
        {
            if (_done || _mods.Count == 0) return;
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

        private static object? AnimalDatabase()
        {
            try
            {
                var type = AccessTools.TypeByName("PrehistoricKingdom.PKPersistentData");
                if (type == null || AccessTools.Property(type, "Instance")?.GetValue(null, null) == null) return null;
                return AccessTools.Property(type, "Animals")?.GetValue(null, null);
            }
            catch (Exception)
            {
                return null; // not ready yet (its getter throws while the reference is unset)
            }
        }

        private static void Inject(object database)
        {
            var path = Path.Combine(MelonEnvironment.UserDataDirectory, "Tyrant", SkinNumbers.FileName);
            SkinNumbers numbers;
            var canSave = true;
            try
            {
                numbers = SkinNumbers.Parse(File.Exists(path) ? File.ReadAllText(path) : null);
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
            {
                FrameworkMod.Log.Error($"{SkinNumbers.FileName} could not be read ({ex.Message}); added skins are numbered for this session only and the file is left alone.");
                numbers = new SkinNumbers();
                canSave = false;
            }

            var get = AccessTools.Method(database.GetType(), "Get", new[] { typeof(string) });
            var messages = new List<string>();
            var changed = false;
            foreach (var group in _mods.SelectMany(m => m.Manifest.Skins.Select(s => (Mod: m, Skin: s))).GroupBy(x => x.Skin.Species, StringComparer.Ordinal))
            {
                var species = group.Key;
                var data = get?.Invoke(database, new object[] { species });
                if (data == null)
                {
                    foreach (var x in group) FrameworkMod.Log.Warning($"{x.Skin.Key(x.Mod.Manifest.Id)}: species \"{species}\" is not in the game; the skin is skipped.");
                    continue;
                }
                if (!(Traverse.Create(data).Field("skinsData").GetValue() is IList list) || list.Count == 0) continue;
                var vanilla = list.Count;
                changed |= numbers.Assign(species, vanilla, group.Select(x => x.Skin.Key(x.Mod.Manifest.Id)), messages);
                var byKey = group.ToDictionary(x => x.Skin.Key(x.Mod.Manifest.Id), StringComparer.Ordinal);
                var standIns = new HashSet<int>();
                var added = 0;
                foreach (var slot in SkinLayout.For(numbers.Of(species), vanilla))
                {
                    object? skin = null;
                    if (slot.Key != null && byKey.TryGetValue(slot.Key, out var x))
                    {
                        skin = MakeSkin(list, vanilla, x.Mod, x.Skin);
                        if (skin != null)
                        {
                            AddedSkins[skin] = new Added(x.Mod.Manifest.Id, x.Skin, x.Mod.Directory);
                            added++;
                        }
                    }
                    if (skin == null)
                    {
                        skin = Clone(list[0]);
                        Traverse.Create(skin).Field("skinName").SetValue("(skin from a removed mod)");
                        StandIns.Add(skin);
                        standIns.Add(slot.Number);
                    }
                    list.Add(skin); // its index is slot.Number: the layout is contiguous from the vanilla count
                }
                if (standIns.Count > 0) StandInIndices[data] = standIns;
                FrameworkMod.Log.Msg($"{species}: {added} skin(s) added{(standIns.Count > 0 ? $", {standIns.Count} kept as stand-ins for removed mods" : "")}.");
            }
            foreach (var message in messages) FrameworkMod.Log.Warning(message);
            if (changed && canSave)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, numbers.ToJson());
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    FrameworkMod.Log.Error($"{SkinNumbers.FileName} could not be saved ({ex.Message}); new skins may get other numbers next time.");
                }
            }
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
