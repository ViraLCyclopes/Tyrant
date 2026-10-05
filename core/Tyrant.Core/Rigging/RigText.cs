using System.Globalization;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Rigging;

/// <summary>A rig edit for people: one line per bone with only the parts it changes (rotation as degrees about X, Y, Z).</summary>
public static class RigText
{
    public static string Describe(string bone, RigOffset o)
    {
        var parts = new List<string>();
        if (o.Move.Length() > 1e-6f) parts.Add($"move {N(o.Move.X)}, {N(o.Move.Y)}, {N(o.Move.Z)}");
        if (Math.Abs(RigQuaternion.Dot(o.Rotate, RigQuaternion.Identity)) < 1 - 1e-7f)
        {
            var (x, y, z) = EulerDegrees(o.Rotate);
            parts.Add($"rotate {N(x)}, {N(y)}, {N(z)} deg");
        }
        if (RigVector3.Distance(o.Scale, RigVector3.One) > 1e-6f) parts.Add($"scale {N(o.Scale.X)}, {N(o.Scale.Y)}, {N(o.Scale.Z)}");
        return $"{bone}  {(parts.Count == 0 ? "no change" : string.Join("  ", parts))}";
    }

    /// <summary>Degrees about X, then Y, then Z (for reading only; the rig edit keeps the quaternion).</summary>
    public static (float X, float Y, float Z) EulerDegrees(RigQuaternion q)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W;
        var rx = Math.Atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y));
        var ry = Math.Asin(Math.Clamp(2 * (w * y - z * x), -1, 1));
        var rz = Math.Atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
        const double ToDegrees = 180 / Math.PI;
        return ((float)(rx * ToDegrees), (float)(ry * ToDegrees), (float)(rz * ToDegrees));
    }

    private static string N(float v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
