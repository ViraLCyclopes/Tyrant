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
        for (var j = 0; j < map.Length; j++)
        {
            if (gameBones.TryGetValue(imported.JointNames[j], out var index)) map[j] = index;
            else
            {
                map[j] = -1;
                if (used.Contains(j))
                    errors.Add($"Bone '{imported.JointNames[j]}' is not in the game's skeleton. Keep the bone names from Tyrant's export (new bones are not supported yet).");
            }
        }
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
                    warnings.Add($"Shape key '{name}' moves the mesh much more than the game's, so the young animal will look misshapen. Reshape only the Basis, in Edit Mode: the growth keys follow it. If you also made the change on the growth keys, it was applied twice; if you reshaped outside Edit Mode, the keys did not follow.");
            }
            else if (s < GrowthKeys) errors.Add($"Shape key '{name}' is missing; the game uses it for growth (baby to adult). Keep the shape keys from Tyrant's export.");
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
            Skin = mesh.Skin.Select(s => Remap(s, map)).ToArray(), Indices = [.. indices], SubMeshes = [.. ordered],
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

    private static BoneWeight4 Remap(BoneWeight4 s, int[] map)
    {
        int M(int i, float w) => w > 0 && i >= 0 && i < map.Length && map[i] >= 0 ? map[i] : 0;
        return new BoneWeight4(M(s.I0, s.W0), M(s.I1, s.W1), M(s.I2, s.W2), M(s.I3, s.W3), s.W0, s.W1, s.W2, s.W3);
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
