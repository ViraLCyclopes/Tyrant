using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SpeciesBehaviourTests
{
    static string[] Prefixed(params string[] names) => names.Select(n => "Aggression_" + n).ToArray();

    static readonly string[] TankTerritorial = Prefixed("NestDefenseAggressive", "PreyDefendFromAttackingPredator", "PreyDefendFromNearbyPredator_Protector",
        "DefendWithFamily", "DefendWithFamily", "FightOwnSpecies", "FightOtherSpecies", "MaleBullyOthersDuringMatingSeason",
        "MaleSparDuringMatingSeason", "PreyFleeWithFamily", "PreyFleeFromPredator", "FemaleMateRejection");

    static readonly string[] PreyScrappy = Prefixed("PreyFleeFromPredator", "PreyFleeWithFamily", "NestDefense", "DefendWithFamily",
        "FightOwnSpecies", "PreyDefendFromAttackingPredator", "MaleSparDuringMatingSeason", "SparWithSiblings", "FemaleMateRejection");

    [Fact]
    public void Order_puts_an_added_rule_before_the_first_later_rule()
    {
        var wanted = TankTerritorial.Append("Aggression_Predation").ToHashSet();

        var slots = SpeciesBehaviours.Order(TankTerritorial, wanted);

        Assert.Equal(13, slots.Count);
        Assert.Equal(new RuleSlot("Aggression_Predation", null), slots[0]);
        for (var i = 0; i < 12; i++) Assert.Equal(new RuleSlot(TankTerritorial[i], i), slots[i + 1]);
    }

    [Fact]
    public void Order_inserts_in_the_middle()
    {
        var wanted = PreyScrappy.Append("Aggression_FeedAggression").ToHashSet();

        var slots = SpeciesBehaviours.Order(PreyScrappy, wanted);

        Assert.Equal(10, slots.Count);
        Assert.Equal(new RuleSlot("Aggression_FeedAggression", null), slots[4]);
        Assert.Equal("Aggression_FightOwnSpecies", slots[5].Rule);
    }

    [Fact]
    public void Order_appends_when_nothing_later_exists()
    {
        var start = Prefixed("PreyFleeFromPredator");

        var slots = SpeciesBehaviours.Order(start, new HashSet<string>(start) { "Aggression_FemaleMateRejection" });

        Assert.Equal(new RuleSlot("Aggression_FemaleMateRejection", null), slots[^1]);
        Assert.Equal(2, slots.Count);
    }

    [Fact]
    public void Order_drops_unticked_rules_and_every_duplicate()
    {
        var wanted = TankTerritorial.Where(r => r != "Aggression_DefendWithFamily").ToHashSet();

        var slots = SpeciesBehaviours.Order(TankTerritorial, wanted);

        Assert.Equal(10, slots.Count);
        Assert.DoesNotContain(slots, s => s.Rule == "Aggression_DefendWithFamily");
    }

    [Fact]
    public void Validate_rejects_unknown_profile_unknown_rule_and_duplicates()
    {
        var badRule = new SpeciesBehaviour { StartFrom = "CombatProfile_PredatorApex", Rules = ["Aggression_Nope"] };
        var ex = Assert.Throws<ManifestException>(() => SpeciesBehaviours.Validate(badRule, "deino"));
        Assert.Contains("deino: unknown behaviour rule \"Aggression_Nope\"", ex.Message);

        var badProfile = new SpeciesBehaviour { StartFrom = "CombatProfile_Nope", Rules = [] };
        ex = Assert.Throws<ManifestException>(() => SpeciesBehaviours.Validate(badProfile, "deino"));
        Assert.Contains("deino", ex.Message);
        Assert.Contains("CombatProfile_Nope", ex.Message);

        var dup = new SpeciesBehaviour { StartFrom = "CombatProfile_PredatorApex", Rules = ["Aggression_Predation", "Aggression_Predation"] };
        ex = Assert.Throws<ManifestException>(() => SpeciesBehaviours.Validate(dup, "deino"));
        Assert.Contains("deino", ex.Message);
        Assert.Contains("Aggression_Predation", ex.Message);

        SpeciesBehaviours.Validate(new SpeciesBehaviour { StartFrom = "CombatProfile_PredatorApex", Rules = ["Aggression_Predation"] }, "deino");
    }

    [Fact]
    public void Resolve_accepts_asset_short_and_plain_names()
    {
        Assert.Equal("CombatProfile_PredatorApex", SpeciesBehaviours.ResolveProfile("predatorapex"));
        Assert.Equal("CombatProfile_PredatorApex", SpeciesBehaviours.ResolveProfile("Apex predator"));
        Assert.Equal("CombatProfile_PredatorApex", SpeciesBehaviours.ResolveProfile("CombatProfile_PredatorApex"));
        Assert.Equal("Aggression_Predation", SpeciesBehaviours.ResolveRule("Hunts prey"));
        Assert.Null(SpeciesBehaviours.ResolveRule("x"));
        Assert.Null(SpeciesBehaviours.ResolveProfile("x"));
    }

    [Fact]
    public void Lists_have_22_profiles_and_23_rules_without_duplicates()
    {
        Assert.Equal(22, SpeciesBehaviours.Profiles.Count);
        Assert.Equal(23, SpeciesBehaviours.Rules.Count);
        Assert.Equal(22, SpeciesBehaviours.Profiles.Select(p => p.Asset).Distinct().Count());
        Assert.Equal(22, SpeciesBehaviours.Profiles.Select(p => p.Name).Distinct().Count());
        Assert.Equal(23, SpeciesBehaviours.Rules.Select(r => r.Asset).Distinct().Count());
        Assert.Equal(23, SpeciesBehaviours.Rules.Select(r => r.Name).Distinct().Count());
    }

    [Fact]
    public void FieldsToApply_skips_rule_rows_only_when_a_behaviour_is_set()
    {
        var rule = new AnimalField { Path = SpeciesBehaviours.RulesPath + "/0/behavioralContribution", Kind = "number", Value = "2", TemplateValue = "1", Editable = true };
        var diet = new AnimalField { Path = "/diet", Kind = "text", Value = "Carnivore", TemplateValue = "Herbivore", Editable = true };
        var entry = new SpeciesEntry { Fields = [rule, diet] };

        Assert.Equal([rule, diet], SpeciesBehaviours.FieldsToApply(entry));

        entry.Behaviour = new SpeciesBehaviour { StartFrom = "CombatProfile_PredatorApex", Rules = ["Aggression_Predation"] };
        Assert.Equal([diet], SpeciesBehaviours.FieldsToApply(entry));
    }

    [Fact]
    public void With_a_behaviour_the_three_profile_rows_apply_even_at_their_template_value()
    {
        AnimalField Row(string name, string value) =>
            new() { Path = SpeciesBehaviours.ProfilePath + "/" + name, Kind = "text", Value = value, TemplateValue = value, Editable = true };
        var confidence = Row("confidenceLevel", "Confident");
        var temperament = Row("temperament", "Aggressive");
        var cannibalism = Row("cannibalism", "None");
        var other = Row("someOtherValue", "1");
        var entry = new SpeciesEntry { Fields = [confidence, temperament, cannibalism, other] };

        Assert.DoesNotContain(entry.Fields, f => SpeciesBehaviours.AlwaysApply(entry, f));

        entry.Behaviour = new SpeciesBehaviour { StartFrom = "CombatProfile_PredatorApex", Rules = [] };
        Assert.Equal([confidence, temperament, cannibalism], entry.Fields.Where(f => SpeciesBehaviours.AlwaysApply(entry, f)));
    }

    [Fact]
    public void IsRuleField_matches_the_rules_path_and_children()
    {
        Assert.True(SpeciesBehaviours.IsRuleField(SpeciesBehaviours.RulesPath));
        Assert.True(SpeciesBehaviours.IsRuleField(SpeciesBehaviours.RulesPath + "/0"));
        Assert.False(SpeciesBehaviours.IsRuleField(SpeciesBehaviours.RulesPath + "x"));
        Assert.False(SpeciesBehaviours.IsRuleField(SpeciesBehaviours.ProfilePath));
    }
}
