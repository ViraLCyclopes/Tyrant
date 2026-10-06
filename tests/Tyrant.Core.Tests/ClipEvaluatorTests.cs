using Tyrant.Core.Animation;

namespace Tyrant.Core.Tests;

public class ClipEvaluatorTests
{
    private static uint F(float v) => BitConverter.SingleToUInt32Bits(v);

    /// <summary>Streamed words: frames of (time, key count, keys of (curve, c0, c1, c2, c3)); value = ((c0 dt + c1) dt + c2) dt + c3.</summary>
    private static uint[] Streamed(params (float Time, (int Curve, float C0, float C1, float C2, float C3)[] Keys)[] frames) =>
        frames.SelectMany(f => new[] { F(f.Time), (uint)f.Keys.Length }
            .Concat(f.Keys.SelectMany(k => new[] { (uint)k.Curve, F(k.C0), F(k.C1), F(k.C2), F(k.C3) }))).ToArray();

    internal static RawClip Clip(uint[] streamed, int streamedCurves, float[] dense, int denseCurves, int denseFrames, float[] constant,
        IReadOnlyList<ClipBindingRaw>? bindings = null) =>
        new("Test|Walk", 30, 0, 1, true, streamedCurves, streamed, denseCurves, denseFrames, 0, dense, constant, bindings ?? []);

    [Fact]
    public void Streamed_curves_follow_their_cubic_piece_from_each_key()
    {
        // Curve 0: from t=0, 1 + 2 dt; from t=0.5, constant 5. A leading frame at -infinity, as Unity writes one.
        var clip = Clip(Streamed((float.NegativeInfinity, [(0, 0, 0, 0, 9)]), (0f, [(0, 0, 0, 2, 1)]), (0.5f, [(0, 0, 0, 0, 5)])), 1, [], 0, 0, []);
        var e = new ClipEvaluator(clip);

        Assert.Equal(1f, e.Value(0, 0f), 5);
        Assert.Equal(1.5f, e.Value(0, 0.25f), 5);
        Assert.Equal(5f, e.Value(0, 0.75f), 5);
        Assert.Equal([0f, 0.5f], e.KeyTimes(0));
    }

    [Fact]
    public void A_cubic_piece_uses_all_four_coefficients()
    {
        var clip = Clip(Streamed((0f, [(0, 1, 2, 3, 4)])), 1, [], 0, 0, []);

        Assert.Equal(1 * 8 + 2 * 4 + 3 * 2 + 4, new ClipEvaluator(clip).Value(0, 2f), 4);
    }

    [Fact]
    public void Before_the_first_finite_key_the_leading_frame_holds()
    {
        var clip = Clip(Streamed((float.NegativeInfinity, [(0, 0, 0, 0, 9)]), (0.2f, [(0, 0, 0, 0, 1)])), 1, [], 0, 0, []);

        Assert.Equal(9f, new ClipEvaluator(clip).Value(0, 0.1f), 5);
    }

    [Fact]
    public void Dense_curves_interpolate_between_frames_and_constants_hold()
    {
        // 2 dense curves × 3 frames at 30 fps; one constant after them.
        var clip = Clip([], 0, [0, 10, 1, 20, 2, 30], 2, 3, [7]);
        var e = new ClipEvaluator(clip);

        Assert.Equal(0.5f, e.Value(0, 0.5f / 30), 5);
        Assert.Equal(25f, e.Value(1, 1.5f / 30), 5);
        Assert.Equal(2f, e.Value(0, 5f), 5); // past the end: the last frame
        Assert.Equal(7f, e.Value(2, 0.3f), 5);
        Assert.Empty(e.KeyTimes(0));
    }

    [Fact]
    public void Curve_count_spans_the_three_kinds_and_dims_follow_the_attribute()
    {
        var clip = Clip([], 2, [0, 0], 2, 1, [1, 2, 3]);

        Assert.Equal(7, clip.CurveCount);
        Assert.Equal((3, 4, 3, 3, 1), (RawClip.Dims(1), RawClip.Dims(2), RawClip.Dims(3), RawClip.Dims(4), RawClip.Dims(9)));
    }
}
