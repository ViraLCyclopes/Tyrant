using System.Numerics;
using System.Text.Json;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Models;
using Tyrant.Framework.Core;

namespace Tyrant.Core.ModelReplacements;

/// <summary>One converted level of detail: its .tmesh, its size, and the game LOD's size it replaces.</summary>
public sealed record ModelLodStats(string File, int Vertices, bool Index32, int Vanilla);

/// <summary>What building a model found: errors (then no .tmesh is written), warnings, and each LOD written; saved next to the .glb.</summary>
/// <remarks>Origin: the user's own .glb the model was added from (outside the mod), with its stamp then, to notice a re-export.</remarks>
public sealed record ModelReport(string Source, string Stamp, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, IReadOnlyList<ModelLodStats> Lods,
    string? Origin = null, string? OriginStamp = null);

/// <summary>Turns the user's .glb into one .tmesh per game LOD: their own LOD meshes when present, else LOD 0 decimated.</summary>
public static class ModelBuilder
{
    private static readonly JsonSerializerOptions Json = new(DataStore.ReadableJson) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>"&lt;length&gt;|&lt;last write UTC ticks&gt;" of a file; empty when it does not exist.</summary>
    public static string Stamp(string glbPath)
    {
        var info = new FileInfo(glbPath);
        return info.Exists ? $"{info.Length}|{info.LastWriteTimeUtc.Ticks}" : "";
    }

