using System.Numerics;

namespace PK.Core.Models;

/// <summary>A transform in a prefab hierarchy (Unity space).</summary>
public sealed class SkeletonNode(string name, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, SkeletonNode? parent = null)
{
    public string Name { get; } = name;
    public Vector3 LocalPosition { get; } = localPosition;
    public Quaternion LocalRotation { get; } = localRotation;
    public Vector3 LocalScale { get; } = localScale;
    public SkeletonNode? Parent { get; } = parent;
    public List<SkeletonNode> Children { get; } = [];

    /// <summary>Slash-separated names from the root, e.g. "Stego/MainBone/Hips".</summary>
    public string Path => Parent is null ? Name : $"{Parent.Path}/{Name}";

    public IEnumerable<SkeletonNode> DepthFirst()
    {
        var stack = new Stack<SkeletonNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
    }
}

/// <summary>One mesh renderer of a prefab; Bones is empty for static (MeshFilter) meshes.</summary>
public sealed record RendererModel(string Name, MeshData Mesh, IReadOnlyList<SkeletonNode> Bones, SkeletonNode Owner)
{
    public bool IsSkinned => Bones.Count > 0;
}

/// <summary>A prefab's hierarchy and decodable renderers; Failures lists renderers that could not be decoded.</summary>
public sealed record PrefabModel(string Name, SkeletonNode Root, IReadOnlyList<RendererModel> Renderers, IReadOnlyList<string> Failures);
