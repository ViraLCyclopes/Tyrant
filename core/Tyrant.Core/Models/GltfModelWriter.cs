using System.Numerics;
using System.Text.Json.Nodes;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace Tyrant.Core.Models;

/// <summary>A sub-mesh's glTF material; its PNGs are linked as textures/&lt;file name&gt; beside the .glb.</summary>
public sealed record GltfMaterial(string Name, string? BaseColorPng = null, string? NormalPng = null)
{
    /// <summary>Alpha below this is cut away (glTF alpha mask); null draws every pixel.</summary>
    public float? Cutoff { get; init; }

    /// <summary>An occlusion (R) / roughness (G) / metallic (B) map, linked as both glTF textures (see OcclusionRoughness).</summary>
    public string? OcclusionRoughnessPng { get; init; }
}

/// <summary>Writes one renderer of a prefab (with the full node hierarchy) as a binary glTF.</summary>
public static class GltfModelWriter
{
    /// <summary>The folder beside each .glb that holds its linked textures; every .glb in a folder shares it.</summary>
    public const string TexturesFolder = "textures";

    /// <param name="materials">One per sub-mesh in Unity's order; sub-meshes beyond the list get a plain material.</param>
    public static void WriteGlb(PrefabModel model, RendererModel renderer, string path, IReadOnlyList<GltfMaterial>? materials = null)
    {
        var nodes = new Dictionary<SkeletonNode, NodeBuilder>();
        var scene = new SceneBuilder();
        scene.AddNode(BuildNodes(model.Root, null, nodes));
        AddRenderer(scene, nodes, renderer, materials);
        Save(scene, path);
    }

    /// <summary>Several renderers of a prefab on one armature in one .glb (Blender projects: LOD0, optionally the far LODs).</summary>
    public static void WriteGlb(PrefabModel model, IReadOnlyList<RendererModel> renderers, string path, Func<RendererModel, IReadOnlyList<GltfMaterial>?>? materials = null)
    {
        var nodes = new Dictionary<SkeletonNode, NodeBuilder>();
        var scene = new SceneBuilder();
        scene.AddNode(BuildNodes(model.Root, null, nodes));
        foreach (var renderer in renderers) AddRenderer(scene, nodes, renderer, materials?.Invoke(renderer));
        Save(scene, path);
    }

    /// <summary>Every renderer of a prefab in one .glb (tests: a file with several meshes, as Blender writes when objects are not joined).</summary>
    internal static void WriteGlbs(PrefabModel model, string path) => WriteGlb(model, model.Renderers, path);

    private static void AddRenderer(SceneBuilder scene, Dictionary<SkeletonNode, NodeBuilder> nodes, RendererModel renderer, IReadOnlyList<GltfMaterial>? materials)
    {
        var mesh = renderer.Mesh;
        if (mesh.Normals.Length != mesh.VertexCount)
            throw new NotSupportedException($"Mesh '{mesh.Name}' has no normals, which is not supported yet.");
        foreach (var (label, count) in new[] { ("UVs", mesh.Uv0.Length), ("colours", mesh.Colors.Length), ("skin weights", mesh.Skin.Length) })
            if (count != 0 && count != mesh.VertexCount)
                throw new InvalidDataException($"Mesh '{mesh.Name}' has {count} {label} for {mesh.VertexCount} vertices.");

        var uvs = SnappedUvs(mesh);
        var fallback = new GltfMaterial(mesh.Name.Length > 0 ? mesh.Name : renderer.Name);
        var built = new Dictionary<GltfMaterial, MaterialBuilder>();
        MaterialBuilder MaterialFor(int subMesh)
        {
            var wanted = materials is not null && subMesh < materials.Count ? materials[subMesh] : fallback;
            if (!built.TryGetValue(wanted, out var made)) built[wanted] = made = Build(wanted);
            return made;
        }

        if (renderer.IsSkinned)
        {
            if (mesh.BindPoses.Length != renderer.Bones.Count)
                throw new InvalidDataException($"Mesh '{mesh.Name}' has {mesh.BindPoses.Length} bind poses for {renderer.Bones.Count} bones.");
            var builder = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>(mesh.Name);
            var jointCount = renderer.Bones.Count;
            VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4> V(int i) =>
                new(Geometry(mesh, i), Material(mesh, uvs, i), Joints(mesh, i, jointCount));
            ForEachTriangle(mesh, (s, a, b, c) => builder.UsePrimitive(MaterialFor(s)).AddTriangle(V(a), V(b), V(c)));
            AddMorphTargets(builder, mesh);
            var joints = renderer.Bones.Select((bone, i) => (nodes[bone], UnityToGltf.Matrix(mesh.BindPoses[i]))).ToArray();
            scene.AddSkinnedMesh(builder, joints);
        }
        else
        {
            var builder = new MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(mesh.Name);
            VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty> V(int i) =>
                new(Geometry(mesh, i), Material(mesh, uvs, i), default(VertexEmpty));
            ForEachTriangle(mesh, (s, a, b, c) => builder.UsePrimitive(MaterialFor(s)).AddTriangle(V(a), V(b), V(c)));
            AddMorphTargets(builder, mesh);
            scene.AddRigidMesh(builder, nodes[renderer.Owner]);
        }
    }

