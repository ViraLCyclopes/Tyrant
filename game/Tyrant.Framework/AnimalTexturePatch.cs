using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Runs after the game puts an animal's skin textures on (AnimalVisuals.AssignTexturesAndSetMaterialVariation) and swaps
    /// every slot whose texture a mod replaces. It edits the property block the way the game's own edit scope does: read it
    /// from the first LOD renderer, write it to every LOD.
    /// </summary>
    internal static class AnimalTexturePatch
    {
        private const string TypeName = "PrehistoricKingdom.AnimalVisuals";
        private const string MethodName = "AssignTexturesAndSetMaterialVariation";

        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        private static readonly HashSet<string> Announced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Type, PropertyInfo?> LodsProperty = new Dictionary<Type, PropertyInfo?>();

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var type = AccessTools.TypeByName(TypeName);
            var method = type == null ? null : AccessTools.Method(type, MethodName);
            if (method == null)
            {
                FrameworkMod.Log.Error($"{TypeName}.{MethodName} was not found (game updated?); texture replacements and animal events are off.");
                return;
            }
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(AnimalTexturePatch), nameof(Postfix)));
        }

        // __args: the original arguments; the first is the animal (the extension method's 'this IAnimal').
        private static void Postfix(object[] __args)
        {
            try
            {
                var animal = __args.Length > 0 ? __args[0] : null;
                if (animal == null) return;
                if (FrameworkMod.Replacements.Count > 0) ReplaceTextures(animal);
                if (animal is Component component)
                    foreach (var mod in FrameworkMod.CodeMods)
                        FrameworkMod.Safe(mod, m => m.OnAnimalSpawned(component), nameof(TyrantMod.OnAnimalSpawned));
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Error("Texture replacement failed for one animal; it keeps its vanilla look: " + ex.Message);
            }
        }

        private static void ReplaceTextures(object animal)
        {
            var renderers = Lods(animal);
            if (renderers.Count == 0) return;
            renderers[0].GetPropertyBlock(Block);
            var changed = false;
            foreach (var slot in TextureSlots.All)
            {
                var current = Block.GetTexture(slot.Property);
                if (current == null || !FrameworkMod.Replacements.TryGet(current.name, out var replacement)) continue;
                var texture = TextureCache.Get(replacement, slot.Kind);
                if (texture == null || ReferenceEquals(texture, current)) continue;
                Block.SetTexture(slot.Property, texture);
                changed = true;
                if (Announced.Add(replacement.Texture + "|" + slot.Property))
                    FrameworkMod.Log.Msg($"replaced {replacement.Texture} ({slot.Property}) on {NameOf(animal)} ({replacement.ModId})");
            }
            if (changed)
                foreach (var renderer in renderers) renderer.SetPropertyBlock(Block);
        }

        /// <summary>IAnimal.Lods (VList&lt;SkinnedMeshRenderer&gt;), read by reflection so the repo needs no game assemblies.</summary>
        private static List<Renderer> Lods(object animal)
        {
            var type = animal.GetType();
            if (!LodsProperty.TryGetValue(type, out var property)) LodsProperty[type] = property = AccessTools.Property(type, "Lods");
            var renderers = new List<Renderer>();
            if (property?.GetValue(animal, null) is IEnumerable items)
                foreach (var item in items)
                    if (item is Renderer renderer && renderer != null) renderers.Add(renderer);
            return renderers;
        }

        private static string NameOf(object animal) => animal is Component c && c != null ? c.gameObject.name : animal.GetType().Name;
    }
}
