using System.Numerics;
using System.Text.RegularExpressions;
using SharpGLTF.Schema2;
using Tyrant.Core.Errors;
using Tyrant.Core.Models;

namespace Tyrant.Core.ModelReplacements;

/// <summary>A skinned mesh of the user's .glb, back in Unity space (the inverse of the export's UnityToGltf).</summary>
public sealed record ImportedMesh(int Lod, string Name, MeshData Mesh, string[] JointNames, Matrix4x4[] InverseBinds, string[] MaterialNames);

/// <summary>Reads a Blender-exported .glb: every skinned mesh, its LOD number from its name, its skin and shape keys.</summary>
public static partial class GlbModelReader
{
    [GeneratedRegex(@"_lod0*(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LodSuffix();

    [GeneratedRegex(@"\.\d{3}$")]
    private static partial Regex BlenderCopySuffix();

    /// <summary>A name without the ".001" Blender adds when a name is already taken (importing a file twice).</summary>
    public static string BaseName(string name) => BlenderCopySuffix().Replace(name, "");

    public static int LodOf(string name) => LodSuffix().Match(BaseName(name)) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    public static bool HasLodSuffix(string name) => LodSuffix().IsMatch(BaseName(name));

    public static IReadOnlyList<ImportedMesh> Read(string glbPath)
    {
        ModelRoot model;
        try
        {
            // Images are not needed: a linked texture that is missing is answered with a 1×1 PNG instead of failing.
            var dir = Path.GetDirectoryName(Path.GetFullPath(glbPath))!;
            var context = ReadContext.Create(name =>
            {
                if (string.Equals(name, Path.GetFileName(glbPath), StringComparison.OrdinalIgnoreCase)) return new ArraySegment<byte>(File.ReadAllBytes(glbPath));
                var file = Path.Combine(dir, Uri.UnescapeDataString(name));
                return File.Exists(file) ? new ArraySegment<byte>(File.ReadAllBytes(file)) : new ArraySegment<byte>(OnePixelPng);
            });
            context.Validation = SharpGLTF.Validation.ValidationMode.Skip;
            model = context.ReadSchema2(Path.GetFileName(glbPath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                $"'{Path.GetFileName(glbPath)}' could not be read as a .glb ({ex.Message}). Export it from Blender with File → Export → glTF 2.0 (.glb).");
        }

        var meshes = new List<ImportedMesh>();
        foreach (var node in model.LogicalNodes.Where(n => n.Mesh is not null && n.Skin is not null))
        {
            var name = node.Name is { Length: > 0 } nodeName ? nodeName : node.Mesh.Name ?? "Mesh";
            var lod = HasLodSuffix(name) ? LodOf(name) : LodOf(node.Mesh.Name ?? "");
            meshes.Add(ReadMesh(node, name, lod));
        }
        if (meshes.Count == 0)
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                $"'{Path.GetFileName(glbPath)}' has no mesh skinned to an armature. Keep the game's armature (from Tyrant's export) and parent your mesh to it with Armature Deform.");
        foreach (var group in meshes.GroupBy(m => m.Lod).Where(g => g.Count() > 1))
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                $"'{Path.GetFileName(glbPath)}' has {group.Count()} meshes for LOD {group.Key} ({string.Join(", ", group.Select(m => m.Name))}). Join them in Blender (Ctrl+J) or name the extra ones …_LOD1 / …_LOD2.");
        return meshes.OrderBy(m => m.Lod).ToList();
    }

    private static ImportedMesh ReadMesh(Node node, string name, int lod)
    {
        var skin = node.Skin;
        var joints = Enumerable.Range(0, skin.JointsCount).Select(skin.GetJoint).ToList();
        var jointNames = joints.Select(j => j.Joint.Name ?? "").ToArray();
        var inverseBinds = joints.Select(j => UnityToGltf.Matrix(j.InverseBindMatrix)).ToArray(); // the conversion is its own inverse

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Vector4>();
        var weights = new List<BoneWeight4>();
        var indices = new List<uint>();
        var subMeshes = new List<SubMesh>();
        var materials = new List<string>();
        var targetNames = TargetNames(node.Mesh);
        var shapePositions = targetNames.Select(_ => new List<Vector3>()).ToList();
        var shapeNormals = targetNames.Select(_ => new List<Vector3>()).ToList();
        var anyColors = node.Mesh.Primitives.Any(p => p.GetVertexAccessor("COLOR_0") is not null);

        foreach (var primitive in node.Mesh.Primitives)
        {
            var baseVertex = positions.Count;
            var p = primitive.GetVertexAccessor("POSITION")?.AsVector3Array()
                ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"Mesh '{name}' has a part without vertex positions.");
            var n = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array()
                ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"Mesh '{name}' has no normals; export with Normals ticked (Blender's glTF export, Data → Mesh).");
            var t = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
            var c = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
            var j = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
            var w = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
            for (var i = 0; i < p.Count; i++)
            {
                positions.Add(UnityToGltf.Position(p[i]));
                normals.Add(UnityToGltf.Position(n[i]));
                uvs.Add(t is null ? Vector2.Zero : UnityToGltf.Uv(t[i]));
                if (anyColors) colors.Add(c is null ? Vector4.One : c[i]);
                weights.Add(j is null || w is null ? new BoneWeight4(0, 0, 0, 0, 1, 0, 0, 0)
                    : new BoneWeight4((int)j[i].X, (int)j[i].Y, (int)j[i].Z, (int)j[i].W, w[i].X, w[i].Y, w[i].Z, w[i].W));
            }
            for (var s = 0; s < targetNames.Length; s++)
            {
                var target = s < primitive.MorphTargetsCount ? primitive.GetMorphTargetAccessors(s) : null;
                var dp = target is not null && target.TryGetValue("POSITION", out var pa) ? pa.AsVector3Array() : null;
                var dn = target is not null && target.TryGetValue("NORMAL", out var na) ? na.AsVector3Array() : null;
                for (var i = 0; i < p.Count; i++)
                {
                    shapePositions[s].Add(dp is null ? Vector3.Zero : UnityToGltf.Position(dp[i]));
                    shapeNormals[s].Add(dn is null ? Vector3.Zero : UnityToGltf.Position(dn[i]));
                }
            }
            var first = indices.Count;
            foreach (var (a, b, c2) in primitive.GetTriangleIndices())
            {
                // glTF winding back to Unity's (the export swapped the second and third vertex)
                indices.Add((uint)(a + baseVertex));
                indices.Add((uint)(c2 + baseVertex));
                indices.Add((uint)(b + baseVertex));
            }
            subMeshes.Add(new SubMesh(first, indices.Count - first, 0));
            materials.Add(BaseName(primitive.Material?.Name ?? ""));
        }

