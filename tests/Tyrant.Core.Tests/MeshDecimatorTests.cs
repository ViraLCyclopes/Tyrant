using System.Numerics;
using Tyrant.Core.Models;
using Tyrant.Core.ModelReplacements;

namespace Tyrant.Core.Tests;

public class MeshDecimatorTests
{
    /// <summary>A bumpy n×n grid (so collapses have a cost), one sub-mesh, skin weights and a shape key on every vertex.</summary>
    private static MeshData Grid(int n, bool seam = false)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        for (var y = 0; y <= n; y++)
            for (var x = 0; x <= n; x++)
            {
                positions.Add(new Vector3(x, MathF.Sin(x * 0.7f) * MathF.Cos(y * 0.5f) * 0.3f, y));
                uvs.Add(new Vector2(x / (float)n, y / (float)n));
            }
        var indices = new List<uint>();
        uint V(int x, int y) => (uint)(y * (n + 1) + x);
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                indices.AddRange([V(x, y), V(x, y + 1), V(x + 1, y)]);
                indices.AddRange([V(x + 1, y), V(x, y + 1), V(x + 1, y + 1)]);
            }
        if (seam)
        {
            // Duplicate the middle column's vertices (a UV seam) and use the copies on the right half.
            var mid = n / 2;
            var copies = new Dictionary<uint, uint>();
            for (var y = 0; y <= n; y++)
            {
                copies[V(mid, y)] = (uint)positions.Count;
                positions.Add(positions[(int)V(mid, y)]);
                uvs.Add(new Vector2(0.99f, y / (float)n));
            }
            for (var t = 0; t < indices.Count; t += 3)
            {
                var right = Enumerable.Range(0, 3).Any(k => indices[t + k] < (n + 1) * (n + 1) && indices[t + k] % (n + 1) > mid);
                if (!right) continue;
                for (var k = 0; k < 3; k++)
                    if (copies.TryGetValue(indices[t + k], out var copy)) indices[t + k] = copy;
            }
        }
        var count = positions.Count;
        return new MeshData
        {
            Name = "Grid", Positions = [.. positions], Normals = Enumerable.Repeat(Vector3.UnitY, count).ToArray(), Uv0 = [.. uvs], Colors = [],
            Skin = Enumerable.Range(0, count).Select(i => new BoneWeight4(i % 3, 0, 0, 0, 1, 0, 0, 0)).ToArray(),
            Indices = [.. indices], SubMeshes = [new SubMesh(0, indices.Count, 0)], BindPoses = [Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity],
            BlendShapes = [new BlendShape("Infant", Enumerable.Range(0, count).ToArray(), Enumerable.Range(0, count).Select(i => new Vector3(0, i, 0)).ToArray(), new Vector3[count])],
        };
    }

    /// <summary>
    /// A bumpy grid cut into vertical UV islands every few columns (like a real animal's many seams): each island has its own
    /// vertices along its edges; the island id is kept in the colour so a test can see triangles that mix islands.
    /// </summary>
    private static MeshData Islands(int n, int every)
    {
        var positions = new List<Vector3>();
        var colors = new List<Vector4>();
        var uvs = new List<Vector2>();
        var index = new Dictionary<(int X, int Y, int Island), uint>();
        uint V(int x, int y, int island)
        {
            if (index.TryGetValue((x, y, island), out var v)) return v;
            positions.Add(new Vector3(x, MathF.Sin(x * 0.7f) * MathF.Cos(y * 0.5f) * 0.3f, y));
            colors.Add(new Vector4(island, 0, 0, 1));
            uvs.Add(new Vector2(island + (x - island * every) / (float)every * 0.9f, y / (float)n));
            return index[(x, y, island)] = (uint)(positions.Count - 1);
        }
        var indices = new List<uint>();
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var island = x / every;
                indices.AddRange([V(x, y, island), V(x, y + 1, island), V(x + 1, y, island)]);
                indices.AddRange([V(x + 1, y, island), V(x, y + 1, island), V(x + 1, y + 1, island)]);
            }
        var count = positions.Count;
        return new MeshData
        {
            Name = "Islands", Positions = [.. positions], Normals = Enumerable.Repeat(Vector3.UnitY, count).ToArray(), Uv0 = [.. uvs], Colors = [.. colors],
            Skin = Enumerable.Range(0, count).Select(i => new BoneWeight4(0, 0, 0, 0, 1, 0, 0, 0)).ToArray(),
            Indices = [.. indices], SubMeshes = [new SubMesh(0, indices.Count, 0)], BindPoses = [Matrix4x4.Identity],
            BlendShapes = [],
        };
    }

    [Fact]
    public void A_mesh_full_of_seams_still_reaches_its_target()
    {
        var mesh = Islands(24, every: 3); // 8 islands: almost every vertex sits on a seam

        var lod = MeshDecimator.Decimate(mesh, mesh.VertexCount / 4);

        Assert.InRange(lod.VertexCount, 1, mesh.VertexCount / 4 * 13 / 10);
    }

    [Fact]
    public void Collapses_never_mix_two_uv_islands_in_one_triangle()
    {
        var lod = MeshDecimator.Decimate(Islands(24, every: 3), 150);

        for (var t = 0; t < lod.Indices.Length; t += 3)
        {
            var islands = Enumerable.Range(0, 3).Select(k => lod.Colors[lod.Indices[t + k]].X).Distinct().Count();
            Assert.Equal(1, islands);
        }
    }

    [Fact]
    public void It_reaches_the_target_vertex_count()
    {
        var mesh = Grid(20); // 441 vertices

        var lod = MeshDecimator.Decimate(mesh, 150);

        Assert.InRange(lod.VertexCount, 100, 165);
        Assert.True(lod.TriangleCount < mesh.TriangleCount);
    }

    [Fact]
    public void Survivors_keep_their_own_weights_uvs_and_shape_deltas()
    {
        var mesh = Grid(20);

        var lod = MeshDecimator.Decimate(mesh, 150);

        for (var v = 0; v < lod.VertexCount; v++)
        {
            var original = Array.IndexOf(mesh.Positions, lod.Positions[v]);
            Assert.True(original >= 0, "every surviving vertex is one of the original vertices");
            Assert.Equal(mesh.Skin[original], lod.Skin[v]);
            Assert.Equal(mesh.Uv0[original], lod.Uv0[v]);
            var shape = lod.BlendShapes[0];
            var k = Array.IndexOf(shape.VertexIndices, v);
            Assert.Equal(mesh.BlendShapes[0].PositionDeltas[original], k >= 0 ? shape.PositionDeltas[k] : Vector3.Zero);
        }
    }

    [Fact]
    public void Borders_stay_and_seams_keep_both_sides()
    {
        var mesh = Grid(20, seam: true);

        var lod = MeshDecimator.Decimate(mesh, 150);

        for (var y = 0; y <= 20; y++)
        {
            Assert.Contains(mesh.Positions[y * 21], lod.Positions); // the open border never moves
            var copies = lod.Positions.Count(p => p == mesh.Positions[y * 21 + 10]);
            Assert.True(copies is 0 or 2, $"seam vertex {y}: {copies} copies (a seam may shorten, but both sides keep their own vertex)");
        }
    }

    [Fact]
    public void No_triangle_is_degenerate_and_every_index_is_valid()
    {
        var lod = MeshDecimator.Decimate(Grid(20), 150);

        Assert.All(Enumerable.Range(0, lod.Indices.Length / 3), t =>
        {
            var (a, b, c) = (lod.Indices[t * 3], lod.Indices[t * 3 + 1], lod.Indices[t * 3 + 2]);
            Assert.True(a != b && b != c && a != c);
            Assert.True(Math.Max(a, Math.Max(b, c)) < lod.VertexCount);
        });
        Assert.Single(lod.SubMeshes);
    }

    [Fact]
    public void A_target_above_the_vertex_count_changes_nothing()
    {
        var mesh = Grid(4);

        Assert.Same(mesh, MeshDecimator.Decimate(mesh, 1000));
    }
}
