namespace Tyrant.Core.Animation;

/// <summary>
/// The value of any curve of a clip at any time, as Unity evaluates it: a streamed curve follows the cubic piece of its
/// last key (value = ((c0·dt + c1)·dt + c2)·dt + c3), a dense curve interpolates linearly between its frames, a constant holds.
/// </summary>
public sealed class ClipEvaluator
{
    private readonly RawClip _clip;
    private readonly List<(float Time, float C0, float C1, float C2, float C3)>[] _streamed;

    public ClipEvaluator(RawClip clip)
    {
        _clip = clip;
        _streamed = new List<(float, float, float, float, float)>[clip.StreamedCurves];
        for (var i = 0; i < _streamed.Length; i++) _streamed[i] = [];
        var words = clip.Streamed;
        // Frames of (time, key count, keys of (curve index, 4 coefficients)).
        for (var p = 0; p + 1 < words.Length;)
        {
            var time = BitConverter.UInt32BitsToSingle(words[p]);
            var keys = (int)words[p + 1];
            p += 2;
            for (var k = 0; k < keys && p + 4 < words.Length; k++, p += 5)
            {
                var curve = (int)words[p];
                if (curve < 0 || curve >= _streamed.Length) continue;
                _streamed[curve].Add((time, BitConverter.UInt32BitsToSingle(words[p + 1]), BitConverter.UInt32BitsToSingle(words[p + 2]),
                    BitConverter.UInt32BitsToSingle(words[p + 3]), BitConverter.UInt32BitsToSingle(words[p + 4])));
            }
        }
        foreach (var list in _streamed) list.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    /// <summary>The value of curve <paramref name="curve"/> (streamed, then dense, then constant numbering) at clip time <paramref name="time"/>.</summary>
    public float Value(int curve, float time)
    {
        if (curve < _clip.StreamedCurves) return Streamed(_streamed[curve], time);
        curve -= _clip.StreamedCurves;
        if (curve < _clip.DenseCurves) return Dense(curve, time);
        curve -= _clip.DenseCurves;
        return curve < _clip.Constant.Length ? _clip.Constant[curve] : 0f;
    }

    /// <summary>The finite times of a streamed curve's keys (empty for dense and constant curves).</summary>
    public IReadOnlyList<float> KeyTimes(int curve) =>
        curve < _clip.StreamedCurves ? _streamed[curve].Where(k => float.IsFinite(k.Time)).Select(k => k.Time).ToList() : [];

    private static float Streamed(List<(float Time, float C0, float C1, float C2, float C3)> keys, float time)
    {
        if (keys.Count == 0) return 0f;
        var at = 0;
        for (var i = 0; i < keys.Count && keys[i].Time <= time; i++) at = i;
        var key = keys[at];
        // A leading frame at -infinity (Unity writes one) holds its value until the first finite key.
        var dt = float.IsFinite(key.Time) ? time - key.Time : 0f;
        if (dt < 0f) dt = 0f;
        return ((key.C0 * dt + key.C1) * dt + key.C2) * dt + key.C3;
    }

    private float Dense(int curve, float time)
    {
        var frames = _clip.DenseFrames;
        if (frames <= 0) return 0f;
        var x = Math.Clamp((time - _clip.DenseBeginTime) * _clip.SampleRate, 0f, frames - 1);
        var a = (int)MathF.Floor(x);
        var b = Math.Min(a + 1, frames - 1);
        float At(int frame) => frame * _clip.DenseCurves + curve < _clip.Dense.Length ? _clip.Dense[frame * _clip.DenseCurves + curve] : 0f;
        return At(a) + (At(b) - At(a)) * (x - a);
    }
}
