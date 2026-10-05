using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class RigTests
{
    [Fact]
    public void Apply_moves_rotates_and_scales_the_animated_value()
    {
        var o = new RigOffset { Move = new RigVector3(0, 0.3f, 0), Rotate = new RigQuaternion(0, 0, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)), Scale = new RigVector3(2, 2, 2) };
        var p = new RigVector3(1, 0, 0);
        var r = RigQuaternion.Identity;
        var s = RigVector3.One;

        o.Apply(ref p, ref r, ref s);

        Assert.Equal(0f, p.X, 4);
        Assert.Equal(2.3f, p.Y, 4); // move + rotate(scale ⊙ p)
        Assert.Equal(o.Rotate, r);
        Assert.Equal(new RigVector3(2, 2, 2), s);
    }

    [Fact]
    public void ApplyInverse_undoes_apply()
    {
        var o = new RigOffset
        {
            Move = new RigVector3(0.1f, -0.2f, 0.05f), Rotate = new RigQuaternion(0.1f, 0.2f, 0.3f, 0.9f).Normalized(), Scale = new RigVector3(1.5f, 0.8f, 1.2f),
        };
        var p = new RigVector3(0.4f, 0.5f, -0.6f);
        var r = new RigQuaternion(0.3f, 0, 0.1f, 0.95f).Normalized();
        var s = new RigVector3(1, 1.1f, 0.9f);
        var (p0, r0, s0) = (p, r, s);

        o.Apply(ref p, ref r, ref s);
        o.ApplyInverse(ref p, ref r, ref s);

        Assert.True(RigVector3.Distance(p0, p) < 1e-5f);
        Assert.True(RigVector3.Distance(s0, s) < 1e-5f);
        Assert.True(MathF.Abs(RigQuaternion.Dot(r0, r)) > 0.99999f);
    }

    [Fact]
    public void A_growth_position_gets_the_same_offset_as_an_animated_one()
    {
        var o = RigOffset.Identity;
        o.Move = new RigVector3(0, 0.1f, 0);
        o.Scale = new RigVector3(1, 1.5f, 1);
        var g = new RigVector3(0, 0.208f, 0.05f);
        var r = RigQuaternion.Identity;
        var s = RigVector3.One;

        o.Apply(ref g, ref r, ref s);

        Assert.Equal(0.1f + 0.312f, g.Y, 4);
    }

    [Fact]
    public void Parse_reads_parts_and_fills_identity()
    {
        var rig = RigEdit.Parse(Json.Parse("""{"Jaw":{"move":[0,0.1,0]},"Neck.002":{"scale":[1,1.2,1]}}"""), "skin x");

        Assert.Equal(new RigVector3(0, 0.1f, 0), rig["Jaw"].Move);
        Assert.Equal(RigQuaternion.Identity, rig["Jaw"].Rotate);
        Assert.Equal(RigVector3.One, rig["Jaw"].Scale);
        Assert.Equal(new RigVector3(1, 1.2f, 1), rig["Neck.002"].Scale);
    }

    [Theory]
    [InlineData("""{"Jaw":{"scale":[1,0,1]}}""", "scale")]
    [InlineData("""{"Jaw":{"move":[0,1]}}""", "move")]
    [InlineData("""{"Jaw":{"rotate":[0,0,0,0]}}""", "rotate")]
    [InlineData("""{"Jaw":5}""", "Jaw")]
    [InlineData("""[1]""", "rig")]
    public void Parse_refuses_bad_values(string json, string mentions)
    {
        var ex = Assert.Throws<ManifestException>(() => RigEdit.Parse(Json.Parse(json), "skin x"));
        Assert.Contains(mentions, ex.Message);
    }

    [Fact]
    public void Round_trip_keeps_values_and_hash()
    {
        var rig = RigEdit.Parse(Json.Parse("""{"Jaw":{"move":[0,0.1,0],"rotate":[0,0,0.38268343,0.9238795],"scale":[1.1,1,1]}}"""), "x");

        var again = RigEdit.Parse(Json.Parse(Json.Write(RigEdit.ToJson(rig))), "x");

        Assert.Equal(RigEdit.Hash(rig), RigEdit.Hash(again));
        Assert.NotEqual("", RigEdit.Hash(rig));
        Assert.Equal("", RigEdit.Hash(null));
        Assert.Equal("", RigEdit.Hash(new Dictionary<string, RigOffset>()));
    }

    [Fact]
    public void Identity_parts_are_left_out_when_written()
    {
        var rig = RigEdit.Parse(Json.Parse("""{"Jaw":{"move":[0,0.1,0]}}"""), "x");

        var json = Json.Write(RigEdit.ToJson(rig));

        Assert.Contains("move", json);
        Assert.DoesNotContain("rotate", json);
        Assert.DoesNotContain("scale", json);
    }
}
