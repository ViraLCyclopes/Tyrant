using System;

namespace Tyrant.Framework.Core
{
    /// <summary>Where the in-game editor was opened: a placed animal's info panel, or the Nursery's preview animal.</summary>
    public enum EditorEntry { Park, Nursery }

    /// <summary>
    /// Which scopes the in-game editor offers for the animal it was opened on, the one it starts on, and why one is not
    /// offered (said in plain words on the status line).
    /// </summary>
    public static class EditorScopes
    {
        private static readonly LookScope[] Order = { LookScope.Animal, LookScope.Skin, LookScope.Species };

        /// <summary>
        /// True when this scope can be edited and saved: "This animal" needs a placed animal with an id in a saved park (the
        /// Nursery's preview is never one, even with an id), "Whole skin" a known skin; "Whole species" always.
        /// </summary>
        public static bool Offers(EditorEntry entry, LookScope scope, AnimalContext? context, string? saveName) =>
            context != null && WhyNot(entry, scope, context, saveName) == null;

        /// <summary>The scope an entry starts on when the animal offers it: this animal in the park, the whole skin in the Nursery.</summary>
        public static LookScope Preferred(EditorEntry entry) => entry == EditorEntry.Nursery ? LookScope.Skin : LookScope.Animal;

        /// <summary>The <see cref="Preferred"/> scope, or the next wider one offered when it is not (the whole species at last).</summary>
        public static LookScope Default(EditorEntry entry, AnimalContext? context, string? saveName)
        {
            for (var i = Array.IndexOf(Order, Preferred(entry)); i < Order.Length; i++)
                if (Offers(entry, Order[i], context, saveName)) return Order[i];
            return LookScope.Species;
        }

        /// <summary>Why the scope is not offered, for the player; null when it is.</summary>
        public static string? WhyNot(EditorEntry entry, LookScope scope, AnimalContext? context, string? saveName)
        {
            if (context == null) return "No animal is being edited.";
            switch (scope)
            {
                case LookScope.Animal:
                    if (entry == EditorEntry.Nursery) return "Place the animal in the park to give it its own look.";
                    if (context.PersistentId == null) return "This animal has no id, so it cannot have its own look.";
                    return VanillaLooks.IsAvailable(LookScope.Animal, context.PersistentId, saveName)
                        ? null
                        : "Save the park once to give this animal its own look."; // its looks file is named after the save
                case LookScope.Skin:
                    return context.SkinKey != null ? null : "This animal's skin is not known, so only its whole species can be edited.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The Nursery closed under a window opened on it: a draft with unsaved changes is kept on its look (Save still writes
        /// its skin or species look), a clean one goes with the animal. Returns the status line.
        /// </summary>
        public static string NurseryClosed(bool dirty, out bool keep)
        {
            keep = dirty;
            return dirty
                ? "The Nursery was closed; your changes are kept. Save or Reset them."
                : "The Nursery was closed; open the editor from an animal or the Nursery again.";
        }

        /// <summary>
        /// True when both are the same look to edit: species, skin and material. The id, the variant (a draft holds every
        /// variant's strength) and the skin's source do not matter: the Nursery makes a new preview animal for a new sex, and a
        /// new mutation keeps the skin.
        /// </summary>
        public static bool SameLook(AnimalContext? a, AnimalContext? b) =>
            a != null && b != null
            && string.Equals(a.Species, b.Species, StringComparison.Ordinal)
            && string.Equals(a.SkinKey, b.SkinKey, StringComparison.OrdinalIgnoreCase)
            && (a.ModSkin == null) == (b.ModSkin == null)
            && string.Equals(a.MaterialName, b.MaterialName, StringComparison.Ordinal);
    }
}
