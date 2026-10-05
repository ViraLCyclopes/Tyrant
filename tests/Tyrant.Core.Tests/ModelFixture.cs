using System.Numerics;
using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

/// <summary>Synthetic meshes/prefabs. The triangle is front-facing in Unity (clockwise seen against its -Z normal).</summary>
public static class ModelFixture
{
    /// <summary>A quad as the game stores it: two triangles whose shared diagonal is split (different UVs = a seam), a shape key on one corner.</summary>
    public static MeshData SplitQuad(Matrix4x4[] bindPoses) => new()
    {
        Name = "Quad_LOD0",
        Positions = [new(0, 0, 0), new(0, 1, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(1, 0, 0)],
        Normals = [.. Enumerable.Repeat(new Vector3(0, 0, -1), 6)],
        Uv0 = [new(0, 0), new(0, 1), new(1, 0), new(1, 1), new(0.2f, 1), new(1, 0.2f)],
        Colors = [],
        Skin = [.. Enumerable.Repeat(new BoneWeight4(0, 0, 0, 0, 1, 0, 0, 0), 6)],
        Indices = [0, 1, 2, 3, 5, 4],
        SubMeshes = [new SubMesh(0, 6, 0)],
        BindPoses = bindPoses,
        BlendShapes = [new BlendShape("Infant", [3], [new Vector3(0, 0.5f, 0)], [Vector3.Zero])],
    };

    public static MeshData Triangle(string name = "Tri", bool skinned = true, bool withShape = true) => new()
    {
        Name = name,
        Positions = [new(0, 0, 0), new(0, 1, 0), new(1, 0, 0)],
        Normals = [new(0, 0, -1), new(0, 0, -1), new(0, 0, -1)],
        Uv0 = [new(0, 0), new(0, 1), new(1, 0)],
        Colors = [],
        Skin = skinned ? [new(0, 0, 0, 0, 1, 0, 0, 0), new(1, 0, 0, 0, 1, 0, 0, 0), new(0, 1, 0, 0, 0.5f, 0.5f, 0, 0)] : [],
        Indices = [0, 1, 2],
        SubMeshes = [new SubMesh(0, 3, 0)],
        BindPoses = skinned ? [Matrix4x4.Identity, Matrix4x4.CreateTranslation(0, -1, 0)] : [],
        BlendShapes = withShape ? [new BlendShape("Infant", [1], [new Vector3(0, 0.5f, 0)], [Vector3.Zero])] : [],
    };

    public static PrefabModel Prefab(MeshData mesh, bool skinned = true)
    {
        var root = new SkeletonNode("Animal", Vector3.Zero, Quaternion.Identity, Vector3.One);
        var hip = new SkeletonNode("Hip", new Vector3(0, 1, 0), Quaternion.Identity, Vector3.One, root);
        root.Children.Add(hip);
        var tail = new SkeletonNode("Tail", new Vector3(0, 0, -1), Quaternion.Identity, Vector3.One, hip);
        hip.Children.Add(tail);
        var renderer = new RendererModel("Body", mesh, skinned ? [hip, tail] : [], root);
        return new PrefabModel("Animal", root, [renderer], []);
    }
}
