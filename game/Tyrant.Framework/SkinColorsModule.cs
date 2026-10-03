using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Gives each added skin with "colors" its own copies of the base skin's variation assets (AnimalSkinVariationAsset), with
    /// the mod's shift ranges and mutation gradients written into them; vanilla skins keep theirs.
    /// </summary>
    internal static partial class SkinColorsModule
    {
        private static readonly Dictionary<object, SkinColors> Copies = new Dictionary<object, SkinColors>(ReferenceComparer.Instance);

        public static void Apply(object skin, SkinEntry entry, string key)
        {
            var colors = entry.Colors;
            if (colors == null) return;
            var made = new Dictionary<object, ScriptableObject>(ReferenceComparer.Instance); // male and female can share one asset: copy it once
            var assign = new List<(Traverse Slot, ScriptableObject Copy)>();
            try
            {
                foreach (var field in new[] { "maleVariationData", "femaleVariationData" })
                {
                    var slot = Traverse.Create(skin).Field(field);
                    if (!(slot.GetValue() is ScriptableObject original) || original == null) continue;
                    if (!made.TryGetValue(original, out var copy))
                    {
                        copy = UnityEngine.Object.Instantiate(original);
                        copy.name = original.name + " (" + key + ")";
                        copy.hideFlags = HideFlags.DontUnloadUnusedAsset; // only this module holds it; Unity must not unload it
                        var asset = Traverse.Create(copy);
                        ApplyTint(asset.Field("defaultVariationData").GetValue(), colors.Tint);
                        ApplySet(asset.Field("albinoVariationData").GetValue(), colors.Albino);
                        ApplySet(asset.Field("melanisticVariationData").GetValue(), colors.Melanistic);
                        ApplySet(asset.Field("leucisticVariationData").GetValue(), colors.Leucistic);
                        made[original] = copy;
                    }
                    assign.Add((slot, copy));
                }
            }
            catch (Exception ex)
            {
                foreach (var copy in made.Values) UnityEngine.Object.Destroy(copy);
                FrameworkMod.Log.Warning($"{key}: its colours could not be set up ({ex.GetBaseException().Message}); it keeps the base skin's colours.");
                return;
            }
            foreach (var (slot, copy) in assign)
            {
                slot.SetValue(copy);
                Copies[copy] = colors;
            }
        }

        public static bool TryGetPattern(object? variationAsset, out SkinColorSet pattern)
        {
            if (variationAsset != null && Copies.TryGetValue(variationAsset, out var colors) && colors.Pattern != null)
            {
                pattern = colors.Pattern;
                return true;
            }
            pattern = null!;
            return false;
        }

        /// <summary>The eye and secondary edge softness the base skin's mutations use (the game's defaults when unreadable).</summary>
        public static (float Eye, float Secondary) Feathers(object variationAsset)
        {
            var data = Traverse.Create(variationAsset).Field("albinoVariationData").GetValue();
            var t = Traverse.Create(data);
            var eye = t.Field("eyeFeather").GetValue() is float e ? e : 0.25f;
            var secondary = t.Field("secondaryFeather").GetValue() is float s ? s : 1f;
            return (eye, secondary);
        }

        private static void ApplyTint(object? data, SkinTint? tint)
        {
            if (data == null || tint == null) return;
            var t = Traverse.Create(data);
            if (tint.Hue is FloatRange hue) t.Field("minMaxHue").SetValue(new Vector2(hue.Min, hue.Max));
            if (tint.Saturation is FloatRange saturation) t.Field("minMaxSat").SetValue(new Vector2(saturation.Min, saturation.Max));
            if (tint.Value is FloatRange value) t.Field("minMaxVal").SetValue(new Vector2(value.Min, value.Max));
        }

        private static void ApplySet(object? data, SkinColorSet? set)
        {
            if (data == null || set == null) return;
            var t = Traverse.Create(data);
            var a = set.A ?? set.B;
            var b = set.B ?? set.A;
            if (a != null) t.Field("patternAGradient").SetValue(Ramp(a));
            if (b != null) t.Field("patternBGradient").SetValue(Ramp(b));
            if (set.Secondary != null) t.Field("secondaryGradient").SetValue(Ramp(set.Secondary));
            if (set.Eye != null) t.Field("eyeGradient").SetValue(Ramp(set.Eye));
            if (set.Strength is FloatRange strength) t.Field("minMaxOverrideBase").SetValue(new Vector2(strength.Min, strength.Max));
            if (set.Softness is FloatRange softness) t.Field("minMaxPatternFeather").SetValue(new Vector2(softness.Min, softness.Max));
            ApplyTint(data, set.Tint);
        }

        private static Gradient Ramp(IReadOnlyList<Rgb> stops)
        {
            var colours = stops.Select((c, i) => new GradientColorKey(new Color(c.R, c.G, c.B), stops.Count == 1 ? 0f : i / (float)(stops.Count - 1))).ToList();
            if (colours.Count == 1) colours.Add(new GradientColorKey(colours[0].color, 1f));
            var gradient = new Gradient();
            gradient.SetKeys(colours.ToArray(), new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
