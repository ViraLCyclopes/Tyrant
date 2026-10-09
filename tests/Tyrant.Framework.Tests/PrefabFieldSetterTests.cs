using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PrefabFieldSetterTests
{
    public enum Mode { Walk, Swim, Fly }
    public struct Vec3 { public float x, y, z; }
    public struct Params { public float waterlineHeight; public int steps; public Mode mode; public Vec3 offset; }
    public class Joint { public float boneLength; }
    public class BaseComponent { protected bool enabledFlag; }
    public class Physics : BaseComponent
    {
        public Params inputParams;
        private string label = "a";
        public List<Joint> chain = [new Joint(), new Joint()];
        public float[] weights = [1f, 2f];
        public Params[] presets = [new Params(), new Params()];
        public char letter;
        public float[,] grid = new float[2, 2];
        public string Label => label;
        public bool Enabled => enabledFlag;
    }

    [Fact]
    public void Sets_numbers_bools_strings_and_enums_through_structs_lists_and_arrays()
    {
        var p = new Physics();

        Assert.True(PrefabFieldSetter.TrySet(p, "inputParams.waterlineHeight", 3.5, out _));
        Assert.True(PrefabFieldSetter.TrySet(p, "inputParams.steps", 4.0, out _));
        Assert.True(PrefabFieldSetter.TrySet(p, "inputParams.mode", "Swim", out _));
        Assert.True(PrefabFieldSetter.TrySet(p, "label", "b", out _));
        Assert.True(PrefabFieldSetter.TrySet(p, "enabledFlag", true, out _)); // a base class's non-public field
        Assert.True(PrefabFieldSetter.TrySet(p, "chain[1].boneLength", 0.25, out _));
        Assert.True(PrefabFieldSetter.TrySet(p, "weights[0]", 7.0, out _));
        Assert.True(PrefabFieldSetter.TrySet(p, "presets[1].mode", 2.0, out _)); // an enum by number, inside an array of structs

        Assert.Equal(3.5f, p.inputParams.waterlineHeight);
        Assert.Equal(4, p.inputParams.steps);
        Assert.Equal(Mode.Swim, p.inputParams.mode);
        Assert.Equal("b", p.Label);
        Assert.True(p.Enabled);
        Assert.Equal(0.25f, p.chain[1].boneLength);
        Assert.Equal(7f, p.weights[0]);
        Assert.Equal(Mode.Fly, p.presets[1].mode);
    }

    [Fact]
    public void Sets_a_vector_from_an_object_value()
    {
        var p = new Physics();
        var value = new Dictionary<string, object?> { ["x"] = 1.0, ["y"] = 2.0, ["z"] = 3.0 };

        Assert.True(PrefabFieldSetter.TrySet(p, "inputParams.offset", value, out _));

        Assert.Equal((1f, 2f, 3f), (p.inputParams.offset.x, p.inputParams.offset.y, p.inputParams.offset.z));
    }

    [Theory]
    [InlineData("inputParams.nope", 1.0, "nope")]
    [InlineData("chain[5].boneLength", 1.0, "5")]
    [InlineData("inputParams.waterlineHeight", "deep", "number")]
    [InlineData("inputParams.steps", 2.5, "whole number")]
    [InlineData("inputParams.mode", "Burrow", "Burrow")]
    [InlineData("inputParams.mode", 9.0, "9")]
    [InlineData("chain", 1.0, "List")]
    public void A_wrong_path_or_value_is_refused_with_why_and_changes_nothing(string path, object value, string word)
    {
        var p = new Physics();

        Assert.False(PrefabFieldSetter.TrySet(p, path, value, out var error));

        Assert.Contains(word, error);
        Assert.Equal(0f, p.inputParams.waterlineHeight);
        Assert.Equal(2, p.chain.Count);
    }

    [Fact]
    public void A_vector_missing_a_component_is_refused()
    {
        var p = new Physics();
        Assert.False(PrefabFieldSetter.TrySet(p, "inputParams.offset", new Dictionary<string, object?> { ["x"] = 1.0, ["y"] = 2.0 }, out var error));
        Assert.Contains("z", error);
    }

    [Fact]
    public void A_char_field_is_refused_without_throwing()
    {
        var p = new Physics();

        Assert.False(PrefabFieldSetter.TrySet(p, "letter", 65.0, out var error));

        Assert.Contains("Char", error);
        Assert.Equal('\0', p.letter);
    }

    [Fact]
    public void A_multi_dimensional_array_item_is_refused_without_throwing()
    {
        var p = new Physics();

        Assert.False(PrefabFieldSetter.TrySet(p, "grid[0]", 1.0, out var error));

        Assert.NotNull(error);
        Assert.Equal(0f, p.grid[0, 0]);
    }

    [Fact]
    public void A_float_out_of_single_range_is_refused()
    {
        var p = new Physics();

        Assert.False(PrefabFieldSetter.TrySet(p, "inputParams.waterlineHeight", 1e300, out var error));

        Assert.Contains("out of range", error);
        Assert.Equal(0f, p.inputParams.waterlineHeight);
    }
}
