using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Shows the Nursery skin row in the species' saved order (mod.json "skinOrder") by moving its swatches among their siblings.
    /// The game's skinToggles list keeps its order (index i is skin i: clicks and UpdateAnimalPreview rely on it); only the
    /// on-screen order changes, so whatever walks the row as the player sees it goes through <see cref="ScreenOrder"/>.
    /// </summary>
    internal static class PickerOrder
    {
        /// <summary>After the game built a fresh row (left to right: skin 0, 1, ...): moves each swatch to its saved place.</summary>
        public static void Apply(object menu, bool rebuild)
        {
            if (!rebuild) return; // the swatches keep the places they were given when the row was built
            var data = Traverse.Create(menu).Property("CurrentSelectedAnimalData").GetValue();
            if (!(Traverse.Create(menu).Field("skinToggles").GetValue() is IList row)) return;
            var order = SkinsModule.DisplayOrderOf(data);
            if (order == null || order.Length != row.Count) return;
            var toggles = row.Cast<object>().OfType<Component>().ToList();
            if (toggles.Count != row.Count || toggles.Any(t => t == null)) return;
            var siblings = toggles.Select(t => t.transform.GetSiblingIndex()).ToList();
            if (siblings.Max() - siblings.Min() + 1 != siblings.Count) return; // the swatches share their parent with other objects: leave the game's row
            foreach (var move in SkinPickerOrder.Moves(siblings, order))
                toggles[move.Skin].transform.SetSiblingIndex(move.Sibling);
        }

        /// <summary>Row indices (skin numbers) as the row shows them, left to right; 0..count-1 when the species has no saved order.</summary>
        public static IReadOnlyList<int> ScreenOrder(object menu, int count)
        {
            try
            {
                var order = SkinsModule.DisplayOrderOf(Traverse.Create(menu).Property("CurrentSelectedAnimalData").GetValue());
                if (order != null && order.Length == count) return order;
            }
            catch (Exception)
            {
                // the game's own order: the row still works, only not in the saved order
            }
            return Enumerable.Range(0, count).ToArray();
        }
    }
}
