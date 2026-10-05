using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tyrant.Framework.Core
{
    /// <summary>A 3D vector (Unity space) without a dependency: this library loads inside the game.</summary>
    public struct RigVector3 : IEquatable<RigVector3>
    {
        public float X, Y, Z;

        public RigVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static RigVector3 Zero => new RigVector3(0, 0, 0);
        public static RigVector3 One => new RigVector3(1, 1, 1);

        public static RigVector3 operator +(RigVector3 a, RigVector3 b) => new RigVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static RigVector3 operator -(RigVector3 a, RigVector3 b) => new RigVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static RigVector3 operator *(RigVector3 a, RigVector3 b) => new RigVector3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public static RigVector3 operator /(RigVector3 a, RigVector3 b) => new RigVector3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
        public static bool operator ==(RigVector3 a, RigVector3 b) => a.Equals(b);
        public static bool operator !=(RigVector3 a, RigVector3 b) => !a.Equals(b);

        public float Length() => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public static float Distance(RigVector3 a, RigVector3 b) => (a - b).Length();
        public bool Equals(RigVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is RigVector3 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 7919);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>A rotation (x, y, z, w; Unity space) without a dependency: this library loads inside the game.</summary>
    public struct RigQuaternion : IEquatable<RigQuaternion>
    {
        public float X, Y, Z, W;

        public RigQuaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static RigQuaternion Identity => new RigQuaternion(0, 0, 0, 1);

        /// <summary>a × b: b's rotation first, then a's (as Unity's and System.Numerics' quaternion product).</summary>
        public static RigQuaternion operator *(RigQuaternion a, RigQuaternion b) => new RigQuaternion(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        public static bool operator ==(RigQuaternion a, RigQuaternion b) => a.Equals(b);
        public static bool operator !=(RigQuaternion a, RigQuaternion b) => !a.Equals(b);

        public float Length() => (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);

        public RigQuaternion Normalized()
        {
            var l = Length();
            return new RigQuaternion(X / l, Y / l, Z / l, W / l);
        }

        public RigQuaternion Inverse()
        {
            var d = X * X + Y * Y + Z * Z + W * W;
            return new RigQuaternion(-X / d, -Y / d, -Z / d, W / d);
        }

        public static float Dot(RigQuaternion a, RigQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        /// <summary>v rotated by this rotation.</summary>
        public RigVector3 Rotate(RigVector3 v)
        {
            // t = 2 q.xyz × v; v' = v + w t + q.xyz × t
            var tx = 2 * (Y * v.Z - Z * v.Y);
            var ty = 2 * (Z * v.X - X * v.Z);
            var tz = 2 * (X * v.Y - Y * v.X);
            return new RigVector3(v.X + W * tx + (Y * tz - Z * ty), v.Y + W * ty + (Z * tx - X * tz), v.Z + W * tz + (X * ty - Y * tx));
        }

        public bool Equals(RigQuaternion other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override bool Equals(object? obj) => obj is RigQuaternion q && Equals(q);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 7919) ^ (W.GetHashCode() * 104729);
        public override string ToString() => $"({X}, {Y}, {Z}, {W})";
    }

    /// <summary>
    /// A rig edit on one bone, like a JWE NODE bone between it and its parent (Unity space, parent-relative), applied to the
    /// animator's (or growth's) value: position = move + rotate × (scale ⊙ p); rotation = rotate × r; scale = scale ⊙ s.
    /// </summary>
    public struct RigOffset
    {
        public RigVector3 Move;
        public RigQuaternion Rotate;
        public RigVector3 Scale;

        public static RigOffset Identity => new RigOffset { Move = RigVector3.Zero, Rotate = RigQuaternion.Identity, Scale = RigVector3.One };

        public void Apply(ref RigVector3 position, ref RigQuaternion rotation, ref RigVector3 scale)
        {
            position = Move + Rotate.Rotate(Scale * position);
            rotation = Rotate * rotation;
            scale = Scale * scale;
        }

        /// <summary>Undoes <see cref="Apply"/> exactly.</summary>
        public void ApplyInverse(ref RigVector3 position, ref RigQuaternion rotation, ref RigVector3 scale)
        {
            var inverse = Rotate.Inverse();
            position = inverse.Rotate(position - Move) / Scale;
            rotation = inverse * rotation;
            scale /= Scale;
        }

        public bool IsIdentity(float tolerance = 1e-5f) =>
            Move.Length() < tolerance && RigVector3.Distance(Scale, RigVector3.One) < tolerance && Math.Abs(RigQuaternion.Dot(Rotate, RigQuaternion.Identity)) > 1f - tolerance;
    }

    /// <summary>mod.json "rig": bone name → offset; parsing, writing, and a stable hash (to notice a changed rig).</summary>
    public static class RigEdit
    {
        public static Dictionary<string, RigOffset> Parse(object? json, string where)
        {
            if (!(json is Dictionary<string, object?> map)) throw new ManifestException($"{where}: \"rig\" must be an object of bone names.");
            var rig = new Dictionary<string, RigOffset>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) throw new ManifestException($"{where}: a \"rig\" bone has no name.");
                if (!(pair.Value is Dictionary<string, object?> bone))
                    throw new ManifestException($"{where}: rig bone \"{pair.Key}\" must be an object with move, rotate and scale.");
                var offset = RigOffset.Identity;
                if (bone.TryGetValue("move", out var move)) offset.Move = Vec3(move, where, pair.Key, "move");
                if (bone.TryGetValue("rotate", out var rotate))
                {
                    var q = Numbers(rotate, 4, where, pair.Key, "rotate");
                    var quaternion = new RigQuaternion(q[0], q[1], q[2], q[3]);
                    if (quaternion.Length() < 1e-6f) throw new ManifestException($"{where}: rig bone \"{pair.Key}\": \"rotate\" is not a rotation (all zero).");
                    offset.Rotate = quaternion.Normalized();
                }
                if (bone.TryGetValue("scale", out var scale))
                {
                    offset.Scale = Vec3(scale, where, pair.Key, "scale");
                    if (offset.Scale.X <= 0 || offset.Scale.Y <= 0 || offset.Scale.Z <= 0)
                        throw new ManifestException($"{where}: rig bone \"{pair.Key}\": \"scale\" must be above 0 on every axis.");
                }
                rig[pair.Key] = offset;
            }
            return rig;
        }

        /// <summary>The "rig" object for mod.json: bones in name order, identity parts left out.</summary>
        public static Dictionary<string, object?> ToJson(IReadOnlyDictionary<string, RigOffset> rig)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in rig.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var o = pair.Value;
                var entry = new Dictionary<string, object?>();
                if (o.Move != RigVector3.Zero) entry["move"] = List(o.Move.X, o.Move.Y, o.Move.Z);
                if (o.Rotate != RigQuaternion.Identity) entry["rotate"] = List(o.Rotate.X, o.Rotate.Y, o.Rotate.Z, o.Rotate.W);
                if (o.Scale != RigVector3.One) entry["scale"] = List(o.Scale.X, o.Scale.Y, o.Scale.Z);
                map[pair.Key] = entry;
            }
            return map;
        }

        /// <summary>"" for no rig; otherwise a stable fingerprint of the values (kept in a model's build report).</summary>
        public static string Hash(IReadOnlyDictionary<string, RigOffset>? rig)
        {
            if (rig == null || rig.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var pair in rig.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var o = pair.Value;
                sb.Append(pair.Key).Append(':');
                foreach (var v in new[] { o.Move.X, o.Move.Y, o.Move.Z, o.Rotate.X, o.Rotate.Y, o.Rotate.Z, o.Rotate.W, o.Scale.X, o.Scale.Y, o.Scale.Z })
                    sb.Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(';');
            }
            unchecked
            {
                ulong h = 14695981039346656037;
                foreach (var c in sb.ToString()) h = (h ^ c) * 1099511628211;
                return h.ToString("x16");
            }
        }

        private static List<object?> List(params float[] values) => values.Select(v => (object?)(double)v).ToList();

        private static RigVector3 Vec3(object? value, string where, string bone, string part)
        {
            var n = Numbers(value, 3, where, bone, part);
            return new RigVector3(n[0], n[1], n[2]);
        }

        private static float[] Numbers(object? value, int count, string where, string bone, string part)
        {
            if (value is List<object?> list && list.Count == count && list.All(v => v is double))
                return list.Select(v => (float)(double)v!).ToArray();
            throw new ManifestException($"{where}: rig bone \"{bone}\": \"{part}\" must be {count} numbers.");
        }
    }
}
