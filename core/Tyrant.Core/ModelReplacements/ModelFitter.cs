using System.Numerics;
using Tyrant.Core.Models;

namespace Tyrant.Core.ModelReplacements;

public sealed record FitResult(MeshData? Mesh, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>
/// Makes an imported mesh wear the game renderer's skeleton: joints matched by name and re-indexed into the renderer's bone
/// order, the game's bind poses kept (so its animations drive the mesh), its shape keys and materials in the game's order.
/// </summary>
public static class ModelFitter
{
    /// <summary>AnimalGrowthManager drives shape keys 0 and 1 of every LOD (baby and juvenile).</summary>
    public const int GrowthKeys = 2;

    /// <summary>A mesh with more than this many times the game LOD's vertices is a warning (GPU skinning, shape keys).</summary>
    public const float HeavyFactor = 4f;

    /// <summary>A growth key moving vertices on average this many times further than the game's: the Basis was reshaped without it.</summary>
    public const float GrowthDeltaFactor = 3f;

    private const float RestPoseTolerance = 0.01f;

    public static FitResult Fit(ImportedMesh imported, RendererModel game, IReadOnlyList<string> gameMaterialNames)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var mesh = imported.Mesh;

        // Bones: file joint → game bone index, by name.
        var gameBones = game.Bones.Select((b, i) => (b.Name, i)).GroupBy(b => b.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.Ordinal);
        var used = mesh.Skin.SelectMany(s => new[] { (s.I0, s.W0), (s.I1, s.W1), (s.I2, s.W2), (s.I3, s.W3) })
            .Where(p => p.Item2 > 0).Select(p => p.Item1).ToHashSet();
        var map = new int[imported.JointNames.Length];
        for (var j = 0; j < map.Length; j++) map[j] = gameBones.TryGetValue(imported.JointNames[j], out var index) ? index : -1;
        var skin = KeepGameBones(imported, mesh, map, used, errors, warnings);
        for (var j = 0; j < map.Length; j++)
        {
            if (map[j] < 0 || !used.Contains(j) || map[j] >= game.Mesh.BindPoses.Length) continue;
            if (!Near(imported.InverseBinds[j], game.Mesh.BindPoses[map[j]]))
            {
                warnings.Add($"The rest pose of bone '{imported.JointNames[j]}' differs from the game's; the game's animations may bend this mesh oddly (keep the armature as exported).");
                break;
            }
        }

        // Shape keys: the game's, by name; the growth keys must be there and should move about as much as the game's.
        var byName = mesh.BlendShapes.GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var shapes = new List<BlendShape>();
        for (var s = 0; s < game.Mesh.BlendShapes.Length; s++)
        {
            var name = game.Mesh.BlendShapes[s].Name;
            if (byName.TryGetValue(name, out var shape))
            {
                shapes.Add(shape);
                if (s < GrowthKeys && MeanDelta(shape) > MeanDelta(game.Mesh.BlendShapes[s]) * GrowthDeltaFactor + 1e-4f)
                    warnings.Add($"Shape key '{name}' moves the mesh much more than the game's, so the young animal will look misshapen. Sculpt or edit (Sculpt Mode or Edit Mode) with the Basis selected in Shape Keys and the growth keys follow it; if you also made the change with a growth key selected, it was applied twice.");
            }
            else if (s < GrowthKeys) errors.Add($"Shape key '{name}' is missing; the game uses it for growth (baby to adult). Keep the shape keys from Tyrant's export (Blender's Voxel Remesh deletes them, so reshape without it).");
            else shapes.Add(new BlendShape(name, [], [], []));
        }
        var dropped = byName.Keys.Except(game.Mesh.BlendShapes.Select(s => s.Name), StringComparer.Ordinal).ToList();
        if (dropped.Count > 0) warnings.Add($"Shape keys {string.Join(", ", dropped.Select(d => $"'{d}'"))} are not the game's and were left out.");

        // Materials: the game's order.
        var order = new List<int>();
        for (var m = 0; m < imported.MaterialNames.Length; m++)
        {
            var at = IndexOf(gameMaterialNames, imported.MaterialNames[m]);
            if (at < 0)
                errors.Add($"Material '{imported.MaterialNames[m]}' is not one of the game's ({string.Join(", ", gameMaterialNames)}); keep the materials from Tyrant's export.");
            order.Add(at);
        }

        if (game.Mesh.VertexCount > 0 && mesh.VertexCount > game.Mesh.VertexCount * HeavyFactor)
            warnings.Add($"This mesh has {mesh.VertexCount:N0} vertices, over {HeavyFactor}× the game's {game.Mesh.VertexCount:N0}; many of these animals can lower the frame rate.");

        if (errors.Count > 0) return new FitResult(null, errors, warnings);

        var indices = new List<uint>();
        var ordered = new List<SubMesh>();
        for (var g = 0; g < gameMaterialNames.Count; g++)
        {
            var first = indices.Count;
            for (var m = 0; m < order.Count; m++)
            {
                if (order[m] != g) continue;
                var sub = mesh.SubMeshes[m];
                for (var k = 0; k < sub.IndexCount; k++) indices.Add((uint)(mesh.Indices[sub.FirstIndex + k] + sub.BaseVertex));
            }
            ordered.Add(new SubMesh(first, indices.Count - first, 0));
        }

        var fitted = new MeshData
        {
            Name = mesh.Name, Positions = mesh.Positions, Normals = mesh.Normals, Uv0 = mesh.Uv0, Colors = mesh.Colors,
            Skin = skin, Indices = [.. indices], SubMeshes = [.. ordered],
            BindPoses = game.Mesh.BindPoses, BlendShapes = [.. shapes],
        };
        return new FitResult(fitted, errors, warnings);
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
            if (string.Equals(names[i], name, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>Average distance a shape key moves the vertices it changes.</summary>
    private static float MeanDelta(BlendShape shape) => shape.PositionDeltas.Length == 0 ? 0f : shape.PositionDeltas.Average(d => d.Length());

    /// <summary>Blender's glTF exporter adds this joint for vertices with no weight on the armature's bones.</summary>
    public const string BlenderNeutralBone = "neutral_bone";

    /// <summary>Above this share of vertices with no weight on the game's bones, the mesh is on another skeleton: an error.</summary>
    public const float MaxUnweightedShare = 0.25f;

    /// <summary>
    /// Weights in the game's bone order. Weights on bones the game lacks (leftovers of a ported model, Blender's neutral_bone) are
    /// dropped and each vertex's remaining weight rebalanced; a vertex left with none borrows its nearest weighted neighbour's.
    /// </summary>
    private static BoneWeight4[] KeepGameBones(ImportedMesh imported, MeshData mesh, int[] map, HashSet<int> used, List<string> errors, List<string> warnings)
    {
        var skin = new BoneWeight4[mesh.Skin.Length];
        var empty = new List<int>();
        var neutral = 0;
        for (var v = 0; v < skin.Length; v++)
        {
            var s = mesh.Skin[v];
            var kept = new List<(int Bone, float Weight)>();
            foreach (var (i, w) in new[] { (s.I0, s.W0), (s.I1, s.W1), (s.I2, s.W2), (s.I3, s.W3) })
            {
                if (w <= 0 || i < 0 || i >= map.Length) continue;
                if (map[i] >= 0) kept.Add((map[i], w));
                else if (imported.JointNames[i] == BlenderNeutralBone) neutral++;
            }
            var total = kept.Sum(k => k.Weight);
            if (total <= 0)
            {
                empty.Add(v);
                continue;
            }
            while (kept.Count < 4) kept.Add((0, 0));
            skin[v] = new BoneWeight4(kept[0].Bone, kept[1].Bone, kept[2].Bone, kept[3].Bone,
                kept[0].Weight / total, kept[1].Weight / total, kept[2].Weight / total, kept[3].Weight / total);
        }

        var unknown = map.Select((m, j) => (m, j)).Where(p => p.m < 0 && used.Contains(p.j) && imported.JointNames[p.j] != BlenderNeutralBone)
            .Select(p => imported.JointNames[p.j]).ToList();
        var listed = string.Join(", ", unknown.Take(6)) + (unknown.Count > 6 ? ", …" : "");
        if (empty.Count > skin.Length * MaxUnweightedShare)
        {
            errors.Add($"Most of this mesh is weighted to bones that are not in the game's skeleton{(unknown.Count == 0 ? "" : $" ({listed})")}. Parent it to the game's armature from Tyrant's export (Armature Deform) and weight it to its bones.");
            return skin;
        }
        if (unknown.Count > 0)
            warnings.Add($"Weights on {unknown.Count} bone(s) the game doesn't have ({listed}) were dropped; the rest of each vertex's weight now carries it. Check those parts move well.");
        if (neutral > 0)
            warnings.Add($"{neutral} {(neutral == 1 ? "vertex" : "vertices")} had no weight on the armature in Blender (its exporter's 'neutral_bone'); {(neutral == 1 ? "it now follows its" : "they now follow their")} nearest weighted neighbour.");

        var weighted = Enumerable.Range(0, skin.Length).Except(empty).ToArray();
        foreach (var v in empty)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            foreach (var w in weighted)
            {
                var d = Vector3.DistanceSquared(mesh.Positions[v], mesh.Positions[w]);
                if (d < bestDistance) (best, bestDistance) = (w, d);
            }
            skin[v] = best >= 0 ? skin[best] : new BoneWeight4(0, 0, 0, 0, 1, 0, 0, 0);
        }
        return skin;
    }

    private static bool Near(Matrix4x4 a, Matrix4x4 b)
    {
        var d = a - b;
        var sum = MathF.Abs(d.M11) + MathF.Abs(d.M12) + MathF.Abs(d.M13) + MathF.Abs(d.M14)
                + MathF.Abs(d.M21) + MathF.Abs(d.M22) + MathF.Abs(d.M23) + MathF.Abs(d.M24)
                + MathF.Abs(d.M31) + MathF.Abs(d.M32) + MathF.Abs(d.M33) + MathF.Abs(d.M34)
                + MathF.Abs(d.M41) + MathF.Abs(d.M42) + MathF.Abs(d.M43) + MathF.Abs(d.M44);
        return sum < RestPoseTolerance * 16;
    }
}