    private static void Save(SceneBuilder scene, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Some prefabs (e.g. Titanoboa) reuse a transform name; glTF allows it and renaming would break bone targets.
        var settings = SceneBuilderSchema2Settings.Default;
        settings.AllowArmatureDuplicatedNames = true;
        // Linked, not embedded: the LODs of one model share one set of PNGs in textures/, written once.
        scene.ToGltf2(settings).SaveGLB(path, new SharpGLTF.Schema2.WriteSettings
        {
            ImageWriting = SharpGLTF.Schema2.ResourceWriteMode.SatelliteFile,
            ImageWriteCallback = (context, uri, image) => WriteOnce(Path.GetDirectoryName(Path.GetFullPath(path))!, context, uri, image),
        });
    }

    /// <summary>A PNG already beside the file (ModelTextures wrote it, or an earlier LOD did) is linked as it is.</summary>
    private static string WriteOnce(string dir, SharpGLTF.Schema2.WriteContext context, string uri, MemoryImage image)
    {
        if (!File.Exists(Path.Combine(dir, uri))) context.WriteAllBytesToEnd(uri, new ArraySegment<byte>(image.Content.ToArray()));
        return uri;
    }

    /// <summary>An explicit dielectric surface: glTF's defaults (metallic 1) render animals as dark metal.</summary>
    private static MaterialBuilder Build(GltfMaterial material)
    {
        var builder = new MaterialBuilder(material.Name).WithMetallicRoughness(0f, 0.85f);
        if (material.BaseColorPng is not null) builder.WithBaseColor(Linked(material.BaseColorPng));
        if (material.NormalPng is not null) builder.WithNormal(Linked(material.NormalPng));
        if (material.Cutoff is { } cutoff) builder.WithAlpha(AlphaMode.MASK, cutoff);
        if (material.OcclusionRoughnessPng is { } orm)
        {
            var image = Linked(orm);
            builder.WithOcclusion(image);
            builder.WithMetallicRoughness(image, 1f, 1f); // the map holds the values: metallic (B) 0, roughness (G)
        }
        return builder;
    }

    private static ImageBuilder Linked(string png)
    {
        var image = ImageBuilder.From(new MemoryImage(png), Path.GetFileNameWithoutExtension(png));
        image.AlternateWriteFileName = $"{TexturesFolder}/{Path.GetFileName(png)}";
        return image;
    }

    private static NodeBuilder BuildNodes(SkeletonNode node, NodeBuilder? parent, Dictionary<SkeletonNode, NodeBuilder> map)
    {
        var builder = parent is null ? new NodeBuilder(node.Name) : parent.CreateNode(node.Name);
        // The prefab root's saved position/rotation is meaningless (Unity overrides it on spawn; some prefabs were
        // saved ~1 km away), so the root sits at the origin; its scale and every child transform are kept.
        builder.LocalTransform = parent is null
            ? new AffineTransform(node.LocalScale, Quaternion.Identity, Vector3.Zero)
            : new AffineTransform(node.LocalScale, UnityToGltf.Rotation(node.LocalRotation), UnityToGltf.Position(node.LocalPosition));
        map[node] = builder;
        foreach (var child in node.Children) BuildNodes(child, builder, map);
        return builder;
    }

    /// <summary>Calls add(subMesh, a, b, c) per triangle with Unity's clockwise winding reversed for glTF.</summary>
    private static void ForEachTriangle(MeshData mesh, Action<int, int, int, int> add)
    {
        for (var s = 0; s < mesh.SubMeshes.Length; s++)
        {
            var sub = mesh.SubMeshes[s];
            for (var k = 0; k + 2 < sub.IndexCount; k += 3)
                add(s, Vertex(mesh, sub, k), Vertex(mesh, sub, k + 2), Vertex(mesh, sub, k + 1));
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

    /// <summary>UVs closer than this (a thousandth of a texel at 4096²) are the same spot on the texture.</summary>
    private const float SameUv = 1e-5f;

    /// <summary>
    /// The mesh's UVs with the copies of a vertex (the game splits vertices for hard edges and tangents) given one UV when
    /// theirs differ by a hair: Blender's Seams from Islands, Tris to Quads and UV selection treat any difference as a seam.
    /// Copies on a real UV seam keep their own UVs.
    /// </summary>
    private static Vector2[] SnappedUvs(MeshData mesh)
    {
        var uvs = mesh.Uv0.ToArray();
        if (uvs.Length != mesh.VertexCount) return uvs;
        foreach (var copies in Enumerable.Range(0, mesh.VertexCount).GroupBy(v => mesh.Positions[v]).Where(g => g.Skip(1).Any()))
        {
            var kept = new List<Vector2>();
            foreach (var v in copies)
            {
                var near = kept.FindIndex(k => Vector2.Distance(k, uvs[v]) < SameUv);
                if (near >= 0) uvs[v] = kept[near];
                else kept.Add(uvs[v]);
            }
        }
        return uvs;
    }

    private static VertexColor1Texture1 Material(MeshData mesh, Vector2[] uvs, int i)
    {
        var color = mesh.Colors.Length > 0 ? Vector4.Clamp(mesh.Colors[i], Vector4.Zero, Vector4.One) : Vector4.One;
        var uv = uvs.Length > 0 ? UnityToGltf.Uv(uvs[i]) : Vector2.Zero;
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
