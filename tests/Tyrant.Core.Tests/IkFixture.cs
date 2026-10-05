using System.Numerics;
using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

/// <summary>
/// A small two-legged animal with a neck, in Unity space with identity rotations, and the game's FABRIK chains on it: the
/// left leg has a forward force on its knee (relative to Hip), the right leg none, the head matches its target's rotation.
/// "Hip" is a SkinDumps growth bone (baby 0.2 lower), so Growth moves the chains.
/// </summary>
public static class IkFixture
{
    private static SkeletonNode Node(string name, Vector3 at, SkeletonNode? parent)
    {
        var node = new SkeletonNode(name, at, Quaternion.Identity, Vector3.One, parent);
        parent?.Children.Add(node);
        return node;
    }

    private static Matrix4x4 World(SkeletonNode n) => n.Parent is null ? Matrix4x4.Identity
        : Matrix4x4.CreateScale(n.LocalScale) * Matrix4x4.CreateFromQuaternion(n.LocalRotation) * Matrix4x4.CreateTranslation(n.LocalPosition) * World(n.Parent);

    public static PrefabModel Prefab()
    {
        var root = Node("Animal", Vector3.Zero, null);
        var main = Node("MainBone", Vector3.Zero, root);
        var hip = Node("Hip", new Vector3(0, 1.0f, 0), main);
        (SkeletonNode Femur, SkeletonNode Calve, SkeletonNode Foot, SkeletonNode Heel) Leg(string side, float x)
        {
            var femur = Node($"Femur.{side}", new Vector3(x, 0, 0), hip);
            var calve = Node($"Calve.{side}", new Vector3(0, -0.5f, 0.15f), femur);
            var foot = Node($"Foot.{side}", new Vector3(0, -0.4f, -0.25f), calve);
            var heel = Node($"Heel.{side}", new Vector3(0, -0.1f, 0.05f), foot);
            return (femur, calve, foot, heel);
        }
        var left = Leg("L", 0.3f);
        var right = Leg("R", -0.3f);
        var spine = Node("Spine", new Vector3(0, 0.1f, 0.4f), hip);
        var neck = Node("Neck", new Vector3(0, 0.3f, 0.3f), spine);
        var head = Node("Head", new Vector3(0, 0.2f, 0.3f), neck);

        SkeletonNode[] bones = [main, hip, left.Femur, left.Calve, left.Foot, left.Heel, right.Femur, right.Calve, right.Foot, right.Heel, spine, neck, head];
        Matrix4x4[] binds = [.. bones.Select(b => Matrix4x4.Invert(World(b), out var inv) ? inv : Matrix4x4.Identity)];
        var renderer = new RendererModel("Quad_LOD0", ModelFixture.SplitQuad(binds), bones, root)
        {
            Materials = [new MaterialModel("Carch", [new TextureSlot("_AdultDiffuse", null, 1), new TextureSlot("_AdultPatternMask", null, 4)])],
        };

        IkChain LegChain((SkeletonNode Femur, SkeletonNode Calve, SkeletonNode Foot, SkeletonNode Heel) l, IReadOnlyList<IkForce> kneeForces) =>
            new(IkChainKind.Limb, 1f,
                [new IkJoint(l.Femur, 0.52f, []), new IkJoint(l.Calve, 0.47f, kneeForces), new IkJoint(l.Foot, 0.11f, []), new IkJoint(l.Heel, 0f, [])],
                new Vector3(0, -0.05f, 0), false);
        IkChain[] chains =
        [
            LegChain(left, [new IkForce(hip, new Vector3(0, 0, 1), 0.4f)]),
            LegChain(right, []),
            new(IkChainKind.Head, 1f, [new IkJoint(spine, 0.42f, []), new IkJoint(neck, 0.36f, []), new IkJoint(head, 0f, [])], Vector3.Zero, true),
        ];
        return new PrefabModel("Animal", root, [renderer], []) { IkChains = chains };
    }
}
