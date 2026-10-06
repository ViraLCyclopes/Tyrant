namespace Tyrant.Core.Animation;

/// <summary>One bound channel of a clip: the bone (CRC32 of its path below the Animator) and what it sets.</summary>
/// <param name="Attribute">For a Transform: 1 position, 2 rotation (quaternion), 3 scale, 4 euler degrees.</param>
/// <param name="TypeId">The bound component's class (4: Transform). Any other component's curve is one float Tyrant does not read.</param>
/// <param name="ObjectReference">An object-reference curve (a sprite or material swap): stored apart, no float curve.</param>
/// <param name="RotationOrder">An euler curve's order (Unity's RotationOrder: 0 XYZ, 1 XZY, 2 YZX, 3 YXZ, 4 ZXY, 5 ZYX; the
/// first axis turns first). Clips keep the order they were made with: 0 for many made in Blender or Maya.</param>
public sealed record ClipBindingRaw(uint PathHash, int Attribute, int TypeId = ClipBindingRaw.Transform, bool ObjectReference = false,
    int RotationOrder = ClipBindingRaw.UnityOrder)
{
    public const int Transform = 4;

    /// <summary>Unity's own order (ZXY: Z first, then X, then Y), as Quaternion.Euler.</summary>
    public const int UnityOrder = 4;

    /// <summary>A bone's position, rotation or scale (the channels Tyrant reads).</summary>
    public bool IsBone => TypeId == Transform && !ObjectReference && Attribute is >= 1 and <= 4;

    /// <summary>How many float curves this binding takes, in order.</summary>
    public int Dims => ObjectReference ? 0 : TypeId == Transform ? RawClip.Dims(Attribute) : 1;
}

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
