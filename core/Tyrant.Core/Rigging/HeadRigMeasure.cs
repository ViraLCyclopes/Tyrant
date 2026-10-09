using Tyrant.Core.Models;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Rigging;

/// <summary>
/// Measures how a rig edit changes the species' head-look boom (see <see cref="HeadRig"/>) on the prefab's rest skeleton:
/// the boom base is AnimalSetup.Necks[0] for a quadruped and its parent otherwise, as the game picks it.
/// </summary>
public static class HeadRigMeasure
{
    /// <summary>The correction for this rig edit; null when the edit does not change the boom or the prefab has no AnimalSetup.</summary>
    public static HeadRigCorrection? For(PrefabModel prefab, IReadOnlyDictionary<string, RigOffset>? rig, bool quadrupedal)
    {
        if (rig is not { Count: > 0 } || prefab.Setup is not { } setup) return null;
        var boomBase = quadrupedal ? setup.Necks[0] : setup.Necks[0].Parent;
        if (boomBase?.Parent is not { } baseParent) return null;
        var edited = FirstByName(prefab.Root, rig);
        if (edited.Count == 0) return null;
        RigPose Rest(SkeletonNode n) => World(n, null);
        RigPose Edit(SkeletonNode n) => World(n, edited);
        var correction = HeadRig.Measure(Rest(baseParent), Rest(boomBase).Position, Rest(setup.Head),
            Edit(baseParent), Edit(boomBase).Position, Edit(setup.Head));
        return correction.IsIdentity() ? null : correction;
    }

    /// <summary>The bones whose edit changes the head look: the boom base, the neck between it and the head, and the head.</summary>
    public static IReadOnlyList<string> Bones(PrefabModel prefab, bool quadrupedal)
    {
        if (prefab.Setup is not { } setup) return [];
        var boomBase = quadrupedal ? setup.Necks[0] : setup.Necks[0].Parent;
        var path = new List<string>();
        for (var n = setup.Head; n is not null; n = n.Parent)
        {
            path.Add(n.Name);
            if (ReferenceEquals(n, boomBase)) return path.AsEnumerable().Reverse().ToList();
        }
        return []; // the head is not under the boom base: an unusual rig; nothing to warn about
    }

    /// <summary>The game applies a rig edit to the first bone of each name (depth first), as Tyrant's framework finds them.</summary>
    private static Dictionary<SkeletonNode, RigOffset> FirstByName(SkeletonNode root, IReadOnlyDictionary<string, RigOffset> rig)
    {
        var result = new Dictionary<SkeletonNode, RigOffset>(ReferenceEqualityComparer.Instance);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in root.DepthFirst())
            if (seen.Add(node.Name) && rig.TryGetValue(node.Name, out var offset)) result[node] = offset;
        return result;
    }

    private static RigPose World(SkeletonNode node, Dictionary<SkeletonNode, RigOffset>? edits)
    {
        var position = new RigVector3(node.LocalPosition.X, node.LocalPosition.Y, node.LocalPosition.Z);
        var rotation = new RigQuaternion(node.LocalRotation.X, node.LocalRotation.Y, node.LocalRotation.Z, node.LocalRotation.W);
        var scale = new RigVector3(node.LocalScale.X, node.LocalScale.Y, node.LocalScale.Z);
        if (edits is not null && edits.TryGetValue(node, out var offset)) offset.Apply(ref position, ref rotation, ref scale);
        if (node.Parent is null) return new RigPose(position, rotation, scale);
        var parent = World(node.Parent, edits);
        return new RigPose(parent.Position + parent.Rotation.Rotate(parent.Scale * position), parent.Rotation * rotation, parent.Scale * scale);
    }
}
