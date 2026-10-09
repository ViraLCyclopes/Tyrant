using System.Text.Json;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;

namespace Tyrant.Core.Tests;

public class PrefabFieldTreeTests
{
    private const string Physics = """
        { "m_GameObject": {"m_FileID":0,"m_PathID":5}, "m_Enabled": 1, "m_Script": {"m_FileID":1,"m_PathID":9}, "m_Name": "",
          "inputParams": { "waterlineHeight": 2.75, "mode": 1, "offset": {"x":0,"y":1,"z":0} },
          "tint": {"r":1,"g":0.5,"b":0,"a":1}, "label": "spino", "swims": true,
          "target": {"m_FileID":0,"m_PathID":77}, "weights": [1, 2], "raw": {"$bytes": 40} }
        """;

    private static readonly PrefabComponents Prefab = new("Spinosaurus", [
        new PrefabObject("", [
            new PrefabComponent("AnimalPhysics", null, "Assembly-CSharp", 0, Physics),
            new PrefabComponent("LODGroup", null, null, 0, """{"m_LODs":[{"screenRelativeHeight":0.5,"fadeTransitionWidth":0},{"screenRelativeHeight":0.1,"fadeTransitionWidth":0}]}"""),
            new PrefabComponent("Animator", null, null, 0, """{"m_ApplyRootMotion":1,"m_CullingMode":1,"m_Avatar":{"m_FileID":0,"m_PathID":3}}"""),
        ]),
        new PrefabObject("IK/LegL", [
            new PrefabComponent("FABRIKComponentAnimal", null, "Assembly-CSharp", 0, """{"core":{"influence":1}}"""),
            new PrefabComponent("FABRIKComponentAnimal", null, "Assembly-CSharp", 1, """{"core":{"influence":0.5}}"""),
        ]),
    ]);

    private static IReadOnlyList<PrefabFieldInfo> Tree() =>
        PrefabFieldTree.Build(Prefab, (c, path) => c.Type == "AnimalPhysics" && path == "inputParams.mode" ? [new("Walk", 0), new("Swim", 1)] : null);

    private static PrefabFieldInfo Get(string obj, string component, string field) =>
        Tree().Single(f => f.Object == obj && f.Component == component && f.Field == field);

    [Fact]
    public void Script_fields_are_flattened_with_kinds_and_header_fields_left_out()
    {
        var tree = Tree();

        Assert.DoesNotContain(tree, f => f.Field.StartsWith("m_"));
        Assert.Equal(("number", "2.75", true), Pick(Get("", "AnimalPhysics", "inputParams.waterlineHeight")));
        Assert.Equal(("enum", "1", true), Pick(Get("", "AnimalPhysics", "inputParams.mode")));
        Assert.Equal(["Walk", "Swim"], Get("", "AnimalPhysics", "inputParams.mode").EnumNames);
        Assert.Equal("vector3", Get("", "AnimalPhysics", "inputParams.offset").Kind);
        Assert.Equal("color", Get("", "AnimalPhysics", "tint").Kind);
        Assert.Equal(("string", "\"spino\"", true), Pick(Get("", "AnimalPhysics", "label")));
        Assert.Equal("bool", Get("", "AnimalPhysics", "swims").Kind);
        Assert.Equal(("reference", false), (Get("", "AnimalPhysics", "target").Kind, Get("", "AnimalPhysics", "target").Editable));
        Assert.Equal(("length", "2", false), Pick(Get("", "AnimalPhysics", "weights")));
        Assert.True(Get("", "AnimalPhysics", "weights[1]").Editable);
        Assert.False(Get("", "AnimalPhysics", "raw").Editable);
    }

    private static (string, string, bool) Pick(PrefabFieldInfo f) => (f.Kind, f.Value, f.Editable);

