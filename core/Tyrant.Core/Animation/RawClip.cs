namespace Tyrant.Core.Animation;

/// <summary>One bound channel of a clip: the bone (CRC32 of its path below the Animator) and what it sets.</summary>
/// <param name="Attribute">Unity's generic binding attribute: 1 position, 2 rotation (quaternion), 3 scale, 4 euler degrees; others are one curve Tyrant does not read.</param>
public sealed record ClipBindingRaw(uint PathHash, int Attribute);

/// <summary>
/// A Mecanim clip's curve data as stored: streamed curves (cubic pieces per key), dense curves (frames at the sample
/// rate from DenseBeginTime) and constants, numbered in that order; bindings take their curves in order (Dims each).
/// </summary>
public sealed record RawClip(string Name, float SampleRate, float StartTime, float StopTime, bool Loops,
    int StreamedCurves, uint[] Streamed, int DenseCurves, int DenseFrames, float DenseBeginTime, float[] Dense, float[] Constant,
    IReadOnlyList<ClipBindingRaw> Bindings)
{
    public int CurveCount => StreamedCurves + DenseCurves + Constant.Length;

    public float Length => Math.Max(0f, StopTime - StartTime);

    /// <summary>How many curves a binding of this attribute uses.</summary>
    public static int Dims(int attribute) => attribute switch { 1 => 3, 2 => 4, 3 => 3, 4 => 3, _ => 1 };
}
