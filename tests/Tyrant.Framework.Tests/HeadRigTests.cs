using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class HeadRigTests
{
    private static RigQuaternion AxisAngle(float x, float y, float z, float degrees)
    {
        var len = MathF.Sqrt(x * x + y * y + z * z);
        var half = degrees * MathF.PI / 360f;
        var s = MathF.Sin(half) / len;
        return new RigQuaternion(x * s, y * s, z * s, MathF.Cos(half));
    }

    private static RigQuaternion PitchX(float degrees) => AxisAngle(1, 0, 0, degrees);

    private static void Same(RigQuaternion expected, RigQuaternion actual)
    {
        // q and -q are the same rotation
        Assert.True(MathF.Abs(RigQuaternion.Dot(expected.Normalized(), actual.Normalized())) > 0.9999f, $"expected {expected}, got {actual}");
    }

    private static void Same(RigVector3 expected, RigVector3 actual) => Assert.True(RigVector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    private static RigPose Pose(RigVector3 position, RigQuaternion rotation, float scale = 1) =>
        new RigPose(position, rotation, new RigVector3(scale, scale, scale));

    // The species' tuned boom (Styracosaurus-like values) and the parent of the boom base.
    private static readonly RigQuaternion Boom = new RigQuaternion(-0.066f, -0.010f, 0.003f, -0.998f).Normalized();
    private static readonly RigQuaternion HeadFromBoom = new RigQuaternion(-0.253f, -0.014f, 0.013f, -0.967f).Normalized();
    private const float BoomPitch = -16.5f;
    private static readonly RigPose Parent = Pose(new RigVector3(0, 1, 0.2f), AxisAngle(0.3f, 1, 0.2f, 40), 1.2f);

    [Fact]
    public void An_unchanged_skeleton_leaves_the_boom_alone()
    {
        var head = Pose(new RigVector3(0.1f, 1.4f, 1.0f), AxisAngle(1, 0.2f, 0, 25));
        var c = HeadRig.Measure(Parent, new RigVector3(0, 1.1f, 0.3f), head, Parent, new RigVector3(0, 1.1f, 0.3f), head);

        Assert.True(c.IsIdentity());
        var (length, boom, headFromBoom) = (0.8055f, Boom, HeadFromBoom);
        HeadRig.Apply(c, upsideDown: false, BoomPitch, ref length, ref boom, ref headFromBoom);
        Assert.Equal(0.8055f, length, 5);
        Same(Boom, boom);
        Same(HeadFromBoom, headFromBoom);
    }

    [Fact]
    public void A_longer_neck_lengthens_the_boom_by_the_same_ratio_and_keeps_its_aim()
    {
        var basePos = new RigVector3(0, 1.1f, 0.3f);
        var head0 = Pose(new RigVector3(0.1f, 1.4f, 1.0f), RigQuaternion.Identity);
        var head1 = Pose(basePos + new RigVector3(0.15f, 0.45f, 1.05f), RigQuaternion.Identity); // 1.5 × the base → head vector

        var c = HeadRig.Measure(Parent, basePos, head0, Parent, basePos, head1);

        Assert.Equal(1.5f, c.LengthScale, 4);
        Same(RigQuaternion.Identity, c.BoomTurn);
        var (length, boom, headFromBoom) = (0.8055f, Boom, HeadFromBoom);
        HeadRig.Apply(c, upsideDown: false, BoomPitch, ref length, ref boom, ref headFromBoom);
        Assert.Equal(0.8055f * 1.5f, length, 4);
        Same(Boom, boom);
        Same(HeadFromBoom, headFromBoom);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_boom_and_the_rigs_head_frame_turn_exactly_with_the_edited_skeleton(bool upsideDown)
    {
        // Any edit: the parent turns and grows, the base moves, the head moves and turns.
        var base0 = new RigVector3(0, 1.1f, 0.3f);
        var head0 = Pose(new RigVector3(0.1f, 1.4f, 1.0f), AxisAngle(1, 0.2f, 0, 25));
        var parent1 = Pose(new RigVector3(0, 1.05f, 0.25f), AxisAngle(0.3f, 1, 0.2f, 40) * AxisAngle(1, 0, 0.4f, 12), 1.3f);
        var base1 = new RigVector3(0.02f, 1.15f, 0.35f);
        var head1 = Pose(new RigVector3(0.0f, 1.9f, 1.2f), AxisAngle(0.2f, 1, 0, 30) * AxisAngle(1, 0.2f, 0, 25));

        var c = HeadRig.Measure(Parent, base0, head0, parent1, base1, head1);
        var (length, boom, headFromBoom) = (0.8055f, Boom, HeadFromBoom);
        HeadRig.Apply(c, upsideDown, BoomPitch, ref length, ref boom, ref headFromBoom);

        // The game's boom frame: base parent rotation × conversion × boom orientation × pitch (AnimalHeadIKRigUpdateJob).
        var conv = upsideDown ? HeadRig.ZDown : HeadRig.ZUp;
        var b0 = Parent.Rotation * conv * Boom * PitchX(BoomPitch);
        var b1 = parent1.Rotation * conv * boom * PitchX(BoomPitch);
        // The boom keeps its angle to the base → head line (the tuning aims it a little off that line).
        var forward = new RigVector3(0, 0, 1);
        Assert.Equal(Angle(b0.Rotate(forward), head0.Position - base0), Angle(b1.Rotate(forward), head1.Position - base1), 3);
        // The rig's head frame (boom frame × head-from-boom) turns exactly as the real head did.
        Same(head1.Rotation * head0.Rotation.Inverse() * b0 * HeadFromBoom, b1 * headFromBoom);
        // And the length follows the base → head distance measured in the parent's scale.
        var d0 = (head0.Position - base0).Length() / 1.2f;
        var d1 = (head1.Position - base1).Length() / 1.3f;
        Assert.Equal(0.8055f * d1 / d0, length, 4);
    }

    [Fact]
    public void With_the_parent_unchanged_the_boom_turns_exactly_as_the_base_to_head_line_turned()
    {
        var base0 = new RigVector3(0, 1.1f, 0.3f);
        var head0 = Pose(new RigVector3(0.1f, 1.4f, 1.0f), RigQuaternion.Identity);
        var head1 = Pose(new RigVector3(0.0f, 1.9f, 1.2f), RigQuaternion.Identity);

        var c = HeadRig.Measure(Parent, base0, head0, Parent, base0, head1);
        var (length, boom, headFromBoom) = (0.8055f, Boom, HeadFromBoom);
        HeadRig.Apply(c, upsideDown: false, BoomPitch, ref length, ref boom, ref headFromBoom);

        var forward = new RigVector3(0, 0, 1);
        var b0 = Parent.Rotation * HeadRig.ZUp * Boom * PitchX(BoomPitch);
        var b1 = Parent.Rotation * HeadRig.ZUp * boom * PitchX(BoomPitch);
        Same(Normalize(FromTo(head0.Position - base0, head1.Position - base0).Rotate(b0.Rotate(forward))), Normalize(b1.Rotate(forward)));
    }

    [Fact]
    public void The_correction_roundtrips_through_mod_json()
    {
        var c = new HeadRigCorrection(1.25f, AxisAngle(1, 0, 0, 10), AxisAngle(0, 1, 0, -5), "abc123");

        var back = HeadRigCorrection.Parse(Json.Parse(Json.Write(c.ToMap())), "headRig");

        Assert.Equal(1.25f, back.LengthScale, 5);
        Same(c.BoomTurn, back.BoomTurn);
        Same(c.HeadTurn, back.HeadTurn);
        Assert.Equal("abc123", back.RigHash);
    }

    private static float Angle(RigVector3 a, RigVector3 b)
    {
        a = Normalize(a); b = Normalize(b);
        return MathF.Acos(Math.Clamp(a.X * b.X + a.Y * b.Y + a.Z * b.Z, -1f, 1f)) * 180f / MathF.PI;
    }

    private static RigVector3 Normalize(RigVector3 v) { var l = v.Length(); return new RigVector3(v.X / l, v.Y / l, v.Z / l); }

    private static RigQuaternion FromTo(RigVector3 a, RigVector3 b)
    {
        a = Normalize(a); b = Normalize(b);
        var cross = new RigVector3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        return new RigQuaternion(cross.X, cross.Y, cross.Z, 1 + a.X * b.X + a.Y * b.Y + a.Z * b.Z).Normalized();
    }
}