    [Fact]
    public void Built_in_components_show_only_their_mapped_fields()
    {
        var tree = Tree();

        Assert.Equal(["lods[0].screenRelativeTransitionHeight", "lods[1].screenRelativeTransitionHeight"], tree.Where(f => f.Component == "LODGroup").Select(f => f.Field));
        Assert.Equal("0.1", Get("", "LODGroup", "lods[1].screenRelativeTransitionHeight").Value);
        Assert.Equal(["applyRootMotion", "cullingMode"], tree.Where(f => f.Component == "Animator").Select(f => f.Field));
        Assert.Equal("true", Get("", "Animator", "applyRootMotion").Value);
        Assert.Equal(["AlwaysAnimate", "CullUpdateTransforms", "CullCompletely"], Get("", "Animator", "cullingMode").EnumNames);
        Assert.Equal([0L, 1L, 2L], Get("", "Animator", "cullingMode").EnumValues);
    }

    [Fact]
    public void A_second_component_of_a_type_is_numbered()
    {
        Assert.Equal("1", Get("IK/LegL", "FABRIKComponentAnimal", "core.influence").Value);
        Assert.Equal("0.5", Get("IK/LegL", "FABRIKComponentAnimal#1", "core.influence").Value);
    }

    [Fact]
    public void Enum_numbers_are_checked_as_real_values_not_positions()
    {
        var tree = PrefabFieldTree.Build(Prefab, (c, path) => path == "inputParams.mode" ? [new("Walk", 0), new("Swim", 2)] : null);
        var field = tree.Single(f => f.Component == "AnimalPhysics" && f.Field == "inputParams.mode");
        using var two = JsonDocument.Parse("2");
        using var one = JsonDocument.Parse("1");

        Assert.Null(PrefabFieldTree.CheckValue(field, two.RootElement));
        Assert.Contains("Walk, Swim", PrefabFieldTree.CheckValue(field, one.RootElement));
    }

    [Theory]
    [InlineData("inputParams.waterlineHeight", "3.5", null)]
    [InlineData("inputParams.waterlineHeight", "\"deep\"", "number")]
    [InlineData("inputParams.mode", "\"Swim\"", null)]
    [InlineData("inputParams.mode", "1", null)]
    [InlineData("inputParams.mode", "\"Fly\"", "Walk, Swim")]
    [InlineData("inputParams.mode", "5", "Walk, Swim")]
    [InlineData("inputParams.offset", """{"x":1,"y":2,"z":3}""", null)]
    [InlineData("inputParams.offset", """{"x":1,"y":2}""", "z")]
    [InlineData("swims", "1", "true or false")]
    [InlineData("target", "1", "cannot be changed")]
    public void Values_are_checked_against_the_field_kind(string field, string json, string? word)
    {
        using var value = JsonDocument.Parse(json);

        var problem = PrefabFieldTree.CheckValue(Get("", "AnimalPhysics", field), value.RootElement);

        if (word is null) Assert.Null(problem);
        else Assert.Contains(word, problem);
    }

}

public enum PrefabTestGait { Walk, Run = 5, Swim = 2 }

public class PrefabTestHolderBase { public PrefabTestGait[] baseGaits = []; }

public class PrefabTestHolder : PrefabTestHolderBase
{
    public List<PrefabTestGait> gaits = [];
    public int plain;
}

public class PrefabEnumNamesTests
{
    [Fact]
    public void Enum_names_come_from_the_assembly_through_lists_arrays_and_base_classes()
    {
        var asm = typeof(PrefabTestHolder).Assembly;
        using var names = new PrefabEnumNames(Path.GetDirectoryName(asm.Location)!);
        var component = new PrefabComponent("PrefabTestHolder", "Tyrant.Core.Tests", asm.GetName().Name, 0, "{}");

        PrefabEnumValue[] expected = [new("Walk", 0), new("Swim", 2), new("Run", 5)];
        Assert.Equal(expected, names.For(component, "gaits"));
        Assert.Equal(expected, names.For(component, "baseGaits"));
        Assert.Null(names.For(component, "plain"));
        Assert.Null(names.For(component, "missing"));
    }
}
