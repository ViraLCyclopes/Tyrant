using System.Numerics;
using Tyrant.Core.Models;

namespace Tyrant.Core.ModelReplacements;

/// <summary>
/// Quadric error half-edge collapse (Garland–Heckbert) on welded positions. Vertices at one position (copies split by UV or
/// normal seams) move together: a position collapses onto a neighbouring position only if every copy has a copy of that
/// position next to it in its own UV island, so seams can shorten but never smear across islands. Each collapse keeps the
/// target vertices as they are, so every surviving vertex keeps its own skin weights, UVs, colour and shape-key deltas
/// exactly. Positions on open borders never move.
/// </summary>
public static class MeshDecimator
{
    /// <summary>A collapse may not turn a triangle's normal more than about 78 degrees.</summary>
    private const double FlipLimit = 0.2;

    public static MeshData Decimate(MeshData mesh, int targetVertices)
    {
        var n = mesh.VertexCount;
        if (targetVertices >= n || n == 0) return mesh;

        // Position groups (welded vertices).
        var groupOf = new int[n];
        var groupIds = new Dictionary<Vector3, int>();
        var groupVerts = new List<List<int>>();
        for (var v = 0; v < n; v++)
        {
            if (!groupIds.TryGetValue(mesh.Positions[v], out var g))
            {
                g = groupVerts.Count;
                groupIds[mesh.Positions[v]] = g;
                groupVerts.Add([]);
            }
            groupOf[v] = g;
            groupVerts[g].Add(v);
        }
        var groups = groupVerts.Count;

        // Triangles (vertex indices) with their sub-mesh.
        var tris = new List<(int A, int B, int C, int Sub)>();
        for (var s = 0; s < mesh.SubMeshes.Length; s++)
        {
            var sub = mesh.SubMeshes[s];
            for (var k = 0; k + 2 < sub.IndexCount; k += 3)
                tris.Add(((int)mesh.Indices[sub.FirstIndex + k] + sub.BaseVertex, (int)mesh.Indices[sub.FirstIndex + k + 1] + sub.BaseVertex,
                    (int)mesh.Indices[sub.FirstIndex + k + 2] + sub.BaseVertex, s));
        }
        var aliveTri = Enumerable.Repeat(true, tris.Count).ToArray();
        var vertexTris = Enumerable.Range(0, n).Select(_ => new HashSet<int>()).ToArray();
        for (var t = 0; t < tris.Count; t++)
        {
            vertexTris[tris[t].A].Add(t);
            vertexTris[tris[t].B].Add(t);
            vertexTris[tris[t].C].Add(t);
        }

        // Open borders of the welded surface (an edge between two positions used by one triangle) never move.
        var locked = new bool[groups];
        var edgeUse = new Dictionary<(int, int), int>();
        foreach (var (a, b, c, _) in tris)
            foreach (var e in new[] { Key(groupOf[a], groupOf[b]), Key(groupOf[b], groupOf[c]), Key(groupOf[c], groupOf[a]) })
                edgeUse[e] = edgeUse.GetValueOrDefault(e) + 1;
        foreach (var ((a, b), uses) in edgeUse)
            if (uses == 1) { locked[a] = true; locked[b] = true; }

        // Quadrics per position: the planes of its triangles, weighted by area.
        var q = new Quadric[groups];
        var groupPosition = new Vector3[groups];
        for (var g = 0; g < groups; g++) groupPosition[g] = mesh.Positions[groupVerts[g][0]];
        foreach (var (a, b, c, _) in tris)
        {
            var plane = Quadric.FromTriangle(mesh.Positions[a], mesh.Positions[b], mesh.Positions[c]);
            q[groupOf[a]] += plane;
            q[groupOf[b]] += plane;
            q[groupOf[c]] += plane;
        }

        var deadVertex = new bool[n];
        var deadGroup = new bool[groups];
        var version = new int[groups];
        var queue = new PriorityQueue<(int U, int V, int VersionU, int VersionV), double>();
        void Push(int u, int v)
        {
            if (locked[u] || deadGroup[u] || deadGroup[v] || u == v) return;
            queue.Enqueue((u, v, version[u], version[v]), (q[u] + q[v]).Error(groupPosition[v]));
        }
        foreach (var (a, b) in edgeUse.Keys)
        {
            Push(a, b);
            Push(b, a);
        }

        var alive = n - Enumerable.Range(0, n).Count(v => vertexTris[v].Count == 0); // vertices no triangle uses are dropped anyway
        while (alive > targetVertices && queue.TryDequeue(out var c, out _))
        {
            if (deadGroup[c.U] || deadGroup[c.V] || version[c.U] != c.VersionU || version[c.V] != c.VersionV) continue;
            var map = MapCopies(c.U, c.V);
            if (map is null || Flips(c.U, c.V)) continue;

            foreach (var (cu, cv) in map)
            {
                foreach (var t in vertexTris[cu].ToList())
                {
                    var tri = tris[t];
                    if (groupOf[tri.A] == c.V || groupOf[tri.B] == c.V || groupOf[tri.C] == c.V)
                    {
                        aliveTri[t] = false; // it spanned the collapsed edge
                        vertexTris[tri.A].Remove(t);
                        vertexTris[tri.B].Remove(t);
                        vertexTris[tri.C].Remove(t);
                    }
                    else
                    {
                        tris[t] = (tri.A == cu ? cv : tri.A, tri.B == cu ? cv : tri.B, tri.C == cu ? cv : tri.C, tri.Sub);
                        vertexTris[cv].Add(t);
                    }
                }
                vertexTris[cu].Clear();
                deadVertex[cu] = true;
                alive--;
            }
            groupVerts[c.U].Clear();
            deadGroup[c.U] = true;
            q[c.V] += q[c.U];
            version[c.V]++; // every queued collapse involving v is out of date now
            foreach (var w in Neighbours(c.V))
            {
                Push(c.V, w);
                Push(w, c.V);
            }
        }

        return Compact(mesh, tris, aliveTri);

        // Each copy of position u → the copy of position v next to it (sharing a triangle); null when one copy has none,
        // which would drag its UV island across a seam.
        List<(int From, int To)>? MapCopies(int u, int v)
        {
            var map = new List<(int, int)>();
            foreach (var cu in groupVerts[u])
            {
                if (vertexTris[cu].Count == 0) continue;
                var best = -1;
                var bestDistance = float.MaxValue;
                foreach (var t in vertexTris[cu])
                    foreach (var w in new[] { tris[t].A, tris[t].B, tris[t].C })
                    {
                        if (groupOf[w] != v) continue;
                        var distance = mesh.Uv0.Length == n ? Vector2.DistanceSquared(mesh.Uv0[cu], mesh.Uv0[w]) : 0f;
                        if (distance < bestDistance) (best, bestDistance) = (w, distance);
                    }
                if (best < 0) return null;
                map.Add((cu, best));
            }
            return map.Count == 0 ? null : map;
        }

        bool Flips(int u, int v)
        {
            foreach (var cu in groupVerts[u])
                foreach (var t in vertexTris[cu])
                {
                    var (a, b, cc, _) = tris[t];
                    if (groupOf[a] == v || groupOf[b] == v || groupOf[cc] == v) continue; // removed by the collapse
                    var before = Normal(mesh.Positions[a], mesh.Positions[b], mesh.Positions[cc]);
                    Vector3 P(int i) => groupOf[i] == u ? groupPosition[v] : mesh.Positions[i];
                    var after = Normal(P(a), P(b), P(cc));
                    if (after == Vector3.Zero || Vector3.Dot(before, after) < FlipLimit) return true;
                }
            return false;
        }

        IEnumerable<int> Neighbours(int g) =>
            groupVerts[g].SelectMany(v => vertexTris[v]).SelectMany(t => new[] { tris[t].A, tris[t].B, tris[t].C })
                .Select(w => groupOf[w]).Where(w => w != g).Distinct().ToList();
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    private static Vector3 Normal(Vector3 a, Vector3 b, Vector3 c)
    {
        var cross = Vector3.Cross(b - a, c - a);
        return cross.LengthSquared() < 1e-20f ? Vector3.Zero : Vector3.Normalize(cross);
    }

    /// <summary>The surviving triangles and the vertices they use, renumbered; attributes and shape keys follow the vertices.</summary>
    private static MeshData Compact(MeshData mesh, List<(int A, int B, int C, int Sub)> tris, bool[] aliveTri)
    {
        bool Keep(int t) => aliveTri[t] && tris[t].A != tris[t].B && tris[t].B != tris[t].C && tris[t].A != tris[t].C;
        var used = new bool[mesh.VertexCount];
        for (var t = 0; t < tris.Count; t++)
            if (Keep(t)) { used[tris[t].A] = true; used[tris[t].B] = true; used[tris[t].C] = true; }
        var remap = new int[mesh.VertexCount];
        var kept = new List<int>();
        for (var v = 0; v < mesh.VertexCount; v++)
        {
            remap[v] = used[v] ? kept.Count : -1;
            if (used[v]) kept.Add(v);
        }

        var indices = new List<uint>();
        var subMeshes = new List<SubMesh>();
        for (var s = 0; s < mesh.SubMeshes.Length; s++)
        {
            var first = indices.Count;
            for (var t = 0; t < tris.Count; t++)
            {
                if (tris[t].Sub != s || !Keep(t)) continue;
                indices.Add((uint)remap[tris[t].A]);
                indices.Add((uint)remap[tris[t].B]);
                indices.Add((uint)remap[tris[t].C]);
            }
            subMeshes.Add(new SubMesh(first, indices.Count - first, 0));
        }

        T[] Take<T>(T[] values) => values.Length == 0 ? values : kept.Select(v => values[v]).ToArray();
        var shapes = mesh.BlendShapes.Select(shape =>
        {
            var pairs = shape.VertexIndices.Select((v, k) => (v, k)).Where(p => p.v >= 0 && p.v < remap.Length && remap[p.v] >= 0).ToList();
            return new BlendShape(shape.Name, pairs.Select(p => remap[p.v]).ToArray(),
                pairs.Select(p => shape.PositionDeltas[p.k]).ToArray(), pairs.Select(p => shape.NormalDeltas[p.k]).ToArray());
        }).ToArray();

        return new MeshData
        {
            Name = mesh.Name, Positions = Take(mesh.Positions), Normals = Take(mesh.Normals), Uv0 = Take(mesh.Uv0), Colors = Take(mesh.Colors),
            Skin = Take(mesh.Skin), Indices = [.. indices], SubMeshes = [.. subMeshes], BindPoses = mesh.BindPoses, BlendShapes = shapes,
        };
    }

    /// <summary>A symmetric 4×4 error quadric (10 terms).</summary>
    private readonly record struct Quadric(double A2, double AB, double AC, double AD, double B2, double BC, double BD, double C2, double CD, double D2)
    {
        public static Quadric FromTriangle(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            var cross = Vector3.Cross(p1 - p0, p2 - p0);
            var area = cross.Length();
            if (area < 1e-12f) return default;
            var normal = cross / area;
            double a = normal.X, b = normal.Y, c = normal.Z, d = -Vector3.Dot(normal, p0), w = area;
            return new Quadric(w * a * a, w * a * b, w * a * c, w * a * d, w * b * b, w * b * c, w * b * d, w * c * c, w * c * d, w * d * d);
        }

        public static Quadric operator +(Quadric x, Quadric y) => new(x.A2 + y.A2, x.AB + y.AB, x.AC + y.AC, x.AD + y.AD, x.B2 + y.B2,
            x.BC + y.BC, x.BD + y.BD, x.C2 + y.C2, x.CD + y.CD, x.D2 + y.D2);

        public double Error(Vector3 p)
        {
            double x = p.X, y = p.Y, z = p.Z;
            return A2 * x * x + 2 * AB * x * y + 2 * AC * x * z + 2 * AD * x + B2 * y * y + 2 * BC * y * z + 2 * BD * y + C2 * z * z + 2 * CD * z + D2;
        }
    }
}
