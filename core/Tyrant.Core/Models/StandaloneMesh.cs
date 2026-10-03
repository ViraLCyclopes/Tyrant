using System.Numerics;

namespace Tyrant.Core.Models;

public static class StandaloneMesh
{
    /// <summary>
    /// A Mesh asset on its own, as a model with one static renderer. Without its prefab there are no bones to bind,
    /// so skin weights and bind poses are dropped; blend shapes and UVs are kept.
    /// </summary>
    public static PrefabModel ToPrefab(MeshData mesh)
    {
        var name = mesh.Name.Length > 0 ? mesh.Name : "Mesh";
        var root = new SkeletonNode(name, Vector3.Zero, Quaternion.Identity, Vector3.One);
        var unskinned = new MeshData
        {
            Name = mesh.Name,
            Positions = mesh.Positions,
            Normals = mesh.Normals,
            Uv0 = mesh.Uv0,
            Colors = mesh.Colors,
            Skin = [],
            Indices = mesh.Indices,
            SubMeshes = mesh.SubMeshes,
            BindPoses = [],
            BlendShapes = mesh.BlendShapes,
        };
        return new PrefabModel(name, root, [new RendererModel(name, unskinned, [], root)], []);
    }
}
