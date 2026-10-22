using System.Numerics;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Models;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

/// <summary>A synthetic animal with a rig edit, and the glb Blender would write for it (shared by the rig-reading tests of Core, Rpc and Cli).</summary>
internal static class RigFixture
{
    internal const float Degrees = MathF.PI / 180;

    internal static string TempGlb(string name = "Fixture.glb") => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), name);

    internal static SkeletonNode Node(string name, Vector3 at, SkeletonNode parent, Quaternion? rotation = null, float scale = 1f)
    {
        var node = new SkeletonNode(name, at, rotation ?? Quaternion.Identity, new Vector3(scale), parent);
        parent.Children.Add(node);
        return node;
    }

    internal static Matrix4x4 Local(SkeletonNode n) =>
        Matrix4x4.CreateScale(n.LocalScale) * Matrix4x4.CreateFromQuaternion(n.LocalRotation) * Matrix4x4.CreateTranslation(n.LocalPosition);

    internal static Matrix4x4 Inverse(Matrix4x4 m) => Matrix4x4.Invert(m, out var inverse) ? inverse : throw new InvalidOperationException();

    /// <summary>
    /// A 16-bone animal laid out as the game's are: the root saved far from the origin and turned (the glTF writer drops
    /// that), the armature node "Carch" turned -90° about X (scaled <paramref name="armatureScale"/>), the renderer on a
    /// mesh node beside it, unskinned *_end tips. The hip and a femur are bound turned against the prefab's pose.
    /// The bind poses are in <paramref name="meshFrame"/> (K) against the prefab's frame, so a Blender export of it is in
    /// the frame G = K⁻¹ against the game's mesh.
    /// </summary>
    internal static PrefabModel Game(Matrix4x4? meshFrame = null, float armatureScale = 1f)
    {
        var root = new SkeletonNode("Fixture", new Vector3(1955, 99, 1127), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f), Vector3.One);
        var carch = Node("Carch", Vector3.Zero, root, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -90 * Degrees), armatureScale);
        var meshNode = Node("Fixture_LOD0", Vector3.Zero, root, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -90 * Degrees));
        var main = Node("MainBone", new Vector3(0, 0.05f, 1.2f), carch, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 90 * Degrees));
        var hip = Node("Hip", new Vector3(0, 0.1f, 0), main);
        var neck1 = Node("Neck1", new Vector3(0, 0.3f, 0.4f), hip, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -20 * Degrees));
        var neck2 = Node("Neck2", new Vector3(0, 0.25f, 0.2f), neck1);
        var neck3 = Node("Neck3", new Vector3(0, 0.2f, 0.2f), neck2, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 5 * Degrees));
        var head = Node("Head", new Vector3(0, 0.15f, 0.25f), neck3);
        Node("Head_end", new Vector3(0, 0, 0.3f), head);
        var tail1 = Node("Tail1", new Vector3(0, 0, -0.5f), hip);
        var tail2 = Node("Tail2", new Vector3(0, 0.02f, -0.4f), tail1, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 8 * Degrees));
        var tail3 = Node("Tail3", new Vector3(0, 0, -0.35f), tail2);
        var tail4 = Node("Tail4", new Vector3(0, -0.01f, -0.3f), tail3);
        Node("Tail4_end", new Vector3(0, 0, -0.2f), tail4);
        SkeletonNode[] Leg(string side, float x)
        {
            var femur = Node($"Leg.{side}", new Vector3(x, -0.1f, 0), hip);
            var shin = Node($"Shin.{side}", new Vector3(0, -0.5f, 0.1f), femur);
            var foot = Node($"Foot.{side}", new Vector3(0, -0.45f, -0.1f), shin);
            return [femur, shin, foot];
        }
        var left = Leg("L", 0.25f);
        var right = Leg("R", -0.25f);
        SkeletonNode[] bones = [main, hip, neck1, neck2, neck3, head, tail1, tail2, tail3, tail4, .. left, .. right];

        Matrix4x4 BindLocal(SkeletonNode n) =>
            (n == hip ? Matrix4x4.CreateRotationY(0.25f) * Matrix4x4.CreateRotationX(0.15f)
                : n == left[0] ? Matrix4x4.CreateRotationZ(0.3f) : Matrix4x4.Identity) * Local(n);
        // The prefab's frame (the glTF writer's): the root keeps only its scale.
        Matrix4x4 BindWorld(SkeletonNode n) => n == carch ? Local(carch) : BindLocal(n) * BindWorld(n.Parent!);
        var k = meshFrame ?? Matrix4x4.Identity;
        Matrix4x4[] binds = [.. bones.Select(b => Inverse(BindWorld(b) * k))];
        var renderer = new RendererModel("Fixture_LOD0", ModelFixture.SplitQuad(binds), bones, meshNode);
        return new PrefabModel("Fixture", root, [renderer], []);
    }

    internal static RendererModel Renderer(PrefabModel game) => ModelBuilder.GameRenderers(game)[0];

    internal static Dictionary<string, RigOffset> Rig(params (string Bone, Vector3 Move, Quaternion Rotate, float Scale)[] bones)
    {
        var rig = new Dictionary<string, RigOffset>(StringComparer.Ordinal);
        foreach (var (bone, move, rotate, scale) in bones)
            rig[bone] = new RigOffset { Move = move.ToRig(), Rotate = rotate.ToRig(), Scale = new RigVector3(scale, scale, scale) };
        return rig;
    }

    internal static (string, Vector3, Quaternion, float) Move(string bone, float x, float y, float z) => (bone, new Vector3(x, y, z), Quaternion.Identity, 1f);

    internal static (string, Vector3, Quaternion, float) Turn(string bone, Vector3 axis, float degrees) =>
        (bone, Vector3.Zero, Quaternion.CreateFromAxisAngle(axis, degrees * Degrees), 1f);

    /// <summary>R from the brief: Neck1 moved and turned 10° about X, Tail1 turned -15° about Y.</summary>
    internal static Dictionary<string, RigOffset> R() => Rig(
        ("Neck1", new Vector3(0, 0.05f, 0.02f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 10 * Degrees), 1f),
        Turn("Tail1", Vector3.UnitY, -15));

    /// <summary>
    /// What Blender writes for the game made with a rig edit: the edited bind skeleton moved into its frame (file = edited
    /// world · G, G = K⁻¹), each bone's own rig scale dropped (its rest pose cannot hold it), the uniform scale of the frame
    /// kept. <paramref name="leaveOut"/> bones are not in the file.
    /// </summary>
    internal static string Export(PrefabModel game, IReadOnlyDictionary<string, RigOffset>? rig, Matrix4x4? meshFrame = null, params string[] leaveOut)
    {
        var renderer = Renderer(game);
        var edited = RigBinds.Edited(game, renderer, rig);
        var g = Inverse(meshFrame ?? Matrix4x4.Identity);
        var bones = new List<SkeletonNode>();
        var binds = new List<Matrix4x4>();
        for (var i = 0; i < renderer.Bones.Count; i++)
        {
            if (leaveOut.Contains(renderer.Bones[i].Name)) continue;
            var world = Inverse(edited[i]) * g;
            var unedited = Inverse(renderer.Mesh.BindPoses[i]) * g;
            Matrix4x4.Decompose(world, out _, out var rotation, out var position);
            var size = MathF.Cbrt(MathF.Abs(unedited.GetDeterminant()));
            bones.Add(renderer.Bones[i]);
            binds.Add(Inverse(Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position)));
        }
        var path = TempGlb();
        var file = renderer with { Mesh = ModelFixture.SplitQuad([.. binds]), Bones = bones };
        GltfModelWriter.WriteGlb(game, file, path);
        return path;
    }

    internal static ModelSkeleton Skeleton(string glb, PrefabModel game) => GlbModelReader.ReadSkeleton(glb, game);

    internal static float Angle(Quaternion q) => 2 * MathF.Atan2(new Vector3(q.X, q.Y, q.Z).Length(), MathF.Abs(q.W)) / Degrees;

    internal static void AssertRig(IReadOnlyDictionary<string, RigOffset> expected, IReadOnlyDictionary<string, RigOffset> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (bone, want) in expected)
        {
            var got = actual[bone];
            Assert.True(Vector3.Distance(want.Move.ToNumerics(), got.Move.ToNumerics()) < 1e-4f, $"{bone} move {got.Move} for {want.Move}");
            var difference = got.Rotate.ToNumerics() * Quaternion.Conjugate(want.Rotate.ToNumerics());
            Assert.True(Angle(difference) < 0.01f, $"{bone} rotate {got.Rotate} for {want.Rotate}");
            Assert.Equal(want.Scale, got.Scale);
        }
    }

    internal static Dictionary<string, RigOffset> WithScaledTail()
    {
        var saved = R();
        var tail = saved["Tail1"];
        tail.Scale = new RigVector3(1.3f, 1.3f, 1.3f);
        saved["Tail1"] = tail;
        return saved;
    }
}
