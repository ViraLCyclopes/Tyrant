using System.Numerics;
using Tyrant.Core.Models;

namespace Tyrant.Core.Blender;

/// <summary>One joint of a chain as Blender sees it (the bone name) and the game's bone length.</summary>
public sealed record BlenderIkJoint(string Name, float Length);

/// <summary>A pull on Joint toward Direction (glTF space, in Bone's frame, normalized): the add-on makes the leg's pole from it.</summary>
public sealed record BlenderIkForce(string Joint, string Bone, float[] Direction, float Strength);

/// <summary>The control bones the add-on builds for a chain: the target (foot, hand, head), its pole (legs), its aim (head).</summary>
public sealed record BlenderIkControls(string Target, string? Pole, string? Look);

/// <summary>
/// A game FABRIK chain for Blender: Kind "limb" or "head", Side "L"/"R" (null for heads), Front for a quadruped's front leg,
/// EndOffset the target point relative to the last joint (glTF space, in that joint's frame).
/// </summary>
public sealed record BlenderIkChain(string Name, string Kind, string? Side, bool Front, IReadOnlyList<BlenderIkJoint> Joints, float Influence,
    float[] EndOffset, IReadOnlyList<BlenderIkForce> Forces, bool MatchHeadRotation, BlenderIkControls Controls);

/// <summary>The project file's "ik": the chains Open in Blender builds into IK controls.</summary>
public sealed record BlenderIk(IReadOnlyList<BlenderIkChain> Chains);

/// <summary>Names the game's FABRIK chains (sides, front/back, control bones) and puts them in glTF space for the add-on.</summary>
public static class BlenderIkReader
{
    public const string LegBend = "the leg's bend";

    /// <summary>The prefab's chains for Blender; null when it has none (objects, animals without IK).</summary>
    public static BlenderIk? From(PrefabModel prefab)
    {
        if (prefab.IkChains.Count == 0) return null;
        var limbs = prefab.IkChains.Where(c => c.Kind == IkChainKind.Limb).ToList();
        var quadruped = limbs.Count == 4;
        // Unity +Z is the animal's forward: the two legs rooted furthest forward are the front legs.
        var front = quadruped ? limbs.OrderByDescending(c => WorldPosition(c.Joints[0].Node).Z).Take(2).ToHashSet() : [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        var controls = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BlenderIkChain>();
        foreach (var chain in prefab.IkChains)
        {
            var head = chain.Kind == IkChainKind.Head;
            var side = head ? null : SideOf(chain);
            var isFront = front.Contains(chain);
            var name = Unique(names, head ? "Head" : $"{(quadruped ? isFront ? "Front leg " : "Back leg " : "Leg ")}{side}", " ");
            var suffix = side is null ? "" : "." + side;
            var (target, pole) = head ? ("head", null) : isFront ? ("hand", (string?)"elbow") : ("foot", "knee");
            var chainControls = new BlenderIkControls(
                Unique(controls, "ctrl_" + target, "", suffix),
                pole is null ? null : Unique(controls, "ctrl_" + pole, "", suffix),
                head ? Unique(controls, "ctrl_look", "") : null);
            result.Add(new BlenderIkChain(name, head ? "head" : "limb", side, isFront,
                chain.Joints.Select(j => new BlenderIkJoint(j.Node.Name, j.Length)).ToList(), chain.Influence,
                Vec(UnityToGltf.Position(chain.EndLocalOffset)),
                chain.Joints.SelectMany(j => j.Forces.Select(f => new BlenderIkForce(j.Node.Name, f.Bone.Name, Vec(UnityToGltf.Position(Direction(f.LocalDirection))), f.Strength))).ToList(),
                head && chain.MatchHeadRotation, chainControls));
        }
        return new BlenderIk(result);
    }

    /// <summary>What a leg's pole comes from, for lists: the bones the knee's (elbow's) forces pull from, else the leg's bend; null for heads.</summary>
    public static string? PoleFrom(BlenderIkChain chain)
    {
        if (chain.Kind != "limb") return null;
        var middle = chain.Joints.Count > 2 ? chain.Joints[1].Name : null;
        var bones = chain.Forces.Where(f => f.Joint == middle).Select(f => f.Bone).Distinct(StringComparer.Ordinal).ToList();
        return bones.Count > 0 ? string.Join(", ", bones) : LegBend;
    }

    /// <summary>"L"/"R" from the joint names (".L", "_L"), else from the side of the body the chain's root is on (Unity +X = the animal's right).</summary>
    private static string SideOf(IkChain chain)
    {
        foreach (var joint in chain.Joints)
        {
            var n = joint.Node.Name;
            if (n.EndsWith(".L", StringComparison.Ordinal) || n.EndsWith("_L", StringComparison.Ordinal)) return "L";
            if (n.EndsWith(".R", StringComparison.Ordinal) || n.EndsWith("_R", StringComparison.Ordinal)) return "R";
        }
        return WorldPosition(chain.Joints[0].Node).X > 0 ? "R" : "L";
    }

    /// <summary>A node's position with the prefab root at the origin (as Tyrant's glTF writer places it).</summary>
    internal static Vector3 WorldPosition(SkeletonNode node)
    {
        var m = Matrix4x4.Identity;
        for (var n = node; n.Parent is not null; n = n.Parent)
            m *= Matrix4x4.CreateScale(n.LocalScale) * Matrix4x4.CreateFromQuaternion(n.LocalRotation) * Matrix4x4.CreateTranslation(n.LocalPosition);
        return m.Translation;
    }

    /// <summary>The game makes a zero direction "forward" (FABRIKJoint.OnForcesChanged).</summary>
    private static Vector3 Direction(Vector3 v) => v.Length() < 1e-6f ? Vector3.UnitZ : Vector3.Normalize(v);

    private static string Unique(HashSet<string> taken, string stem, string separator, string suffix = "")
    {
        var candidate = stem + suffix;
        for (var i = 2; !taken.Add(candidate); i++) candidate = $"{stem}{separator}{i}{suffix}";
        return candidate;
    }

    private static float[] Vec(Vector3 v) => [v.X, v.Y, v.Z];
}
