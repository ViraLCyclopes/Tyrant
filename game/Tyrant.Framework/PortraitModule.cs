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
            if (!_on) return;
            try
            {
                var skipped = Table.Register(skin, modDirectory, portraits, inheritFrom);
                if (skipped.Count > 0)
                    FrameworkMod.Log.Warning($"A skin's portraits for {string.Join(", ", skipped)} are not usable paths inside its mod; the game's are shown for them.");
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("A skin's portraits could not be registered (" + ex.GetBaseException().Message + "); the game's are shown.");
            }
        }

        public static void Start(IReadOnlyList<LoadedMod> mods, HarmonyLib.Harmony harmony)
        {
            if (!mods.Any(m => m.Manifest.Skins.Any(s => s.Portraits is { Count: > 0 }) || m.Manifest.Species.Any(s => s.Portraits.Count > 0))) return;
            try
            {
                var data = AccessTools.TypeByName("PrehistoricKingdom.BaseAnimalData");
                var method = data == null ? null : AccessTools.Method(data, "GetPortrait");
                var skinType = Enumeration("PrehistoricKingdom.AnimalSkinType", PortraitSlots.Morphs);
                var sex = Enumeration("PrehistoricKingdom.Sex", new[] { "Male", "Female" });
                var stage = Enumeration("PrehistoricKingdom.AnimalGrowthStage", PortraitSlots.Stages);
                var parameters = method?.GetParameters();
                if (method == null || !method.IsStatic || parameters == null || parameters.Length != 4 || skinType == null || sex == null || stage == null
                    || parameters[0].ParameterType.FullName != "PrehistoricKingdom.AnimalSkinData" || parameters[1].ParameterType != skinType
                    || parameters[2].ParameterType != sex || parameters[3].ParameterType != stage || method.ReturnType != typeof(Sprite))
                {
                    FrameworkMod.Log.Warning(NotAsKnown);
                    return;
                }
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(PortraitModule), nameof(BeforeGetPortrait)));
                _on = true;
                var fields = AccessTools.TypeByName("PrehistoricKingdom.AnimalPortraits");
                if (fields == null || PortraitSlots.All.Any(name => fields.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) == null))
                    FrameworkMod.Log.Warning("The game's portrait fields changed (game updated?): Tyrant's app may not show the game's own portraits; skins' portraits still work.");
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning(NotAsKnown + " (" + ex.GetBaseException().Message + ")");
            }
        }

        private const string NotAsKnown = "The game's portraits are not as Tyrant knows them (game updated?); skins show their base skin's portraits.";

        /// <summary>The enum when it has exactly these names at 0, 1, 2...; otherwise null.</summary>
        private static Type? Enumeration(string name, string[] expected)
        {
            var type = AccessTools.TypeByName(name);
            return type != null && type.IsEnum && Enum.GetValues(type).Cast<object>().Select(v => Convert.ToInt32(v)).SequenceEqual(Enumerable.Range(0, expected.Length))
                   && Enum.GetNames(type).AsEnumerable().SequenceEqual(expected) ? type : null;
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
            Texture2D? texture = null;
            try
            {
                if (File.Exists(path))
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileName(path) };
                    if (texture.LoadImage(File.ReadAllBytes(path), true))
                    {
                        texture.wrapMode = TextureWrapMode.Clamp; // bilinear filtering must not bleed the opposite edge
                        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero, PortraitSlots.PixelsPerUnit(texture.width), 0, SpriteMeshType.FullRect);
                    }
                }
            }
            catch (Exception)
            {
                sprite = null;
            }
            if (sprite == null && texture != null) UnityEngine.Object.Destroy(texture);
            if (sprite == null) FrameworkMod.Log.Warning($"Portrait {path} could not be read; the game's portrait is shown instead.");
            Sprites[path] = sprite;
            return sprite;
        }
    }
}
