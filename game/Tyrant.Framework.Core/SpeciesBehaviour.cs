using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>A species' combat behaviour: the vanilla profile it starts from and the aggression rules it ends up with (full asset names).</summary>
    public sealed class SpeciesBehaviour
    {
        /// <summary>A combat profile asset, e.g. CombatProfile_PredatorApex.</summary>
        public string StartFrom { get; set; } = "";
        /// <summary>The aggression rule assets the species keeps, e.g. Aggression_Predation.</summary>
        public List<string> Rules { get; set; } = new List<string>();

        internal Dictionary<string, object?> ToMap() => new Dictionary<string, object?>
        {
            ["startFrom"] = StartFrom,
            ["rules"] = Rules.Select(r => (object?)r).ToList(),
        };
    }

    /// <summary>A game asset name with the plain name shown to people.</summary>
    public sealed record BehaviourName(string Asset, string Name);

    /// <summary>One rule in a species' final rule list; StartIndex is its position in the start profile's entries, or null when it is added.</summary>
    public readonly record struct RuleSlot(string Rule, int? StartIndex);

    /// <summary>The combat profiles and aggression rules a species can use, with their plain names, priority order and validation.</summary>
    public static class SpeciesBehaviours
    {
        public const string ProfilePath = "/brain/combat/shared/combatProfile";
        public const string RulesPath = ProfilePath + "/aggressionProfileEntries";

        const string ProfilePrefix = "CombatProfile_";
        const string RulePrefix = "Aggression_";

        public static readonly IReadOnlyList<BehaviourName> Profiles = new[]
        {
            new BehaviourName("CombatProfile_PredatorApexCannibalistic", "Apex predator, cannibal"),
            new BehaviourName("CombatProfile_PredatorApex", "Apex predator"),
            new BehaviourName("CombatProfile_PredatorActive", "Active predator"),
            new BehaviourName("CombatProfile_PredatorActiveRivalrous", "Active predator, rivalrous"),
            new BehaviourName("CombatProfile_PredatorPassive", "Passive predator"),
            new BehaviourName("CombatProfile_PredatorOpportunistic", "Opportunistic predator"),
            new BehaviourName("CombatProfile_PredatorRaider", "Raider"),
            new BehaviourName("CombatProfile_Bear", "Bear"),
            new BehaviourName("CombatProfile_TankTerritorial", "Territorial tank"),
            new BehaviourName("CombatProfile_TankDefensive", "Defensive tank"),
            new BehaviourName("CombatProfile_Elephant", "Elephant"),
            new BehaviourName("CombatProfile_HerdAnimalDefensive", "Herd animal, defensive"),
            new BehaviourName("CombatProfile_HerdAnimalPassive", "Herd animal, passive"),
            new BehaviourName("CombatProfile_GiantDefenderAssertive", "Giant defender, assertive"),
            new BehaviourName("CombatProfile_GiantDefenderAssertive_Sauropod", "Giant defender, assertive (sauropod)"),
            new BehaviourName("CombatProfile_GiantDefenderPassive", "Giant defender, passive"),
            new BehaviourName("CombatProfile_GiantDefenderPassive_Sauropod", "Giant defender, passive (sauropod)"),
            new BehaviourName("CombatProfile_PreyBrawler", "Prey, brawler"),
            new BehaviourName("CombatProfile_PreyBully", "Prey, bully"),
            new BehaviourName("CombatProfile_PreyScrappy", "Prey, scrappy"),
            new BehaviourName("CombatProfile_PreySkittish", "Prey, skittish"),
            new BehaviourName("CombatProfile_Deer", "Deer"),
        };

        /// <summary>In the game's priority order: a rule earlier in the list is checked first.</summary>
        public static readonly IReadOnlyList<BehaviourName> Rules = new[]
        {
            new BehaviourName("Aggression_Predation", "Hunts prey"),
            new BehaviourName("Aggression_PredationStarving", "Hunts when starving"),
            new BehaviourName("Aggression_CannibaliseSameSpecies", "Eats its own kind"),
            new BehaviourName("Aggression_CannibaliseBaby", "Eats babies of its own kind"),
            new BehaviourName("Aggression_NestDefenseAggressive", "Guards its nest fiercely"),
            new BehaviourName("Aggression_NestDefense", "Guards its nest"),
            new BehaviourName("Aggression_DefendWithFamily", "Defends its family"),
            new BehaviourName("Aggression_DefendWithGroup", "Defends its group"),
            new BehaviourName("Aggression_PreyDefendFromAttackingPredator", "Fights back against predators"),
            new BehaviourName("Aggression_PreyDefendFromNearbyPredator_Protector", "Confronts nearby predators to protect others"),
            new BehaviourName("Aggression_PreyDefendFromNearbyPredator_Selfish", "Confronts nearby predators"),
            new BehaviourName("Aggression_PreyFleeFromPredator", "Flees predators"),
            new BehaviourName("Aggression_PreyFleeWithFamily", "Flees with its family"),
            new BehaviourName("Aggression_FeedAggressionSocial", "Guards food from its group"),
            new BehaviourName("Aggression_FeedAggression", "Guards food"),
            new BehaviourName("Aggression_FightOtherSpecies", "Fights other species"),
            new BehaviourName("Aggression_FightOtherSpeciesCasual", "Scuffles with other species"),
            new BehaviourName("Aggression_FightOwnSpecies", "Fights its own kind"),
            new BehaviourName("Aggression_SparOwnSpecies", "Spars with its own kind"),
            new BehaviourName("Aggression_MaleBullyOthersDuringMatingSeason", "Males bully others in mating season"),
            new BehaviourName("Aggression_MaleSparDuringMatingSeason", "Males spar in mating season"),
            new BehaviourName("Aggression_SparWithSiblings", "Young spar with siblings"),
            new BehaviourName("Aggression_FemaleMateRejection", "Females fight off unwanted mates"),
        };

        /// <summary>True for the rule list field itself and any entry under it.</summary>
        public static bool IsRuleField(string path) =>
            path == RulesPath || path.StartsWith(RulesPath + "/", StringComparison.Ordinal);

        /// <summary>The record edits to apply: with a behaviour the species builds its own rule list, so its old rule rows are left out.</summary>
        public static IEnumerable<AnimalField> FieldsToApply(SpeciesEntry entry) =>
            entry.Behaviour == null ? entry.Fields : entry.Fields.Where(f => !IsRuleField(f.Path));

        /// <summary>The profile asset a name refers to (asset name, name without CombatProfile_, or plain name; any case), or null.</summary>
        public static string? ResolveProfile(string text) => Resolve(Profiles, ProfilePrefix, text);

        /// <summary>The rule asset a name refers to (asset name, name without Aggression_, or plain name; any case), or null.</summary>
        public static string? ResolveRule(string text) => Resolve(Rules, RulePrefix, text);

        static string? Resolve(IReadOnlyList<BehaviourName> names, string prefix, string text)
        {
            var wanted = text.Trim();
            foreach (var n in names)
                if (string.Equals(n.Asset, wanted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n.Asset.Substring(prefix.Length), wanted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n.Name, wanted, StringComparison.OrdinalIgnoreCase))
                    return n.Asset;
            return null;
        }

        public static void Validate(SpeciesBehaviour behaviour, string owner)
        {
            if (!Profiles.Any(p => p.Asset == behaviour.StartFrom))
                throw new ManifestException($"{owner}: unknown behaviour profile \"{behaviour.StartFrom}\".");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in behaviour.Rules)
            {
                if (!Rules.Any(r => r.Asset == rule))
                    throw new ManifestException($"{owner}: unknown behaviour rule \"{rule}\".");
                if (!seen.Add(rule))
                    throw new ManifestException($"{owner}: behaviour rule \"{rule}\" is listed twice.");
            }
        }

        /// <summary>
        /// The final rule list: the start profile's entries that are still wanted, in their own order (duplicates kept),
        /// then each added rule just before the first kept entry that comes later in the priority order, or last if none does.
        /// </summary>
        public static IReadOnlyList<RuleSlot> Order(IReadOnlyList<string> startEntries, IReadOnlyCollection<string> wanted)
        {
            var priority = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < Rules.Count; i++) priority[Rules[i].Asset] = i;
            int Rank(string rule) => priority.TryGetValue(rule, out var p) ? p : -1;

            var slots = new List<RuleSlot>();
            for (var i = 0; i < startEntries.Count; i++)
                if (wanted.Contains(startEntries[i])) slots.Add(new RuleSlot(startEntries[i], i));

            var inStart = new HashSet<string>(startEntries, StringComparer.Ordinal);
            foreach (var rule in wanted.Where(r => !inStart.Contains(r)).OrderBy(Rank))
            {
                var at = slots.FindIndex(s => Rank(s.Rule) > Rank(rule));
                slots.Insert(at < 0 ? slots.Count : at, new RuleSlot(rule, null));
            }
            return slots;
        }
    }
}
