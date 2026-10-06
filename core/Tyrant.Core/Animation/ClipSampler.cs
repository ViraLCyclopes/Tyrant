using System.Numerics;
using Tyrant.Core.Models;

namespace Tyrant.Core.Animation;

/// <summary>A position or scale key (Unity space, parent-relative).</summary>
public sealed record VecKey(float Time, float X, float Y, float Z);

/// <summary>A rotation key (Unity space, parent-relative; x, y, z, w).</summary>
public sealed record QuatKey(float Time, float X, float Y, float Z, float W);

/// <summary>One bone's keys; a null channel is not animated by the clip.</summary>
public sealed record BoneTrack(string Bone, IReadOnlyList<VecKey>? Position, IReadOnlyList<QuatKey>? Rotation, IReadOnlyList<VecKey>? Scale);

/// <summary>
/// A game clip as per-bone keys: Id is the game's clip name (e.g. "Carch|LocWalk"), Name the shown one ("LocWalk");
/// Travels: its root moves (root motion); Skipped: what could not be used, said once each.
/// </summary>
public sealed record ClipAnimation(string Id, string Name, float FrameRate, float Length, bool Loops, bool Travels,
    IReadOnlyList<BoneTrack> Bones, IReadOnlyList<string> Skipped);

/// <summary>A species' animation as listed (no keys): its id, shown name, length in seconds, frame rate, looping and travel.</summary>
public sealed record AnimationInfo(string Id, string Name, float Length, float FrameRate, bool Loops, bool Travels);

/// <summary>Samples a clip's curves into per-bone keys at its frame rate, then drops keys a straight line reproduces.</summary>
public static class ClipSampler
{
    public const float PositionTolerance = 1e-5f; // 0.01 mm
    public const float RotationTolerance = 0.01f * MathF.PI / 180f; // 0.01°
    public const float ScaleTolerance = 1e-4f;

    /// <summary>
    /// A position moving more than this (metres) on a bone at most two levels below the animator's object counts as travel
    /// (the game's MainBone sits under the armature's own node: Carcharodontosaurus.V2/Carch/MainBone).
    /// </summary>
    public const float TravelThreshold = 0.05f;

    public static string DisplayName(string id) => id.Contains('|') ? id[(id.LastIndexOf('|') + 1)..] : id;

    /// <summary>What the animation list shows, without sampling: travel from the channel ranges of the animator's first-level bones.</summary>
    public static AnimationInfo Info(RawClip clip, SkeletonNode prefabRoot)
    {
        var animatorRoot = ClipReader.AnimatorRoot(clip.Bindings.Select(b => b.PathHash), prefabRoot);
        var nodes = ClipReader.PathHashes(animatorRoot);
        var travels = ClipReader.Channels(clip).Bindings.Any(b => b.Attribute == 1 && b.Range > TravelThreshold
            && nodes.TryGetValue(b.PathHash, out var node) && IsTop(node, animatorRoot));
        return new AnimationInfo(clip.Name, DisplayName(clip.Name), clip.Length, clip.SampleRate > 0 ? clip.SampleRate : 30f, clip.Loops, travels);
    }

    public static ClipAnimation Sample(RawClip clip, SkeletonNode prefabRoot)
    {
        var evaluator = new ClipEvaluator(clip);
        var animatorRoot = ClipReader.AnimatorRoot(clip.Bindings.Select(b => b.PathHash), prefabRoot);
        var nodes = ClipReader.PathHashes(animatorRoot);
        var rate = clip.SampleRate > 0 ? clip.SampleRate : 30f;
        var length = clip.Length;
        var count = Math.Max(1, (int)MathF.Round(length * rate) + 1);
        var times = Enumerable.Range(0, count).Select(i => Math.Min(i / rate, length)).ToArray();
        var constantStart = clip.StreamedCurves + clip.DenseCurves;

        var tracks = new Dictionary<string, (List<VecKey>? P, List<QuatKey>? R, List<VecKey>? S)>(StringComparer.Ordinal);
        var order = new List<string>();
        var skipped = new List<string>();
        var travels = false;
        var offset = 0;
        foreach (var binding in clip.Bindings)
        {
            var dims = RawClip.Dims(binding.Attribute);
            var first = offset;
            offset += dims;
            if (binding.Attribute is < 1 or > 4)
            {
                Note(skipped, $"curve kind {binding.Attribute} is not read (only bone position, rotation and scale)");
                continue;
            }
            if (!nodes.TryGetValue(binding.PathHash, out var node))
            {
                Note(skipped, $"a bone not on this skeleton (path hash {binding.PathHash:x8}) is skipped");
                continue;
            }
            var constant = first >= constantStart;
            var at = constant ? [0f] : times;
            float V(int d, float t) => evaluator.Value(first + d, clip.StartTime + t);
            if (!tracks.TryGetValue(node.Name, out var track)) order.Add(node.Name);
            switch (binding.Attribute)
            {
                case 1:
                    var positions = at.Select(t => new VecKey(t, V(0, t), V(1, t), V(2, t))).ToList();
                    if (IsTop(node, animatorRoot) && Range(positions) > TravelThreshold) travels = true;
                    track.P = Thin(positions, PositionTolerance);
                    break;
                case 3:
                    track.S = Thin(at.Select(t => new VecKey(t, V(0, t), V(1, t), V(2, t))).ToList(), ScaleTolerance);
                    break;
                case 2:
                    track.R = Thin(at.Select(t => Normalised(t, new Quaternion(V(0, t), V(1, t), V(2, t), V(3, t)))).ToList());
                    break;
                case 4:
                    track.R = Thin(at.Select(t => Normalised(t, Euler(V(0, t), V(1, t), V(2, t)))).ToList());
                    break;
            }
            tracks[node.Name] = track;
        }
        var bones = order.Select(n => new BoneTrack(n, tracks[n].P, tracks[n].R, tracks[n].S)).ToList();
        return new ClipAnimation(clip.Name, DisplayName(clip.Name), rate, length, clip.Loops, travels, bones, skipped);
    }

