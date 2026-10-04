using System.Numerics;

namespace Tyrant.Core.Models;

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

/// <summary>A texture a material uses; Archive names the serialized file holding it ("archive:/CAB-…"), null for the material's own file.</summary>
public sealed record TextureSlot(string Slot, string? Archive, long PathId);

/// <summary>A renderer's material: its name and every slot that points at a texture.</summary>
public sealed record MaterialModel(string Name, IReadOnlyList<TextureSlot> Textures)
{
    /// <summary>The material's shader: its file (null = the material's own file) and path id; 0 when it has none.</summary>
    public string? ShaderArchive { get; init; }
    public long ShaderPathId { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public IReadOnlyDictionary<string, float> Floats { get; init; } = new Dictionary<string, float>();
}

/// <summary>One mesh renderer of a prefab; Bones is empty for static (MeshFilter) meshes.</summary>
public sealed record RendererModel(string Name, MeshData Mesh, IReadOnlyList<SkeletonNode> Bones, SkeletonNode Owner)
{
    public bool IsSkinned => Bones.Count > 0;

    /// <summary>One material per sub-mesh, in Unity's order; empty when none could be read (e.g. a bare mesh).</summary>
    public IReadOnlyList<MaterialModel> Materials { get; init; } = [];
}

/// <summary>A prefab's hierarchy and decodable renderers; Failures lists renderers that could not be decoded.</summary>
public sealed record PrefabModel(string Name, SkeletonNode Root, IReadOnlyList<RendererModel> Renderers, IReadOnlyList<string> Failures)
{
    /// <summary>Materials that could not be read; their meshes are kept and drawn plain.</summary>
    public IReadOnlyList<string> MaterialFailures { get; init; } = [];
}
