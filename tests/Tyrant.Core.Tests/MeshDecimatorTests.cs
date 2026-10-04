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

    /// <summary>A smooth closed sphere (no borders, no seams): every collapse has to be paid for in shape.</summary>
    private static MeshData Sphere(int segments, int rings)
    {
        var positions = new List<Vector3> { Vector3.UnitY };
        for (var r = 1; r < rings; r++)
            for (var s = 0; s < segments; s++)
            {
                var (lat, lon) = (MathF.PI * r / rings, 2 * MathF.PI * s / segments);
                positions.Add(new Vector3(MathF.Sin(lat) * MathF.Cos(lon), MathF.Cos(lat), MathF.Sin(lat) * MathF.Sin(lon)));
            }
        positions.Add(-Vector3.UnitY);
        uint V(int r, int s) => r == 0 ? 0u : r == rings ? (uint)(positions.Count - 1) : (uint)(1 + (r - 1) * segments + (s % segments));
        var indices = new List<uint>();
        for (var r = 0; r < rings; r++)
            for (var s = 0; s < segments; s++)
            {
                if (r > 0) indices.AddRange([V(r, s), V(r, s + 1), V(r + 1, s)]);
                if (r < rings - 1) indices.AddRange([V(r, s + 1), V(r + 1, s + 1), V(r + 1, s)]);
            }
        var count = positions.Count;
        return new MeshData
        {
            Name = "Sphere", Positions = [.. positions], Normals = [.. positions], Uv0 = [], Colors = [],
            Skin = Enumerable.Range(0, count).Select(_ => new BoneWeight4(0, 0, 0, 0, 1, 0, 0, 0)).ToArray(),
            Indices = [.. indices], SubMeshes = [new SubMesh(0, indices.Count, 0)], BindPoses = [Matrix4x4.Identity], BlendShapes = [],
        };
    }

    private static float Area(MeshData m) => Enumerable.Range(0, m.Indices.Length / 3).Sum(t =>
        Vector3.Cross(m.Positions[m.Indices[t * 3 + 1]] - m.Positions[m.Indices[t * 3]], m.Positions[m.Indices[t * 3 + 2]] - m.Positions[m.Indices[t * 3]]).Length() / 2);

    [Fact]
    public void It_stops_before_the_shape_is_crushed()
    {
        var sphere = Sphere(32, 16); // 960 triangles

        var lod = MeshDecimator.Decimate(sphere, 8); // an octahedron would keep barely half the surface

        Assert.True(lod.TriangleCount > 8, $"{lod.TriangleCount} triangles");
        Assert.True(Area(lod) >= Area(sphere) * 0.85f, $"kept {Area(lod) / Area(sphere):P0} of the surface");
        Assert.True(lod.TriangleCount < sphere.TriangleCount, "it still simplifies what it can");
    }

    /// <summary>The grid plus two separate pieces: a tiny tetrahedron (a tooth) and a large one (a sail joined without connecting).</summary>
    private static MeshData WithLooseParts()
    {
        var grid = Grid(20);
        var positions = grid.Positions.ToList();
        var indices = grid.Indices.ToList();
        void Tetra(Vector3 at, float size)
        {
            var first = (uint)positions.Count;
            positions.AddRange([at, at + new Vector3(size, 0, 0), at + new Vector3(0, size, 0), at + new Vector3(0, 0, size)]);
            indices.AddRange([first, first + 2, first + 1, first, first + 1, first + 3, first, first + 3, first + 2, first + 1, first + 2, first + 3]);
        }
        Tetra(new Vector3(5, 3, 5), 0.2f);  // under 5% of the ~28-unit grid
        Tetra(new Vector3(10, 3, 10), 6f);  // over 5%
        var count = positions.Count;
        return new MeshData
        {
            Name = "Loose", Positions = [.. positions], Normals = Enumerable.Repeat(Vector3.UnitY, count).ToArray(), Uv0 = [.. grid.Uv0, .. new Vector2[8]],
            Colors = [], Skin = [.. grid.Skin, .. Enumerable.Repeat(new BoneWeight4(0, 0, 0, 0, 1, 0, 0, 0), 8)], Indices = [.. indices],
            SubMeshes = [new SubMesh(0, indices.Count, 0)], BindPoses = grid.BindPoses, BlendShapes = [],
        };
    }

    [Fact]
    public void Small_loose_parts_are_removed_and_large_ones_kept()
    {
        var mesh = WithLooseParts();

        var lod = MeshDecimator.RemoveLooseParts(mesh, 0.05);

        Assert.Equal(mesh.TriangleCount - 4, lod.TriangleCount);
        Assert.DoesNotContain(new Vector3(5, 3, 5), lod.Positions);
        Assert.Contains(new Vector3(10, 3, 10), lod.Positions);
        Assert.Equal(1, MeshDecimator.LooseParts(lod, 1.0)); // only the large piece is left beside the grid
    }

    [Fact]
    public void A_mesh_without_loose_parts_is_left_alone()
    {
        var mesh = Grid(8);

        Assert.Same(mesh, MeshDecimator.RemoveLooseParts(mesh, 0.05));
        Assert.Equal(0, MeshDecimator.LooseParts(mesh, 0.05));
    }

    [Fact]
    public void A_mesh_full_of_seams_still_reaches_its_target()
    {
        var mesh = Islands(24, every: 3); // 8 islands: almost every vertex sits on a seam

        var lod = MeshDecimator.Decimate(mesh, mesh.TriangleCount / 4);

        Assert.InRange(lod.TriangleCount, 1, mesh.TriangleCount / 4);
    }

    [Fact]
    public void Collapses_never_mix_two_uv_islands_in_one_triangle()
    {
        var lod = MeshDecimator.Decimate(Islands(24, every: 3), 250);

        for (var t = 0; t < lod.Indices.Length; t += 3)
        {
            var islands = Enumerable.Range(0, 3).Select(k => lod.Colors[lod.Indices[t + k]].X).Distinct().Count();
            Assert.Equal(1, islands);
        }
    }

    [Fact]
    public void It_reaches_the_target_triangle_count()
    {
        var mesh = Grid(20); // 800 triangles

        var lod = MeshDecimator.Decimate(mesh, 250);

        Assert.InRange(lod.TriangleCount, 200, 250);
        Assert.True(lod.VertexCount < mesh.VertexCount);
    }

    [Fact]
    public void Survivors_keep_their_own_weights_uvs_and_shape_deltas()
    {
        var mesh = Grid(20);

        var lod = MeshDecimator.Decimate(mesh, 250);

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

        var lod = MeshDecimator.Decimate(mesh, 250);

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
        var lod = MeshDecimator.Decimate(Grid(20), 250);

        Assert.All(Enumerable.Range(0, lod.Indices.Length / 3), t =>
        {
            var (a, b, c) = (lod.Indices[t * 3], lod.Indices[t * 3 + 1], lod.Indices[t * 3 + 2]);
            Assert.True(a != b && b != c && a != c);
            Assert.True(Math.Max(a, Math.Max(b, c)) < lod.VertexCount);
        });
        Assert.Single(lod.SubMeshes);
    }

    [Fact]
    public void A_target_above_the_triangle_count_changes_nothing()
    {
        var mesh = Grid(4);

        Assert.Same(mesh, MeshDecimator.Decimate(mesh, 1000));
    }
}
