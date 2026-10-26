using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Gives a new species its own copy of a vanilla combat profile, with its chosen rule list, on its own brain.
    /// Vanilla profiles, rule assets, brains and their combat rows are only read.
    /// </summary>
    internal static class SpeciesBehaviourBuilder
    {
        // An entry with no rule asset (inline-only) has no rule name; it is always kept where it is.
        private const string Inline = "";

        public static void Apply(ScriptableObject data, SpeciesBehaviour behaviour, IReadOnlyList<object> vanillaAnimals, string owner)
        {
            var profiles = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
            foreach (var animal in vanillaAnimals)
                if (Get(Get(Get(Get(animal, "brain"), "combat"), "shared"), "combatProfile") is ScriptableObject profile && profile != null && !profiles.ContainsKey(profile.name))
                    profiles.Add(profile.name, profile);
            if (!profiles.TryGetValue(behaviour.StartFrom, out var start))
                throw new InvalidOperationException($"{owner}: behaviour profile '{behaviour.StartFrom}' is not in the game");
            var entriesField = AccessTools.Field(start.GetType(), "aggressionProfileEntries")
                ?? throw new MissingFieldException(start.GetType().FullName, "aggressionProfileEntries");

            var startEntries = Entries(entriesField, start);
            var names = startEntries.Select(RuleOf).ToList();
            var wanted = new HashSet<string>(behaviour.Rules, StringComparer.Ordinal);
            if (names.Contains(Inline)) wanted.Add(Inline);
            var slots = SpeciesBehaviours.Order(names, wanted);
            // Borrow added rules before anything is copied: a missing rule leaves nothing behind.
            var borrowed = slots.Select(s => s.StartIndex == null ? Borrow(s.Rule, profiles, entriesField, owner) : null).ToList();

            var copy = UnityEngine.Object.Instantiate(start);
            var own = Entries(entriesField, copy);
            if (own.Count != startEntries.Count)
            {
                UnityEngine.Object.Destroy(copy);
                throw new InvalidOperationException($"{owner}: the copy of '{behaviour.StartFrom}' lost its rules");
            }
            var entries = Array.CreateInstance(entriesField.FieldType.GetElementType()!, slots.Count);
            for (var i = 0; i < slots.Count; i++) entries.SetValue(slots[i].StartIndex is int at ? own[at] : borrowed[i], i);
            // Set by hand: the copy must not depend on Odin carrying the array over.
            entriesField.SetValue(copy, entries);

            // The species' own brain, cloned the way AnimalFieldEditor clones on write: Instantiate the asset, shallow-clone the rows.
            if (!(Get(data, "brain") is ScriptableObject brain) || brain == null) throw new InvalidOperationException($"{owner}: the template has no brain");
            var ownBrain = UnityEngine.Object.Instantiate(brain);
            var combat = AnimalFieldEditor.ShallowClone(Get(ownBrain, "combat") ?? throw new MissingFieldException("AnimalBrain.combat"));
            var shared = AnimalFieldEditor.ShallowClone(Get(combat, "shared") ?? throw new MissingFieldException("AnimalBrainCombat.shared"));
            Put(shared, "combatProfile", copy);
            Put(combat, "shared", shared);
            Put(ownBrain, "combat", combat);
            Put(data, "brain", ownBrain);
            FrameworkMod.Log.Msg($"{owner}: own behaviour profile from {behaviour.StartFrom}, {slots.Count} rules.");
        }

        /// <summary>A clone of the entry of the first profile (in SpeciesBehaviours.Profiles order) that has the rule.</summary>
        private static object Borrow(string rule, Dictionary<string, ScriptableObject> profiles, FieldInfo entriesField, string owner)
        {
            foreach (var name in SpeciesBehaviours.Profiles)
            {
                if (!profiles.TryGetValue(name.Asset, out var profile)) continue;
                var entry = Entries(entriesField, profile).FirstOrDefault(e => RuleOf(e) == rule);
                if (entry == null) continue;
                var clone = AnimalFieldEditor.ShallowClone(entry);
                Members.Set(clone, "cachedResolvedProfile", null); // resolved again from the clone's own fields
                return clone;
            }
            throw new InvalidOperationException($"{owner}: behaviour rule '{rule}' is not in any game profile");
        }

        private static List<object> Entries(FieldInfo entriesField, ScriptableObject profile) =>
            (entriesField.GetValue(profile) as Array)?.Cast<object>().ToList() ?? new List<object>();

        private static string RuleOf(object entry) => Get(entry, "asset") is UnityEngine.Object asset && asset != null ? asset.name : Inline;

        private static object? Get(object? target, string name) => target == null ? null : Members.Get(target, name);

        private static void Put(object target, string name, object value)
        {
            if (!Members.Set(target, name, value)) throw new MissingFieldException(target.GetType().FullName, name);
        }
    }
}
