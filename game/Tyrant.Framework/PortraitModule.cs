using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Skins' own portraits (mod.json "portraits", animals.db animal_portraits): a prefix on BaseAnimalData.GetPortrait, the
    /// game's one way to a portrait, returns Tyrant's sprite for a slot a skin sets and lets the game run for every other.
    /// </summary>
    internal static class PortraitModule
    {
        private static readonly PortraitTable Table = new PortraitTable();
        private static readonly Dictionary<string, Sprite?> Sprites = new Dictionary<string, Sprite?>(StringComparer.OrdinalIgnoreCase);
        private static bool _on;

        public static void Register(object skin, string modDirectory, IReadOnlyDictionary<string, string>? portraits, object? inheritFrom = null)
        {
            if (_on) Table.Register(skin, modDirectory, portraits, inheritFrom);
        }

        public static void Start(IReadOnlyList<LoadedMod> mods, HarmonyLib.Harmony harmony)
        {
            if (!mods.Any(m => m.Manifest.Skins.Any(s => s.Portraits is { Count: > 0 }) || m.Manifest.Species.Any(s => s.Portraits.Count > 0))) return;
            var data = AccessTools.TypeByName("PrehistoricKingdom.BaseAnimalData");
            var method = data == null ? null : AccessTools.Method(data, "GetPortrait");
            if (method == null || method.GetParameters().Length != 4 || !SameEnum("PrehistoricKingdom.AnimalSkinType", PortraitSlots.Morphs)
                || !SameEnum("PrehistoricKingdom.Sex", new[] { "Male", "Female" }) || !SameEnum("PrehistoricKingdom.AnimalGrowthStage", PortraitSlots.Stages))
            {
                FrameworkMod.Log.Warning("The game's portraits are not as Tyrant knows them (game updated?); skins show their base skin's portraits.");
                return;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PortraitModule), nameof(BeforeGetPortrait)));
            _on = true;
        }

        private static bool SameEnum(string name, string[] expected)
        {
            var type = AccessTools.TypeByName(name);
            return type != null && type.IsEnum && Enum.GetValues(type).Cast<object>().Select(v => Convert.ToInt32(v)).SequenceEqual(Enumerable.Range(0, expected.Length))
                   && Enum.GetNames(type).AsEnumerable().SequenceEqual(expected);
        }

        // GetPortrait(AnimalSkinData skinData, AnimalSkinType skinType, Sex sex, AnimalGrowthStage growthStage) : Sprite
        private static bool BeforeGetPortrait(object[] __args, ref Sprite __result)
        {
            try
            {
                if (__args[0] == null) return true;
                var slot = PortraitSlots.For(Convert.ToInt32(__args[3]), Convert.ToInt32(__args[2]) == 0, Convert.ToInt32(__args[1]));
                var path = slot == null ? null : Table.PathFor(__args[0], slot);
                if (path == null || !(SpriteFor(path) is Sprite sprite)) return true;
                __result = sprite;
                return false;
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("A skin portrait could not be shown (" + ex.GetBaseException().Message + "); the game's is shown.");
                return true;
            }
        }

        /// <summary>Made once per file, as the game makes its own; a file that cannot be read warns once and shows the game's.</summary>
        private static Sprite? SpriteFor(string path)
        {
            if (Sprites.TryGetValue(path, out var known)) return known;
            Sprite? sprite = null;
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileName(path) };
                if (File.Exists(path) && texture.LoadImage(File.ReadAllBytes(path), true))
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero, 16f, 0, SpriteMeshType.FullRect);
                else UnityEngine.Object.Destroy(texture);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            if (sprite == null) FrameworkMod.Log.Warning($"Portrait {path} could not be read; the game's portrait is shown instead.");
            Sprites[path] = sprite;
            return sprite;
        }
    }
}