    private static bool IsTop(SkeletonNode node, SkeletonNode animatorRoot) =>
        node.Parent == animatorRoot || node.Parent?.Parent == animatorRoot;

    private static void Note(List<string> notes, string note)
    {
        if (!notes.Contains(note)) notes.Add(note);
    }

    /// <summary>The diagonal of the box the positions span.</summary>
    private static float Range(List<VecKey> keys) =>
        new Vector3(keys.Max(k => k.X) - keys.Min(k => k.X), keys.Max(k => k.Y) - keys.Min(k => k.Y), keys.Max(k => k.Z) - keys.Min(k => k.Z)).Length();

    /// <summary>Unity's Quaternion.Euler (degrees): Z first, then X, then Y.</summary>
    private static Quaternion Euler(float x, float y, float z)
    {
        const float d = MathF.PI / 180f;
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, x * d);
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, y * d);
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, z * d);
        return qy * qx * qz;
    }

    private static QuatKey Normalised(float t, Quaternion q)
    {
        var length = q.Length();
        if (length < 1e-8f || float.IsNaN(length)) return new QuatKey(t, 0, 0, 0, 1);
        q = Quaternion.Multiply(q, 1f / length);
        return new QuatKey(t, q.X, q.Y, q.Z, q.W);
    }

    /// <summary>Keeps the first and last key and each key a straight line from the last kept one cannot reproduce within tolerance.</summary>
    private static List<VecKey> Thin(List<VecKey> keys, float tolerance)
    {
        static Vector3 V(VecKey k) => new(k.X, k.Y, k.Z);
        return Thin(keys, (a, b, k) =>
        {
            var u = (k.Time - a.Time) / (b.Time - a.Time);
            return Vector3.Distance(Vector3.Lerp(V(a), V(b), u), V(k)) <= tolerance;
        }, (a, b) => Vector3.Distance(V(a), V(b)) <= tolerance);
    }

    private static List<QuatKey> Thin(List<QuatKey> keys)
    {
        static Quaternion Q(QuatKey k) => new(k.X, k.Y, k.Z, k.W);
        return Thin(keys, (a, b, k) =>
        {
            var qa = Q(a);
            var qb = Q(b);
            if (Quaternion.Dot(qa, qb) < 0) qb = -qb;
            var u = (k.Time - a.Time) / (b.Time - a.Time);
            var lerped = Quaternion.Normalize(Quaternion.Lerp(qa, qb, u));
            var dot = MathF.Min(1f, MathF.Abs(Quaternion.Dot(lerped, Q(k))));
            return 2f * MathF.Acos(dot) <= RotationTolerance;
        }, (a, b) => 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(Q(a), Q(b))))) <= RotationTolerance);
    }

    /// <param name="fits">Whether key k lies within tolerance of the line from a to b.</param>
    /// <param name="same">Whether two keys are equal within tolerance.</param>
    private static List<T> Thin<T>(List<T> keys, Func<T, T, T, bool> fits, Func<T, T, bool> same)
    {
        // A channel that never changes is one key.
        if (keys.Count > 1 && keys.All(k => same(keys[0], k))) return [keys[0]];
        if (keys.Count <= 2) return keys;
        var kept = new List<T> { keys[0] };
        var anchor = 0;
        for (var i = 2; i < keys.Count; i++)
        {
            var all = true;
            for (var k = anchor + 1; k < i && all; k++) all = fits(keys[anchor], keys[i], keys[k]);
            if (all) continue;
            kept.Add(keys[i - 1]);
            anchor = i - 1;
        }
        kept.Add(keys[^1]);
        return kept;
    }
}
