using System.Numerics;
using System.Text.Json.Nodes;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace PK.Core.Models;

/// <summary>Writes one renderer of a prefab (with the full node hierarchy) as a binary glTF.</summary>
public static class GltfModelWriter
{
    public static void WriteGlb(PrefabModel model, RendererModel renderer, string path)
    {
        var mesh = renderer.Mesh;
        if (mesh.Normals.Length != mesh.VertexCount)
            throw new NotSupportedException($"Mesh '{mesh.Name}' has no normals, which is not supported yet.");

        var nodes = new Dictionary<SkeletonNode, NodeBuilder>();
        var root = BuildNodes(model.Root, null, nodes);
        var scene = new SceneBuilder();
        scene.AddNode(root);
        var material = new MaterialBuilder(mesh.Name.Length > 0 ? mesh.Name : renderer.Name);

        if (renderer.IsSkinned)
        {
            if (mesh.BindPoses.Length != renderer.Bones.Count)
                throw new InvalidDataException($"Mesh '{mesh.Name}' has {mesh.BindPoses.Length} bind poses for {renderer.Bones.Count} bones.");
            var builder = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>(mesh.Name);
            var primitive = builder.UsePrimitive(material);
            var jointCount = renderer.Bones.Count;
            VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4> V(int i) =>
                new(Geometry(mesh, i), Material(mesh, i), Joints(mesh, i, jointCount));
            ForEachTriangle(mesh, (a, b, c) => primitive.AddTriangle(V(a), V(b), V(c)));
            AddMorphTargets(builder, mesh);
            var joints = renderer.Bones.Select((bone, i) => (nodes[bone], UnityToGltf.Matrix(mesh.BindPoses[i]))).ToArray();
            scene.AddSkinnedMesh(builder, joints);
        }
        else
        {
            var builder = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(mesh.Name);
            var primitive = builder.UsePrimitive(material);
            VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> V(int i) =>
                new(Geometry(mesh, i), Material(mesh, i), default(VertexEmpty));
            ForEachTriangle(mesh, (a, b, c) => primitive.AddTriangle(V(a), V(b), V(c)));
            AddMorphTargets(builder, mesh);
            scene.AddRigidMesh(builder, nodes[renderer.Owner]);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        scene.ToGltf2().SaveGLB(path);
    }

    private static NodeBuilder BuildNodes(SkeletonNode node, NodeBuilder? parent, Dictionary<SkeletonNode, NodeBuilder> map)
    {
        var builder = parent is null ? new NodeBuilder(node.Name) : parent.CreateNode(node.Name);
        builder.LocalTransform = new AffineTransform(node.LocalScale, UnityToGltf.Rotation(node.LocalRotation), UnityToGltf.Position(node.LocalPosition));
        map[node] = builder;
        foreach (var child in node.Children) BuildNodes(child, builder, map);
        return builder;
    }

    /// <summary>Calls add(a, b, c) per triangle with Unity's clockwise winding reversed for glTF.</summary>
    private static void ForEachTriangle(MeshData mesh, Action<int, int, int> add)
    {
        foreach (var sub in mesh.SubMeshes)
        {
            for (var k = 0; k + 2 < sub.IndexCount; k += 3)
            {
                var a = Vertex(mesh, sub, k);
                var b = Vertex(mesh, sub, k + 1);
                var c = Vertex(mesh, sub, k + 2);
                add(a, c, b);
            }
        }
    }

    private static int Vertex(MeshData mesh, SubMesh sub, int k)
    {
        var v = (long)mesh.Indices[sub.FirstIndex + k] + sub.BaseVertex;
        if (v >= mesh.VertexCount) throw new InvalidDataException($"Mesh '{mesh.Name}' references vertex {v} of {mesh.VertexCount}.");
        return (int)v;
    }

    private static VertexPositionNormal Geometry(MeshData mesh, int i) =>
        new(UnityToGltf.Position(mesh.Positions[i]), SafeNormal(UnityToGltf.Position(mesh.Normals[i])));

    private static VertexColor1Texture1 Material(MeshData mesh, int i)
    {
        var color = mesh.Colors.Length > 0 ? Vector4.Clamp(mesh.Colors[i], Vector4.Zero, Vector4.One) : Vector4.One;
        var uv = mesh.Uv0.Length > 0 ? UnityToGltf.Uv(mesh.Uv0[i]) : Vector2.Zero;
        return new VertexColor1Texture1(color, uv);
    }

    private static VertexJoints4 Joints(MeshData mesh, int i, int jointCount)
    {
        var s = mesh.Skin.Length > 0 ? mesh.Skin[i] : new BoneWeight4(0, 0, 0, 0, 1, 0, 0, 0);
        (int Joint, float Weight)[] pairs = [(s.I0, s.W0), (s.I1, s.W1), (s.I2, s.W2), (s.I3, s.W3)];
        foreach (var (joint, weight) in pairs)
            if (weight > 0 && (joint < 0 || joint >= jointCount))
                throw new InvalidDataException($"Mesh '{mesh.Name}' vertex {i} uses bone {joint} of {jointCount}.");
        var total = pairs.Sum(p => Math.Max(p.Weight, 0));
        return total <= 0
            ? new VertexJoints4((0, 1f))
            : new VertexJoints4(pairs.Select(p => (p.Joint, Math.Max(p.Weight, 0) / total)).ToArray());
    }

    private static void AddMorphTargets<TMaterial, TSkin>(MeshBuilder<VertexPositionNormal, TMaterial, TSkin> builder, MeshData mesh)
        where TMaterial : struct, IVertexMaterial
        where TSkin : struct, IVertexSkinning
    {
        if (mesh.BlendShapes.Length == 0) return;
        var names = new JsonArray();
        for (var s = 0; s < mesh.BlendShapes.Length; s++)
        {
            var shape = mesh.BlendShapes[s];
            var target = builder.UseMorphTarget(s);
            for (var k = 0; k < shape.VertexIndices.Length; k++)
            {
                var baseGeometry = Geometry(mesh, shape.VertexIndices[k]);
                var normal = baseGeometry.Normal + UnityToGltf.Position(shape.NormalDeltas[k]);
                target.SetVertex(baseGeometry, new VertexPositionNormal(baseGeometry.Position + UnityToGltf.Position(shape.PositionDeltas[k]), SafeNormal(normal)));
            }
            names.Add(shape.Name);
        }
        builder.Extras = new JsonObject { ["targetNames"] = names };
    }

    private static Vector3 SafeNormal(Vector3 n) => n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
}
