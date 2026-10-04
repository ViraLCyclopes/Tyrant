using System;
using System.Collections.Generic;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Texture replacements on any object (fences, buildings, scenery): points material texture properties whose texture
    /// has a replaced name at the mod's texture. Runs when a scene loads, then every two seconds over materials not seen
    /// before: objects arrive through Addressables and GPU instancing, so there is no single hook. Animals keep their own
    /// exact hook (AnimalTexturePatch). The game's textures are never changed, only which texture a material uses.
    /// </summary>
    internal static class MaterialTextureModule
    {
        private const float Interval = 2f;
        private static MaterialSwap? _swap;
        private static float _next;
        private static readonly HashSet<string> Announced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Start()
        {
            var table = FrameworkMod.Replacements;
            _swap = new MaterialSwap(name => table.TryGet(name, out _), table.Count > 0);
        }

        public static void OnSceneLoaded() => Scan();

        public static void Tick()
        {
            if (_swap is null || !_swap.HasWork || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            Scan();
        }

        private static void Scan()
        {
            if (_swap is null || !_swap.HasWork) return;
            try
            {
                foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (material == null) continue;
                    var names = material.GetTexturePropertyNames();
                    var slots = new List<(string, string?)>(names.Length);
                    foreach (var name in names)
                    {
                        var texture = material.GetTexture(name);
                        slots.Add((name, texture == null ? null : texture.name));
                    }
                    foreach (var (property, textureName) in _swap.Plan(material.GetInstanceID(), slots))
                    {
                        if (!FrameworkMod.Replacements.TryGet(textureName, out var replacement)) continue;
                        var replaced = TextureCache.Get(replacement, MaterialSwap.KindOf(property));
                        if (replaced == null) continue;
                        material.SetTexture(property, replaced);
                        if (Announced.Add(textureName)) FrameworkMod.Log.Msg($"Replaced {textureName} on game materials ({replacement.ModId}).");
                    }
                }
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Texture replacements on objects could not be applied this time: " + ex.Message);
            }
        }
    }
}
