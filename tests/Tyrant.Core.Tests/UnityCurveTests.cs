using System.Text.Json;
using Tyrant.Core.Blender;

namespace Tyrant.Core.Tests;

public class UnityCurveTests
{
    private static JsonElement Curve(string keys) => JsonDocument.Parse($$"""{"keys":[{{keys}}],"preWrapMode":"ClampForever","postWrapMode":"ClampForever"}""").RootElement;

    [Fact]
    public void A_straight_line_with_matching_tangents_is_linear()
    {
        var c = Curve("""{"time":0,"value":0,"inTangent":1,"outTangent":1},{"time":1,"value":1,"inTangent":1,"outTangent":1}""");
        Assert.Equal(0.25f, UnityCurve.Evaluate(c, 0.25f), 4);
        Assert.Equal(0.8f, UnityCurve.Evaluate(c, 0.8f), 4);
    }

    [Fact]
    public void Flat_tangents_ease_in_and_out()
    {
        var c = Curve("""{"time":0,"value":0,"inTangent":0,"outTangent":0},{"time":1,"value":1,"inTangent":0,"outTangent":0}""");
        Assert.Equal(0.5f, UnityCurve.Evaluate(c, 0.5f), 4);
        Assert.Equal(0.15625f, UnityCurve.Evaluate(c, 0.25f), 4); // 3t²-2t³
    }

    [Fact]
    public void Clamps_outside_the_keys_and_steps_on_infinite_tangents()
    {
        var c = Curve("""{"time":0.2,"value":2,"inTangent":0,"outTangent":"Infinity"},{"time":0.6,"value":5,"inTangent":0,"outTangent":0}""");
        Assert.Equal(2f, UnityCurve.Evaluate(c, 0f));
        Assert.Equal(2f, UnityCurve.Evaluate(c, 0.5f));
        Assert.Equal(5f, UnityCurve.Evaluate(c, 1f));
    }

    [Fact]
    public void The_dumped_allosaurus_skin_curve_matches_hand_computed_values()
    {
        // Allosaurus Anax skinGrowthCurve from the user's dump (weightedMode None).
        var c = Curve("""
            {"time":0,"value":0,"inTangent":0,"outTangent":0},
            {"time":0.46406886,"value":0.480891615,"inTangent":1.5399735,"outTangent":1.5399735},
            {"time":1,"value":1,"inTangent":0,"outTangent":0}
            """);
        Assert.Equal(0.480891615f, UnityCurve.Evaluate(c, 0.46406886f), 5);
        // t = 0.25 in the first segment: s = 0.538712, h01 = 0.558948, h11 = -0.133869 →
        // 0.480892·0.558948 + 0.464069·1.539974·(−0.133869) = 0.268795 − 0.095671 = 0.173124
        Assert.Equal(0.1731f, UnityCurve.Evaluate(c, 0.25f), 3);
    }

    [Fact]
    public void Sampling_gives_count_values_from_0_to_1_and_a_line_without_a_curve()
    {
        Assert.Equal([0f, 0.25f, 0.5f, 0.75f, 1f], UnityCurve.Sample(null, 5));
    }
}