        var shapes = targetNames.Select((shapeName, s) =>
        {
            var changed = Enumerable.Range(0, positions.Count).Where(i => shapePositions[s][i] != Vector3.Zero || shapeNormals[s][i] != Vector3.Zero).ToArray();
            return new BlendShape(shapeName, changed, changed.Select(i => shapePositions[s][i]).ToArray(), changed.Select(i => shapeNormals[s][i]).ToArray());
        }).ToArray();

        var mesh = new MeshData
        {
            Name = name, Positions = [.. positions], Normals = [.. normals], Uv0 = [.. uvs], Colors = [.. colors], Skin = [.. weights],
            Indices = [.. indices], SubMeshes = [.. subMeshes], BindPoses = inverseBinds, BlendShapes = shapes,
        };
        return new ImportedMesh(lod, name, mesh, jointNames, inverseBinds, [.. materials]);
    }

    /// <summary>Shape key names: Blender and Tyrant both write them as the mesh's extras "targetNames".</summary>
    private static string[] TargetNames(Mesh mesh)
    {
        var count = mesh.Primitives.Count == 0 ? 0 : mesh.Primitives.Max(p => p.MorphTargetsCount);
        string[] names;
        try
        {
            names = mesh.Extras?["targetNames"]?.AsArray().Select(n => n?.GetValue<string>() ?? "").ToArray() ?? [];
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            names = [];
        }
        return Enumerable.Range(0, count).Select(i => i < names.Length && names[i].Length > 0 ? names[i] : $"Key {i + 1}").ToArray();
    }

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
