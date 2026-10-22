using System.Numerics;
using Tyrant.Core.Models;
using Tyrant.Framework.Core;

namespace Tyrant.Core.ModelReplacements;

/// <summary>Where a model's rig edit came from. GameSkeleton: the file's skeleton is the game's. Saved: the file matches the destination's saved rig edit.</summary>
public enum RigSource { GameSkeleton, Bones, RigFile, Saved }

/// <summary>The rig edit found for a model: empty for GameSkeleton, the saved rig edit itself for Saved.</summary>
public sealed record RigReading(IReadOnlyDictionary<string, RigOffset> Rig, RigSource Source, IReadOnlyList<string> Warnings);

/// <summary>
/// The rig edit a model file carries for a destination: its .rig.json when that matches the bones, the destination's saved
/// rig edit when the bones match it, else read from the bones against the game's bind skeleton (moves and turns; a plain
/// export cannot carry scale). Only the renderer's skinned bones are read; other game nodes keep their game local.
/// </summary>
public static class ModelRigReader
{
    /// <summary>Metres: a bone moved less (and turned less than TurnToleranceDegrees) is unchanged. Blender and FBX round trips stay under 0.007 mm.</summary>
    public const float MoveTolerance = 0.0001f;

    /// <summary>Degrees; round trips stay under 0.003°.</summary>
    public const float TurnToleranceDegrees = 0.05f;

    /// <summary>Another species' skeleton turns at least this share of the bones in common, and this many bones, by more than OtherSpeciesDegrees.</summary>
    private const float OtherSpeciesShare = 0.25f;
    private const int OtherSpeciesBones = 10;
    private const float OtherSpeciesDegrees = 5f;

    private const float ToDegrees = 180f / MathF.PI;

    /// <summary>The add-on's Export model for Tyrant… writes the rig edit beside the model as "&lt;file name&gt;.rig.json".</summary>
    public static string? RigFilePathFor(string modelPath)
    {
        var path = modelPath + ".rig.json";
        return File.Exists(path) ? path : null;
    }

    /// <param name="saved">The destination's current rig edit.</param>
    /// <param name="rigFile">The .rig.json beside the model (see RigFilePathFor), named <paramref name="rigFileName"/> in warnings.</param>
    /// <param name="speciesName">The destination's species, for the other-species warning.</param>
    public static RigReading Read(ModelSkeleton file, PrefabModel game, IReadOnlyDictionary<string, RigOffset>? saved,
        IReadOnlyDictionary<string, RigOffset>? rigFile, string speciesName, string? rigFileName = null)
    {
        var warnings = new List<string>();
        if (ModelBuilder.GameRenderers(game) is not [var renderer, ..]) return new RigReading(NoRig(), RigSource.GameSkeleton, warnings);
        var frame = new Frame(file, game, renderer);
        var top = RigBinds.TopBones(renderer);
        if (top.Count > 1)
            warnings.Add($"The game's skeleton has {top.Count} top bones ({Names(top.Select(b => b.Name).ToList())}); the model's frame is taken from '{top[0].Name}', so bones under the others may read as moved.");

        if (rigFile is not null)
        {
            // An empty one ({}) says the model has the game's skeleton (asked like a plain export of it: keep or remove).
            if (frame.Matches(rigFile)) return rigFile.Count == 0 ? new RigReading(NoRig(), RigSource.GameSkeleton, warnings) : new RigReading(rigFile, RigSource.RigFile, warnings);
            warnings.Add($"{rigFileName ?? "The .rig.json"} does not match this model's bones (it may be from an older export), so the rig edit was read from the bones and has no scale.");
        }
        if (saved is { Count: > 0 } && frame.Matches(saved)) return new RigReading(saved, RigSource.Saved, warnings);

        var bones = frame.ReadBones();
        if (frame.Missing.Count == 1)
            warnings.Add($"This model lacks the game's bone '{frame.Missing[0]}', so the rig edit leaves it as the game's.");
        else if (frame.Missing.Count > 1)
            warnings.Add($"This model lacks {frame.Missing.Count} of the game's bones ({Names(frame.Missing)}), so the rig edit leaves them as the game's.");
        var over = bones.Where(b => b.Metres > MoveTolerance || b.Degrees > TurnToleranceDegrees).ToList();
        if (over.Count == 0) return new RigReading(NoRig(), RigSource.GameSkeleton, warnings);

        var rig = NoRig();
        foreach (var bone in over) rig.TryAdd(bone.Name, bone.Offset);
        var scaled = saved?.Where(p => RigVector3.Distance(p.Value.Scale, RigVector3.One) > 1e-6f).Select(p => p.Key).ToList() ?? [];
        if (scaled.Count > 0)
            warnings.Add($"The current rig edit scales {string.Join(", ", scaled)}; reading this model's bones cannot see scale, so using it drops that scale. Export with Export model for Tyrant… in Blender to keep it.");
        var turned = bones.Count(b => b.Degrees > OtherSpeciesDegrees);
        if (turned >= OtherSpeciesBones && turned >= OtherSpeciesShare * bones.Count)
        {
            var cm = (int)Math.Round(over.Max(b => b.Metres) * 100, MidpointRounding.AwayFromZero);
            warnings.Add($"This model's skeleton is very different from {speciesName}'s ({over.Count} bones moved, up to {cm} cm): was it exported from another species' project?");
        }
        return new RigReading(rig, RigSource.Bones, warnings);
    }

