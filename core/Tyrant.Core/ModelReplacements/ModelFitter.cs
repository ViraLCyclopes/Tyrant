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

    /// <param name="bindPoses">The edited skeleton's bind poses when the model has a rig edit; null keeps the game's.</param>
    public static FitResult Fit(ImportedMesh imported, RendererModel game, IReadOnlyList<string> gameMaterialNames, Matrix4x4[]? bindPoses = null)
    {
        var binds = bindPoses ?? game.Mesh.BindPoses;
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
        var frame = FileFrame(imported, map, binds);
        Matrix4x4.Invert(frame, out var unframe);
        var skin = KeepGameBones(imported, mesh, map, used, errors, warnings);
        for (var j = 0; j < map.Length; j++)
        {
            if (map[j] < 0 || !used.Contains(j) || map[j] >= binds.Length) continue;
            var inverseBind = unframe * imported.InverseBinds[j]; // as the bone sees the mesh once it is in the game's frame
            if (bindPoses is null && !Near(inverseBind, binds[map[j]]))
            {
                warnings.Add($"The rest pose of bone '{imported.JointNames[j]}' differs from the game's; the game's animations may bend this mesh oddly (keep the armature as exported).");
                break;
            }
            // Blender's rest pose cannot hold a bone's scale, so against a rig edit only places and directions are compared.
            if (bindPoses is not null && !NearIgnoringScale(inverseBind, binds[map[j]]))
            {
                warnings.Add($"The rest pose of bone '{imported.JointNames[j]}' differs from the edited skeleton (rig edit); the game's animations may bend this mesh oddly.");
                break;
            }
        }

        // Shape keys: the game's, by name; the growth keys must be there and should move about as much as the game's.
        var byName = mesh.BlendShapes.GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var shapes = new List<BlendShape>();
        for (var s = 0; s < game.Mesh.BlendShapes.Length; s++)
        {
            var name = game.Mesh.BlendShapes[s].Name;
            if (byName.TryGetValue(name, out var found))
            {
                var shape = ToGameFrame(found, frame);
                shapes.Add(shape);
                if (s < GrowthKeys && MeanDelta(shape) > MeanDelta(game.Mesh.BlendShapes[s]) * GrowthDeltaFactor + 1e-4f)
                    warnings.Add($"Shape key '{name}' moves the mesh much more than the game's, so the young animal will look misshapen. Sculpt or edit (Sculpt Mode or Edit Mode) with the Basis selected in Shape Keys and the growth keys follow it; if you also made the change with a growth key selected, it was applied twice.");
            }
            else if (s < GrowthKeys) errors.Add($"Shape key '{name}' is missing; the game uses it for growth (baby to adult). Keep the shape keys from Tyrant's export (Blender's Voxel Remesh deletes them, so reshape without it).");
            else shapes.Add(new BlendShape(name, [], [], []));
        }
        var dropped = byName.Keys.Except(game.Mesh.BlendShapes.Select(s => s.Name), StringComparer.Ordinal).ToList();
        if (dropped.Count > 0) warnings.Add($"Shape keys {string.Join(", ", dropped.Select(d => $"'{d}'"))} are not the game's and were left out.");

        // Materials: the game's order. Blender adds ".001" to a taken name; an animal has one material, so any name is it.
        var order = new List<int>();
        for (var m = 0; m < imported.MaterialNames.Length; m++)
        {
            var name = GlbModelReader.BaseName(imported.MaterialNames[m]);
            var at = gameMaterialNames.Count == 1 ? 0 : IndexOf(gameMaterialNames, name);
            if (at < 0)
                errors.Add($"Material '{imported.MaterialNames[m]}' is not one of the game's ({string.Join(", ", gameMaterialNames)}): in Blender, select the mesh and press Use game material in the Tyrant panel, or name the material like the game's.");
            order.Add(at);
        }

        if (game.Mesh.VertexCount > 0 && mesh.VertexCount > game.Mesh.VertexCount * HeavyFactor)
            warnings.Add($"This mesh has {mesh.VertexCount:N0} vertices, over {HeavyFactor}× the game's {game.Mesh.VertexCount:N0}; many of these animals can lower the frame rate.");

        if (errors.Count > 0) return new FitResult(null, errors, warnings);

        var mirrored = frame.GetDeterminant() < 0;
        var indices = new List<uint>();
        var ordered = new List<SubMesh>();
        for (var g = 0; g < gameMaterialNames.Count; g++)
        {
            var first = indices.Count;
            for (var m = 0; m < order.Count; m++)
            {
                if (order[m] != g) continue;
                var sub = mesh.SubMeshes[m];
                for (var k = 0; k < sub.IndexCount; k++)
                {
                    // a mirroring frame turns every face inside out unless each triangle's winding turns with it
                    var corner = mirrored && k % 3 != 0 ? k + (k % 3 == 1 ? 1 : -1) : k;
                    indices.Add((uint)(mesh.Indices[sub.FirstIndex + corner] + sub.BaseVertex));
                }
            }
            ordered.Add(new SubMesh(first, indices.Count - first, 0));
        }

        var fitted = new MeshData
        {
            Name = mesh.Name, Positions = Points(mesh.Positions, frame), Normals = Directions(mesh.Normals, frame, normalize: true),
            Uv0 = mesh.Uv0, Colors = mesh.Colors,
            Skin = skin, Indices = [.. indices], SubMeshes = [.. ordered],
            BindPoses = binds, BlendShapes = [.. shapes],
        };
        return new FitResult(fitted, errors, warnings);
    }

    /// <summary>
    /// The frame the file's vertices are in, against the game's mesh, as most of its bones agree on (each bone's bind matrix in
    /// the file against the game's). Blender writes some prefabs' meshes (scaled bind poses: Panthera atrox) upright and
    /// rescaled in its own frame, with bind matrices to match; Maya or a ported mesh can do the same. Identity when as many
    /// bones agree with the game's frame as with any other, so a moved bone or a rig edit never turns the whole mesh.
    /// </summary>
    internal static Matrix4x4 FileFrame(ImportedMesh imported, int[] map, Matrix4x4[] binds)
    {
        var frames = new List<Matrix4x4>();
        for (var j = 0; j < map.Length && j < imported.InverseBinds.Length; j++)
            if (map[j] >= 0 && map[j] < binds.Length && Matrix4x4.Invert(binds[map[j]], out var unbind))
                frames.Add(imported.InverseBinds[j] * unbind);
        var best = Matrix4x4.Identity;
        var most = frames.Count(f => Near(f, Matrix4x4.Identity));
        foreach (var f in frames)
        {
            var count = frames.Count(o => Near(o, f));
            if (count > most) (best, most) = (f, count);
        }
        return best;
    }

    private static Vector3[] Points(Vector3[] points, Matrix4x4 frame) =>
        frame.IsIdentity ? points : [.. points.Select(p => Vector3.Transform(p, frame))];

    /// <summary>Normals (and their shape key deltas) turn with the frame; its scale is left out (uniform: only the turn).</summary>
    private static Vector3[] Directions(Vector3[] directions, Matrix4x4 frame, bool normalize)
    {
        if (frame.IsIdentity) return directions;
        Matrix4x4.Invert(frame, out var inverse);
        var turn = Matrix4x4.Transpose(inverse);
        var scale = MathF.Cbrt(MathF.Abs(turn.GetDeterminant()));
        if (scale > 0) turn = Matrix4x4.Multiply(turn, 1f / scale);
        return [.. directions.Select(d =>
        {
            var t = Vector3.TransformNormal(d, turn);
            return normalize && t.LengthSquared() > 0 ? Vector3.Normalize(t) : t;
        })];
    }

    private static BlendShape ToGameFrame(BlendShape shape, Matrix4x4 frame) => frame.IsIdentity ? shape
        : new BlendShape(shape.Name, shape.VertexIndices, [.. shape.PositionDeltas.Select(d => Vector3.TransformNormal(d, frame))],
            Directions(shape.NormalDeltas, frame, normalize: false));

    private static bool NearIgnoringScale(Matrix4x4 a, Matrix4x4 b)
    {
        if (!Matrix4x4.Invert(a, out var wa) || !Matrix4x4.Invert(b, out var wb)) return false;
        if (Vector3.Distance(wa.Translation, wb.Translation) > RestPoseTolerance) return false;
        static Vector3 Axis(Matrix4x4 m, int i) => Vector3.Normalize(i == 0 ? new(m.M11, m.M12, m.M13) : i == 1 ? new(m.M21, m.M22, m.M23) : new(m.M31, m.M32, m.M33));
        for (var i = 0; i < 3; i++)
            if (Vector3.Distance(Axis(wa, i), Axis(wb, i)) > RestPoseTolerance) return false;
        return true;
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
