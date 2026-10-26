using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Dumper.Serialization;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class BehaviourCatalogTests
{
    const string Ai = "PrehistoricKingdom.AnimalsV2.AI.";

    static string Link(string asset, int id) => $$$"""{"asset":{"$ref":{"type":"{{{Ai}}}AggressionProfileAsset","name":"{{{asset}}}","id":{{{id}}}}},"inlineProfile":{},"overrideFlags":"0","behavioralContribution":0.15}""";

    static Workspace Setup(bool withProfiles, bool broken = false)
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(TestTemp.Root, Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        var result = new DumpResult();
        void Add(string type, string name, long id, string json) => result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo(type, name, id), json));
        Add("PrehistoricKingdom.AnimalData", "Triceratops", 1, """{"speciesID":"Triceratops Horridus","brain":{"$ref":{"type":"PrehistoricKingdom.AnimalsV2.AI.AnimalBrain","name":"TriceratopsHorridus","id":2}}}""");
        if (withProfiles)
        {
            Add(Ai + "AnimalBrain", "TriceratopsHorridus", 2, """{"combat":{"shared":{"combatProfile":{"$ref":{"type":"PrehistoricKingdom.AnimalsV2.AI.AnimalCombatProfile","name":"CombatProfile_TankTerritorial","id":3}}}}}""");
            Add(Ai + "AnimalCombatProfile", "CombatProfile_TankTerritorial", 3,
                $$$"""{"confidenceLevel":"Confident","temperament":"Aggressive","cannibalism":"None","aggressionProfileEntries":[{{{Link("Aggression_DefendWithFamily", 4)}}},{{{Link("Aggression_NestDefense", 5)}}},{{{Link("Aggression_DefendWithFamily", 4)}}},{"asset":null,"inlineProfile":{}}]}""");
            Add(Ai + "AnimalCombatProfile", "CombatProfile_Deer", 6, broken
                ? $$$"""{"confidenceLevel":7,"aggressionProfileEntries":[42,"text",{"asset":"x"},{"asset":{"$ref":"oops"}},{{{Link("Aggression_PreyFleeFromPredator", 7)}}}]}"""
                : """{"confidenceLevel":"Timid","temperament":"Passive","cannibalism":"None","aggressionProfileEntries":[]}""");
            Add(Ai + "AggressionProfileAsset", "Aggression_DefendWithFamily", 4, """{"profile":{"showInGUI":true}}""");
        }
        if (broken)
        {
            // Odd shapes a game update could leave in the dump: each is skipped, never fatal.
            Add(Ai + "AnimalCombatProfile", "CombatProfile_Elephant", 8, "[1, 2]");
            Add(Ai + "AnimalBrain", "Odd", 9, """{"combat":"broken"}""");
            Add(Ai + "AnimalBrain", "Odder", 10, """{"combat":{"shared":{"combatProfile":{"$ref":7}}}}""");
            Add("PrehistoricKingdom.AnimalData", "Weird", 11, """{"speciesID":"Weird","brain":{"$ref":"Odd"}}""");
            Add(Ai + "AggressionProfileAsset", "Aggression_NestDefense", 5, """{"profile":"weird"}""");
        }
        DumpWriter.Write(ws.DataDir, result, [], new DumpManifest { RequestId = "r" });
        return ws;
    }

    [Fact]
    public void Load_lists_known_profiles_in_order_with_distinct_rules_and_users()
    {
        var data = BehaviourCatalog.Load(Setup(true));

        Assert.Equal(["CombatProfile_TankTerritorial", "CombatProfile_Deer"], data.Profiles.Select(p => p.Asset));
        var tank = data.Profiles[0];
        Assert.Equal("Territorial tank", tank.Name);
        Assert.Equal(("Confident", "Aggressive", "None"), (tank.Confidence, tank.Temperament, tank.Cannibalism));
        Assert.Equal(["Aggression_DefendWithFamily", "Aggression_NestDefense"], tank.Rules);
        Assert.Equal(["Triceratops Horridus"], tank.UsedBy);
        Assert.Empty(data.Profiles[1].UsedBy);
        Assert.Equal(SpeciesBehaviours.Rules.Select(r => r.Asset), data.Rules.Select(r => r.Asset));
        Assert.True(data.Rules.Single(r => r.Asset == "Aggression_DefendWithFamily").ShownInGame);
        Assert.False(data.Rules.Single(r => r.Asset == "Aggression_NestDefense").ShownInGame);
    }

    [Fact]
    public void Load_skips_values_of_an_unexpected_shape()
    {
        var data = BehaviourCatalog.Load(Setup(true, broken: true));

        Assert.Equal(["CombatProfile_TankTerritorial", "CombatProfile_Elephant", "CombatProfile_Deer"], data.Profiles.Select(p => p.Asset));
        var deer = data.Profiles[2];
        Assert.Equal(["Aggression_PreyFleeFromPredator"], deer.Rules);
        Assert.Equal(("", "", ""), (deer.Confidence, deer.Temperament, deer.Cannibalism));
        Assert.Empty(data.Profiles[1].Rules);
        Assert.Equal(["Triceratops Horridus"], data.Profiles[0].UsedBy);
        Assert.False(data.Rules.Single(r => r.Asset == "Aggression_NestDefense").ShownInGame);
    }

    [Fact]
    public void Load_is_empty_without_combat_profiles_in_the_dump()
    {
        var data = BehaviourCatalog.Load(Setup(false));

        Assert.Empty(data.Profiles);
        Assert.Empty(data.Rules);
    }

    [Fact]
    public void TemplateProfile_reads_the_reference_row()
    {
        var fields = new List<AnimalField>
        {
            new() { Path = "/other", Kind = "text", Value = "x" },
            new() { Path = SpeciesBehaviours.ProfilePath, Kind = "reference", Value = """{"type":"PrehistoricKingdom.AnimalsV2.AI.AnimalCombatProfile","name":"CombatProfile_TankTerritorial","id":3}""" },
        };

        Assert.Equal("CombatProfile_TankTerritorial", BehaviourCatalog.TemplateProfile(fields));
        Assert.Null(BehaviourCatalog.TemplateProfile([]));
        Assert.Null(BehaviourCatalog.TemplateProfile([new() { Path = SpeciesBehaviours.ProfilePath, Kind = "reference", Value = "5" }]));
        Assert.Null(BehaviourCatalog.TemplateProfile([new() { Path = SpeciesBehaviours.ProfilePath, Kind = "reference", Value = """{"name":3}""" }]));
    }
}