    /// <summary>
    /// True when the file's bones sit where the game's skeleton with <paramref name="rig"/> applied in full puts them, and
    /// point the same way: scale's effect on each bone itself is ignored (Blender's rest pose cannot hold it), its effect on
    /// the bones below is not. False when the file has none of the game's bones.
    /// </summary>
    public static bool Matches(ModelSkeleton file, PrefabModel game, IReadOnlyDictionary<string, RigOffset> rig) =>
        ModelBuilder.GameRenderers(game) is [var renderer, ..] && new Frame(file, game, renderer).Matches(rig);

    /// <summary>Two bone worlds at the same place (within MoveTolerance, in metres at <paramref name="metresPerUnit"/>) with their axes the same way.</summary>
    internal static bool SamePlace(Matrix4x4 a, Matrix4x4 b, float metresPerUnit)
    {
        if (Vector3.Distance(a.Translation, b.Translation) * metresPerUnit > MoveTolerance) return false;
        static Vector3 Axis(Matrix4x4 m, int i) => Vector3.Normalize(i == 0 ? new(m.M11, m.M12, m.M13) : i == 1 ? new(m.M21, m.M22, m.M23) : new(m.M31, m.M32, m.M33));
        for (var i = 0; i < 3; i++)
        {
            Vector3 u = Axis(a, i), v = Axis(b, i);
            if (2 * MathF.Atan2((u - v).Length(), (u + v).Length()) * ToDegrees > TurnToleranceDegrees) return false;
        }
        return true;
    }

    private static Dictionary<string, RigOffset> NoRig() => new(StringComparer.Ordinal);

    private static string Names(IReadOnlyList<string> names) => string.Join(", ", names.Take(6).Select(n => $"'{n}'")) + (names.Count > 6 ? ", …" : "");

    /// <summary>The turn of a rotation in degrees (atan2: acos cannot resolve the tolerance in float).</summary>
    private static float Angle(Quaternion q) => 2 * MathF.Atan2(new Vector3(q.X, q.Y, q.Z).Length(), MathF.Abs(q.W)) * ToDegrees;

    /// <summary>The uniform scale of a similarity.</summary>
    private static float Size(Matrix4x4 m) => MathF.Cbrt(MathF.Abs(m.GetDeterminant()));

    private sealed record BoneReading(string Name, RigOffset Offset, float Metres, float Degrees);

    /// <summary>The game's bind skeleton and the file's skinned bones, both in the game's mesh space (row vectors).</summary>
    private sealed class Frame
    {
        private readonly PrefabModel game;
        private readonly RendererModel renderer;
        private readonly Dictionary<SkeletonNode, Matrix4x4> gameWorld;
        private readonly Matrix4x4 expected;

        /// <summary>The file's world (W_f · G⁻¹) of each skinned game bone it has, in the renderer's bone order.</summary>
        private readonly Dictionary<SkeletonNode, Matrix4x4> fileWorld = new(ReferenceEqualityComparer.Instance);

        /// <summary>The renderer's bones the file lacks.</summary>
        public List<string> Missing { get; } = [];

