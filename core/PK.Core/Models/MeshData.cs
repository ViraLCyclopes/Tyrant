using System.Numerics;

namespace PK.Core.Models;

/// <summary>Up to four bone influences of one vertex.</summary>
public readonly record struct BoneWeight4(int I0, int I1, int I2, int I3, float W0, float W1, float W2, float W3)
{
    public static BoneWeight4 From(float[] indices, float[] weights)
    {
        int I(int k) => k < indices.Length ? (int)indices[k] : 0;
        float W(int k) => k < weights.Length ? weights[k] : 0f;
        return new BoneWeight4(I(0), I(1), I(2), I(3), W(0), W(1), W(2), W(3));
    }
}

/// <summary>A range of the index buffer; FirstIndex counts indices, not bytes.</summary>
public sealed record SubMesh(int FirstIndex, int IndexCount, int BaseVertex);

/// <summary>A blend shape at full weight: per-vertex deltas for the listed vertices.</summary>
public sealed record BlendShape(string Name, int[] VertexIndices, Vector3[] PositionDeltas, Vector3[] NormalDeltas);

/// <summary>Engine-neutral mesh, still in Unity's coordinate system.</summary>
public sealed class MeshData
{
    public required string Name { get; init; }
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Vector2[] Uv0 { get; init; }
    public required Vector4[] Colors { get; init; }
    public required BoneWeight4[] Skin { get; init; }
    public required uint[] Indices { get; init; }
    public required SubMesh[] SubMeshes { get; init; }

    /// <summary>Inverse bind matrices in Unity space, System.Numerics layout.</summary>
    public required Matrix4x4[] BindPoses { get; init; }

    public required BlendShape[] BlendShapes { get; init; }

    public int VertexCount => Positions.Length;
    public int TriangleCount => SubMeshes.Sum(s => s.IndexCount) / 3;
}
