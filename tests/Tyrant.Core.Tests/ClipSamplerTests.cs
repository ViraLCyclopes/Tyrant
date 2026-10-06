using System.Numerics;
using Tyrant.Core.Animation;
using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class ClipSamplerTests
{
    private static SkeletonNode Skeleton()
    {
        // As in the game: the prefab, then the armature's own node, then MainBone (paths start below the prefab).
        var root = new SkeletonNode("Carcharodontosaurus.V2", Vector3.Zero, Quaternion.Identity, Vector3.One);
        var armature = new SkeletonNode("Carch", Vector3.Zero, Quaternion.Identity, Vector3.One, root);
        root.Children.Add(armature);
        var main = new SkeletonNode("MainBone", Vector3.Zero, Quaternion.Identity, Vector3.One, armature);
        armature.Children.Add(main);
        var pelvis = new SkeletonNode("Pelvis", Vector3.Zero, Quaternion.Identity, Vector3.One, main);
        main.Children.Add(pelvis);
        return root;
    }

    private static RawClip Clip(string name, float length, float[] dense, int denseCurves, int frames, float[] constant, params ClipBindingRaw[] bindings) =>
        new(name, 30, 0, length, true, 0, [], denseCurves, frames, 0, dense, constant, bindings);

    [Fact]
    public void A_dense_position_is_sampled_at_the_clip_rate_and_thinned_to_its_ends()
    {
        // MainBone x: 0 → 3 over 1 s linearly (31 frames at 30 fps): thinning keeps only the two ends.
        const int frames = 31;
        var dense = Enumerable.Range(0, frames).SelectMany(f => new[] { f / 10f, 0f, 0f }).ToArray();
        var clip = Clip("Carch|LocWalk", 1, dense, 3, frames, [], new ClipBindingRaw(Models.ClipReader.Crc32("Carch/MainBone"), 1));

        var anim = ClipSampler.Sample(clip, Skeleton());

        var main = Assert.Single(anim.Bones);
        Assert.Equal("MainBone", main.Bone);
        Assert.Equal(2, main.Position!.Count);
        Assert.Equal(3f, main.Position[1].X, 4);
        Assert.Equal(1f, main.Position[1].Time, 4);
        Assert.Null(main.Rotation);
        Assert.True(anim.Travels);
        Assert.Equal(("Carch|LocWalk", "LocWalk", 30f, 1f, true), (anim.Id, anim.Name, anim.FrameRate, anim.Length, anim.Loops));
    }

    [Fact]
    public void Rotations_are_normalised_and_a_constant_channel_is_one_key()
    {
        var clip = Clip("Carch|Idle", 1, [], 0, 0, [0, 0, 0, 2], new ClipBindingRaw(Models.ClipReader.Crc32("Carch/MainBone/Pelvis"), 2));

        var anim = ClipSampler.Sample(clip, Skeleton());

        var key = Assert.Single(Assert.Single(anim.Bones).Rotation!);
        Assert.Equal(1f, key.W, 5);
        Assert.False(anim.Travels);
    }

    [Fact]
    public void The_animators_own_curves_take_one_value_each_and_object_curves_none()
    {
        // As the game's clips: the Animator's root motion curves (attributes 1..13 of the Animator, one float each) and an
        // object-reference curve (no float) come before the bones' own; the bones must still read their own values.
        // Pelvis turned 90 degrees about z; read from the wrong place it would take the 9s that follow.
        var clip = Clip("Camara|LocWalk", 1, [], 0, 0, [5, 6, 0, 0, 0.70710677f, 0.70710677f, 9, 9, 9, 9, 9, 9],
            new ClipBindingRaw(0, 1, TypeId: 95),
            new ClipBindingRaw(0, 2, TypeId: 95),
            new ClipBindingRaw(0, 7, TypeId: 95, ObjectReference: true),
            new ClipBindingRaw(Models.ClipReader.Crc32("Carch/MainBone/Pelvis"), 2));

        var anim = ClipSampler.Sample(clip, Skeleton());

        var pelvis = Assert.Single(anim.Bones);
        Assert.Equal("Pelvis", pelvis.Bone);
        var key = Assert.Single(pelvis.Rotation!);
        Assert.Equal(0f, key.X, 5);
        Assert.Equal(0f, key.Y, 5);
        Assert.Equal(0.70710677f, key.Z, 5);
        Assert.Equal(0.70710677f, key.W, 5);
    }

    [Fact]
    public void A_binding_to_a_bone_the_skeleton_lacks_is_named_and_skipped()
    {
        var clip = Clip("Carch|Roar", 1, [], 0, 0, [1, 2, 3, 0, 0, 0],
            new ClipBindingRaw(Models.ClipReader.Crc32("Carch/MainBone/Tongue"), 1), new ClipBindingRaw(Models.ClipReader.Crc32("Carch/MainBone"), 1));

        var anim = ClipSampler.Sample(clip, Skeleton());

        Assert.Equal("MainBone", Assert.Single(anim.Bones).Bone);
        Assert.Contains(anim.Skipped, s => s.Contains("not on this skeleton"));
    }

    [Fact]
    public void Thinning_never_moves_a_sampled_value_past_the_tolerance()
    {
        // A sine on Pelvis y: every frame must stay within 0.01 mm of the line between the kept keys around it.
        const int frames = 61;
        var dense = Enumerable.Range(0, frames).SelectMany(f => new[] { 0f, MathF.Sin(f / 60f * 6.283f) * 0.1f, 0f }).ToArray();
        var clip = Clip("Carch|Bob", 2, dense, 3, frames, [], new ClipBindingRaw(Models.ClipReader.Crc32("Carch/MainBone/Pelvis"), 1));

        var keys = ClipSampler.Sample(clip, Skeleton()).Bones.Single().Position!;

        for (var f = 0; f < frames; f++)
        {
            var t = f / 30f;
            var a = keys.Last(k => k.Time <= t + 1e-6f);
            var b = keys.First(k => k.Time >= t - 1e-6f);
            var y = b.Time - a.Time < 1e-6f ? a.Y : a.Y + (b.Y - a.Y) * (t - a.Time) / (b.Time - a.Time);
            Assert.True(MathF.Abs(y - dense[f * 3 + 1]) <= 1.01e-5f, $"frame {f}: {y} vs {dense[f * 3 + 1]}");
        }
        Assert.True(keys.Count < frames);
    }

    [Fact]
    public void Euler_curves_become_quaternions_in_unitys_order()
    {
        // Unity applies Z, then X, then Y: 90° about Y alone is (0, 0.7071, 0, 0.7071).
        var clip = Clip("Carch|Turn", 1, [], 0, 0, [0, 90, 0], new ClipBindingRaw(Models.ClipReader.Crc32("Carch/MainBone"), 4));

        var key = Assert.Single(Assert.Single(ClipSampler.Sample(clip, Skeleton()).Bones).Rotation!);

        Assert.Equal(0.7071f, key.Y, 3);
        Assert.Equal(0.7071f, key.W, 3);
    }

    [Theory]
    // x 90 and z 90: X then Z (an XYZ clip, as models made in Blender or Maya come in) is (0.5, 0.5, 0.5, 0.5);
    // Z then X (Unity's default ZXY) is (0.5, -0.5, 0.5, 0.5).
    [InlineData(0, 0.5f)]
    [InlineData(4, -0.5f)]
    public void Euler_curves_turn_in_the_rotation_order_the_clip_was_made_with(int order, float y)
    {
        var clip = Clip("Camara|LocWalk", 1, [], 0, 0, [90, 0, 90],
            new ClipBindingRaw(Models.ClipReader.Crc32("Carch/MainBone"), 4, RotationOrder: order));

        var key = Assert.Single(Assert.Single(ClipSampler.Sample(clip, Skeleton()).Bones).Rotation!);

        Assert.Equal(0.5f, key.X, 4);
        Assert.Equal(y, key.Y, 4);
        Assert.Equal(0.5f, key.Z, 4);
        Assert.Equal(0.5f, key.W, 4);
    }

    [Fact]
    public void Display_names_drop_the_species_prefix()
    {
        Assert.Equal("LocWalk", ClipSampler.DisplayName("Carch|LocWalk"));
        Assert.Equal("Walk", ClipSampler.DisplayName("Walk"));
    }
}