        public Frame(ModelSkeleton file, PrefabModel game, RendererModel renderer)
        {
            this.game = game;
            this.renderer = renderer;
            gameWorld = RigBinds.GameWorlds(game, renderer);
            expected = RigBinds.ExpectedFrame(game, renderer);
            // A reused name: the k-th game bone of that name is the k-th joint of it (the export keeps the renderer's order).
            var joints = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
            for (var j = 0; j < file.JointNames.Count && j < file.InverseBinds.Length; j++)
            {
                if (!joints.TryGetValue(file.JointNames[j], out var queue)) joints[file.JointNames[j]] = queue = new Queue<int>();
                queue.Enqueue(j);
            }
            var raw = new Dictionary<SkeletonNode, Matrix4x4>(ReferenceEqualityComparer.Instance);
            foreach (var bone in renderer.Bones)
            {
                if (raw.ContainsKey(bone)) continue;
                if (joints.TryGetValue(bone.Name, out var queue) && queue.TryDequeue(out var j) && Matrix4x4.Invert(file.InverseBinds[j], out var world))
                    raw[bone] = world;
                else if (!Missing.Contains(bone.Name)) Missing.Add(bone.Name);
            }
            // A glb Tyrant wrote itself (not exported by Blender) keeps the game's bind poses: it is in no frame.
            if (RigBinds.TopBones(renderer) is [var top, ..] && raw.TryGetValue(top, out var topWorld)
                && SamePlace(topWorld, gameWorld[top], 1f) && !SamePlace(topWorld, gameWorld[top] * expected, 1f))
                expected = Matrix4x4.Identity;
            Matrix4x4.Invert(expected, out var unframe);
            foreach (var (bone, world) in raw) fileWorld[bone] = world * unframe;
        }

        public bool Matches(IReadOnlyDictionary<string, RigOffset> rig)
        {
            if (fileWorld.Count == 0) return false;
            var edited = RigBinds.Edited(game, renderer, rig);
            var metresPerUnit = Size(expected);
            for (var i = 0; i < renderer.Bones.Count && i < edited.Length; i++)
                if (fileWorld.TryGetValue(renderer.Bones[i], out var world) && Matrix4x4.Invert(edited[i], out var wanted)
                    && !SamePlace(wanted, world, metresPerUnit)) return false;
            return true;
        }

        /// <summary>
        /// Each skinned bone the file has, as the offset that turns the game's parent-relative transform into the file's
        /// (Rotate = file × game⁻¹, Move = file position − Rotate × game position, Scale 1): RigBinds.Edited's inverse.
        /// A node the file lacks keeps its game local under its parent's file world.
        /// </summary>
        public List<BoneReading> ReadBones()
        {
            var world = new Dictionary<SkeletonNode, Matrix4x4>(ReferenceEqualityComparer.Instance);
            foreach (var node in game.Root.DepthFirst())
            {
                if (fileWorld.TryGetValue(node, out var filed)) world[node] = filed;
                else if (node.Parent is { } parent && Matrix4x4.Invert(gameWorld[parent], out var toParent)) world[node] = gameWorld[node] * toParent * world[parent];
                else world[node] = gameWorld[node];
            }

            var bones = new List<BoneReading>();
            foreach (var bone in fileWorld.Keys)
            {
                var (game0, file0, parentWorld) = (gameWorld[bone], world[bone], Matrix4x4.Identity);
                if (bone.Parent is { } parent && Matrix4x4.Invert(gameWorld[parent], out var gameParent) && Matrix4x4.Invert(world[parent], out var fileParent))
                    (game0, file0, parentWorld) = (game0 * gameParent, file0 * fileParent, gameWorld[parent]);
                Matrix4x4.Decompose(game0, out _, out var gameRotation, out var gamePosition);
                Matrix4x4.Decompose(file0, out _, out var fileRotation, out var filePosition);
                var rotate = Quaternion.Normalize(fileRotation * Quaternion.Conjugate(gameRotation));
                if (rotate.W < 0) rotate = Quaternion.Negate(rotate);
                var move = filePosition - Vector3.Transform(gamePosition, rotate);
                var offset = new RigOffset { Move = move.ToRig(), Rotate = rotate.ToRig(), Scale = RigVector3.One };
                bones.Add(new BoneReading(bone.Name, offset, move.Length() * Size(parentWorld * expected), Angle(rotate)));
            }
            return bones;
        }
    }
}
