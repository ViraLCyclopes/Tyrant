using System.Numerics;
using Tyrant.Core.Models;
using Tyrant.Framework.Core;

namespace Tyrant.Core.ModelReplacements;

/// <summary>
/// A rig edit's skeleton: the game's bind skeleton (skinned bones from the renderer's bind poses, other nodes from the
/// prefab's own transforms) with each edited bone's offset composed on its parent-relative transform, as the framework
/// composes it on the animation in game.
/// </summary>
public static class RigBinds
{
    /// <summary>The renderer's bind poses for the edited skeleton (System.Numerics, as MeshData.BindPoses); its own array when nothing changes.</summary>
    public static Matrix4x4[] Edited(PrefabModel model, RendererModel renderer, IReadOnlyDictionary<string, RigOffset>? rig)
    {
        var binds = renderer.Mesh.BindPoses;
        if (rig is null || rig.Count == 0) return binds;
        var index = new Dictionary<SkeletonNode, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < renderer.Bones.Count && i < binds.Length; i++) index.TryAdd(renderer.Bones[i], i);
        Matrix4x4.Invert(PrefabWorld(renderer.Owner), out var meshFromWorld);

        // Bind worlds in mesh space (row vectors): skinned bones from their bind poses, the rest from the prefab's pose.
        var world = new Dictionary<SkeletonNode, Matrix4x4>(ReferenceEqualityComparer.Instance);
        foreach (var node in model.Root.DepthFirst())
        {
            if (index.TryGetValue(node, out var i) && Matrix4x4.Invert(binds[i], out var bound)) world[node] = bound;
            else world[node] = node.Parent is { } parent && world.TryGetValue(parent, out var parentWorld)
                ? Local(node) * parentWorld
                : PrefabWorld(node) * meshFromWorld;
        }

        var edited = new Dictionary<SkeletonNode, Matrix4x4>(ReferenceEqualityComparer.Instance);
        var touched = new HashSet<SkeletonNode>(ReferenceEqualityComparer.Instance);
        foreach (var node in model.Root.DepthFirst())
        {
            var parent = node.Parent;
            var has = rig.TryGetValue(node.Name, out var offset);
            if (!has && (parent is null || !touched.Contains(parent)))
            {
                edited[node] = world[node];
                continue;
            }
            touched.Add(node);
            var local = world[node];
            if (parent is not null && Matrix4x4.Invert(world[parent], out var toParent)) local = world[node] * toParent;
            if (has)
            {
                Matrix4x4.Decompose(local, out var s, out var r, out var t);
                var (p, q, sc) = (t.ToRig(), r.ToRig(), s.ToRig());
                offset.Apply(ref p, ref q, ref sc);
                local = Matrix4x4.CreateScale(sc.ToNumerics()) * Matrix4x4.CreateFromQuaternion(q.ToNumerics()) * Matrix4x4.CreateTranslation(p.ToNumerics());
            }
            edited[node] = parent is null ? local : local * edited[parent];
        }

        var result = (Matrix4x4[])binds.Clone();
        for (var i = 0; i < renderer.Bones.Count && i < result.Length; i++)
            if (touched.Contains(renderer.Bones[i]) && Matrix4x4.Invert(edited[renderer.Bones[i]], out var bind)) result[i] = bind;
        return result;
    }

    /// <summary>
    /// The prefab with the offsets composed on its own transforms (what the game shows when the animation holds the prefab
    /// pose), renderers pointing at the new nodes; the same object when the rig is empty. IK chains keep the old nodes.
    /// </summary>
    public static PrefabModel Apply(PrefabModel model, IReadOnlyDictionary<string, RigOffset>? rig)
    {
        if (rig is null || rig.Count == 0) return model;
        var map = new Dictionary<SkeletonNode, SkeletonNode>(ReferenceEqualityComparer.Instance);
        SkeletonNode Clone(SkeletonNode n, SkeletonNode? parent)
        {
            var position = n.LocalPosition;
            var rotation = n.LocalRotation;
            var scale = n.LocalScale;
            if (rig.TryGetValue(n.Name, out var offset))
            {
                var (p, q, s) = (position.ToRig(), rotation.ToRig(), scale.ToRig());
                offset.Apply(ref p, ref q, ref s);
                (position, rotation, scale) = (p.ToNumerics(), q.ToNumerics(), s.ToNumerics());
            }
            var copy = new SkeletonNode(n.Name, position, rotation, scale, parent);
            map[n] = copy;
            foreach (var child in n.Children) copy.Children.Add(Clone(child, copy));
            return copy;
        }
        var root = Clone(model.Root, null);
        return model with
        {
            Root = root,
            Renderers = model.Renderers.Select(r => r with
            {
                Bones = r.Bones.Select(b => map.GetValueOrDefault(b, b)).ToList(),
                Owner = map.GetValueOrDefault(r.Owner, r.Owner),
            }).ToList(),
        };
    }

    private static Matrix4x4 Local(SkeletonNode n) =>
        Matrix4x4.CreateScale(n.LocalScale) * Matrix4x4.CreateFromQuaternion(n.LocalRotation) * Matrix4x4.CreateTranslation(n.LocalPosition);

    private static Matrix4x4 PrefabWorld(SkeletonNode n) => n.Parent is null ? Local(n) : Local(n) * PrefabWorld(n.Parent);
}

/// <summary>Framework.Core's dependency-free rig types ↔ System.Numerics.</summary>
public static class RigNumerics
{
    public static RigVector3 ToRig(this Vector3 v) => new(v.X, v.Y, v.Z);
    public static RigQuaternion ToRig(this Quaternion q) => new(q.X, q.Y, q.Z, q.W);
    public static Vector3 ToNumerics(this RigVector3 v) => new(v.X, v.Y, v.Z);
    public static Quaternion ToNumerics(this RigQuaternion q) => new(q.X, q.Y, q.Z, q.W);
}