    public static ModelReport? ReadReport(string modDir, string glbFile)
    {
        var path = Path.Combine(modDir, ModelFiles.Report(glbFile));
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<ModelReport>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True when the model was never built, its .glb changed since, or a built .tmesh is gone.</summary>
    public static bool IsStale(string modDir, string glbFile)
    {
        var report = ReadReport(modDir, glbFile);
        if (report is null || report.Stamp != Stamp(Path.Combine(modDir, glbFile))) return true;
        return report.Errors.Count == 0 && report.Lods.Any(l => !File.Exists(Path.Combine(modDir, l.File)));
    }

    /// <summary>True when the user's own .glb (where the model was added from) changed since then: a re-export to import again.</summary>
    public static bool OriginChanged(ModelReport report) =>
        report.Origin is { } origin && File.Exists(origin) && Stamp(origin) != report.OriginStamp;

    /// <param name="origin">The user's .glb the model is added from; a rebuild keeps the one recorded before.</param>
    public static ModelReport Build(string modDir, string glbFile, PrefabModel game, string? origin = null)
    {
        var glbPath = Path.Combine(modDir, glbFile);
        var stamp = Stamp(glbPath);
        var previous = origin is null ? ReadReport(modDir, glbFile) : null;
        var (from, fromStamp) = origin is not null ? (origin, Stamp(origin)) : (previous?.Origin, previous?.OriginStamp);
        ModelReport Report(IReadOnlyList<string> errors, IReadOnlyList<string> warnings, IReadOnlyList<ModelLodStats> lods) =>
            new(glbFile, stamp, errors, warnings, lods, from, fromStamp);
        var renderers = GameRenderers(game);
        IReadOnlyList<ImportedMesh> imported;
        try
        {
            imported = GlbModelReader.Read(glbPath);
        }
        catch (TyrantException ex)
        {
            return Save(modDir, glbFile, Report([ex.Message], [], []));
        }
        if (renderers.Count == 0)
            return Save(modDir, glbFile, Report(["The game prefab has no skinned mesh to replace."], [], []));

        var errors = new List<string>();
        var warnings = new List<string>();
        var fitted = new List<MeshData>();
        for (var lod = 0; lod < renderers.Count; lod++)
        {
            var game0 = renderers[lod];
            var own = imported.FirstOrDefault(m => m.Lod == lod);
            if (own is not null)
            {
                var fit = ModelFitter.Fit(own, game0, game0.Materials.Select(m => m.Name).ToList());
                errors.AddRange(fit.Errors.Select(e => lod == 0 ? e : $"LOD {lod}: {e}"));
                warnings.AddRange(fit.Warnings.Select(w => lod == 0 ? w : $"LOD {lod}: {w}"));
                if (fit.Mesh is not null) fitted.Add(fit.Mesh);
            }
            else if (lod > 0 && fitted.Count == lod)
            {
                // No mesh of their own for this LOD: decimate LOD 0 to the game's own triangle ratio (seam copies make vertex
                // counts a poor measure of detail).
                var ratio = renderers[0].Mesh.TriangleCount == 0 ? 1.0 : (double)game0.Mesh.TriangleCount / renderers[0].Mesh.TriangleCount;
                fitted.Add(MeshDecimator.Decimate(fitted[0], Math.Max(1, (int)Math.Round(fitted[0].TriangleCount * ratio))));
            }
            else if (lod == 0)
            {
                errors.Add("The .glb has no main mesh (LOD 0): name your mesh without a _LOD1 / _LOD2 suffix.");
                break;
            }
        }
        if (errors.Count > 0 || fitted.Count != renderers.Count)
            return Save(modDir, glbFile, Report(errors.Distinct().ToList(), warnings.Distinct().ToList(), []));

        var lods = new List<ModelLodStats>();
        for (var lod = 0; lod < fitted.Count; lod++)
        {
            var file = ModelFiles.Lod(glbFile, lod);
            var tmesh = ToTMesh(fitted[lod], renderers[lod].Bones.Count, stamp);
            var path = Path.Combine(modDir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(path)) tmesh.Write(stream);
            lods.Add(new ModelLodStats(file, tmesh.VertexCount, tmesh.Index32, renderers[lod].Mesh.VertexCount));
        }
        return Save(modDir, glbFile, Report([], warnings.Distinct().ToList(), lods));
    }

    /// <summary>The prefab's skinned renderers in LOD order: by a _LOD&lt;n&gt; suffix on the renderer or its mesh, then most vertices first.</summary>
    public static IReadOnlyList<RendererModel> GameRenderers(PrefabModel game) =>
        game.Renderers.Where(r => r.IsSkinned)
            .OrderBy(r => GlbModelReader.HasLodSuffix(r.Name) ? GlbModelReader.LodOf(r.Name) : GlbModelReader.LodOf(r.Mesh.Name))
            .ThenByDescending(r => r.Mesh.VertexCount)
            .ToList();

    private static ModelReport Save(string modDir, string glbFile, ModelReport report)
    {
        var path = Path.Combine(modDir, ModelFiles.Report(glbFile));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, Json));
        return report;
    }

    internal static TMesh ToTMesh(MeshData mesh, int boneCount, string stamp)
    {
        var n = mesh.VertexCount;
        var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
        var max = new[] { float.MinValue, float.MinValue, float.MinValue };
        foreach (var p in mesh.Positions)
        {
            min[0] = Math.Min(min[0], p.X); min[1] = Math.Min(min[1], p.Y); min[2] = Math.Min(min[2], p.Z);
            max[0] = Math.Max(max[0], p.X); max[1] = Math.Max(max[1], p.Y); max[2] = Math.Max(max[2], p.Z);
        }
        return new TMesh
        {
            Name = mesh.Name, SourceStamp = stamp, VertexCount = n,
            Positions = mesh.Positions.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
            Normals = mesh.Normals.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
            Uv0 = mesh.Uv0.SelectMany(p => new[] { p.X, p.Y }).ToArray(),
            Colors = mesh.Colors.SelectMany(c => new[] { c.X, c.Y, c.Z, c.W }).ToArray(),
            BoneIndices = mesh.Skin.SelectMany(s => new[] { s.I0, s.I1, s.I2, s.I3 }).ToArray(),
            BoneWeights = mesh.Skin.SelectMany(s => new[] { s.W0, s.W1, s.W2, s.W3 }).ToArray(),
            BoneCount = boneCount,
            Indices = mesh.Indices.Select(i => (int)i).ToArray(),
            SubMeshStarts = mesh.SubMeshes.Select(s => s.FirstIndex).ToArray(),
            SubMeshCounts = mesh.SubMeshes.Select(s => s.IndexCount).ToArray(),
            Index32 = n > ushort.MaxValue,
            Shapes = mesh.BlendShapes.Select(shape =>
            {
                var dp = new float[n * 3];
                var dn = new float[n * 3];
                for (var k = 0; k < shape.VertexIndices.Length; k++)
                {
                    var v = shape.VertexIndices[k];
                    dp[v * 3] = shape.PositionDeltas[k].X; dp[v * 3 + 1] = shape.PositionDeltas[k].Y; dp[v * 3 + 2] = shape.PositionDeltas[k].Z;
                    dn[v * 3] = shape.NormalDeltas[k].X; dn[v * 3 + 1] = shape.NormalDeltas[k].Y; dn[v * 3 + 2] = shape.NormalDeltas[k].Z;
                }
                return new TMeshShape { Name = shape.Name, PositionDeltas = dp, NormalDeltas = dn };
            }).ToArray(),
            BoundsMin = n == 0 ? new float[3] : min,
            BoundsMax = n == 0 ? new float[3] : max,
        };
    }

    /// <summary>A .tmesh back as a mesh (for previews), with the game renderer's bind poses.</summary>
    internal static MeshData FromTMesh(TMesh t, Matrix4x4[] bindPoses)
    {
        var n = t.VertexCount;
        Vector3 V3(float[] a, int i) => new(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]);
        var shapes = t.Shapes.Select(shape =>
        {
            var changed = Enumerable.Range(0, n).Where(i => V3(shape.PositionDeltas, i) != Vector3.Zero || V3(shape.NormalDeltas, i) != Vector3.Zero).ToArray();
            return new BlendShape(shape.Name, changed, changed.Select(i => V3(shape.PositionDeltas, i)).ToArray(), changed.Select(i => V3(shape.NormalDeltas, i)).ToArray());
        }).ToArray();
        return new MeshData
        {
            Name = t.Name,
            Positions = Enumerable.Range(0, n).Select(i => V3(t.Positions, i)).ToArray(),
            Normals = Enumerable.Range(0, n).Select(i => V3(t.Normals, i)).ToArray(),
            Uv0 = t.Uv0.Length == n * 2 ? Enumerable.Range(0, n).Select(i => new Vector2(t.Uv0[i * 2], t.Uv0[i * 2 + 1])).ToArray() : [],
            Colors = t.Colors.Length == n * 4 ? Enumerable.Range(0, n).Select(i => new Vector4(t.Colors[i * 4], t.Colors[i * 4 + 1], t.Colors[i * 4 + 2], t.Colors[i * 4 + 3])).ToArray() : [],
            Skin = t.BoneIndices.Length == n * 4
                ? Enumerable.Range(0, n).Select(i => new BoneWeight4(t.BoneIndices[i * 4], t.BoneIndices[i * 4 + 1], t.BoneIndices[i * 4 + 2], t.BoneIndices[i * 4 + 3],
                    t.BoneWeights[i * 4], t.BoneWeights[i * 4 + 1], t.BoneWeights[i * 4 + 2], t.BoneWeights[i * 4 + 3])).ToArray()
                : [],
            Indices = t.Indices.Select(i => (uint)i).ToArray(),
            SubMeshes = t.SubMeshStarts.Select((start, s) => new SubMesh(start, t.SubMeshCounts[s], 0)).ToArray(),
            BindPoses = bindPoses,
            BlendShapes = shapes,
        };
    }
}
