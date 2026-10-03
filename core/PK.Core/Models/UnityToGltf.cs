using System.Numerics;
using AssetsTools.NET;

namespace PK.Core.Models;

/// <summary>Converts Unity (left-handed, Y up) data to glTF (right-handed, Y up) by mirroring the X axis.</summary>
public static class UnityToGltf
{
    public static Vector3 Position(Vector3 v) => new(-v.X, v.Y, v.Z);

    public static Quaternion Rotation(Quaternion q) => Quaternion.Normalize(new Quaternion(q.X, -q.Y, -q.Z, q.W));

    public static Vector2 Uv(Vector2 uv) => new(uv.X, 1f - uv.Y);

    /// <summary>S·M·S with S = diag(-1, 1, 1, 1).</summary>
    public static Matrix4x4 Matrix(Matrix4x4 m)
    {
        m.M12 = -m.M12; m.M13 = -m.M13; m.M14 = -m.M14;
        m.M21 = -m.M21; m.M31 = -m.M31; m.M41 = -m.M41;
        return m;
    }

    /// <summary>Unity Matrix4x4f (eRC, column vectors) → System.Numerics (row vectors), i.e. transposed.</summary>
    public static Matrix4x4 ReadUnityMatrix(AssetTypeValueField f)
    {
        float E(string n) => f[n].AsFloat;
        return new Matrix4x4(
            E("e00"), E("e10"), E("e20"), E("e30"),
            E("e01"), E("e11"), E("e21"), E("e31"),
            E("e02"), E("e12"), E("e22"), E("e32"),
            E("e03"), E("e13"), E("e23"), E("e33"));
    }

    public static Vector3 ReadVector3(AssetTypeValueField f) => new(f["x"].AsFloat, f["y"].AsFloat, f["z"].AsFloat);

    public static Quaternion ReadQuaternion(AssetTypeValueField f) => new(f["x"].AsFloat, f["y"].AsFloat, f["z"].AsFloat, f["w"].AsFloat);
}
