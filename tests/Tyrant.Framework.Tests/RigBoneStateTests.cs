using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class RigBoneStateTests
{
    private static readonly RigOffset Lift = new() { Move = new RigVector3(0, 0.5f, 0), Rotate = RigQuaternion.Identity, Scale = RigVector3.One };
    private static readonly RigVector3 Rest = new(0, 1, 0);

    private static RigQuaternion Turn(float angle) => new(0, MathF.Sin(angle / 2), 0, MathF.Cos(angle / 2));

    /// <summary>One frame: the animator's values go in, the composed values come out and end the frame as the bone's final pose.</summary>
    private static (RigVector3 P, RigQuaternion R, RigVector3 S) Frame(RigBoneState bone, RigVector3 p, RigQuaternion r, RigVector3 s)
    {
        bone.Compose(Lift, ref p, ref r, ref s);
        bone.NoteFinal(p, r, s);
        return (p, r, s);
    }

    [Fact]
    public void A_bone_whose_rotation_is_animated_but_position_never_rewritten_is_moved_once_not_every_frame()
    {
        // The Nursery preview: the animation turns the bone each frame; nothing resets its position, so the transform keeps
        // last frame's composed value. Stacking the move there sent the bone (and the mesh weighted to it) off into the sky.
        var bone = new RigBoneState();
        var (p, _, s) = Frame(bone, Rest, Turn(0.1f), RigVector3.One);
        for (var i = 2; i <= 60; i++) (p, _, s) = Frame(bone, p, Turn(0.1f * i), s);

        Assert.Equal(1.5f, p.Y, 5);
    }

    [Fact]
    public void A_bone_the_animator_rewrites_every_frame_gets_the_offset_on_the_new_value()
    {
        var bone = new RigBoneState();
        Frame(bone, Rest, Turn(0.1f), RigVector3.One);

        var (p, _, _) = Frame(bone, new RigVector3(0, 2, 0), Turn(0.2f), RigVector3.One);

        Assert.Equal(2.5f, p.Y, 5);
    }

    [Fact]
    public void A_bone_the_animator_skipped_keeps_its_pose_and_is_not_written()
    {
        var bone = new RigBoneState();
        var (p, r, s) = Frame(bone, Rest, Turn(0.1f), RigVector3.One);

        Assert.False(bone.Compose(Lift, ref p, ref r, ref s));
        Assert.Equal(1.5f, p.Y, 5);
    }

    [Fact]
    public void The_first_frame_composes_whatever_the_bone_holds()
    {
        var bone = new RigBoneState();
        var (p, r, s) = (Rest, RigQuaternion.Identity, RigVector3.One);

        Assert.True(bone.Compose(Lift, ref p, ref r, ref s));
        Assert.Equal(1.5f, p.Y, 5);
    }
}
