using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>A bone's rest pose in the prefab's space: what the head-rig measurement needs.</summary>
    public struct RigPose
    {
        public RigPose(RigVector3 position, RigQuaternion rotation, RigVector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public RigVector3 Position;
        public RigQuaternion Rotation;
        public RigVector3 Scale;
    }

    /// <summary>
    /// How a species-wide rig edit changes what the game's head-look "boom" was measured from (HeadNeckControlRig.Config,
    /// tuned per species on the original skeleton): the boom base → head distance (LengthScale) and direction (BoomTurn), and
    /// the head's own rotation (HeadTurn), both turns in the boom base's parent space. mod.json "headRig" on a models entry.
    /// </summary>
    public sealed class HeadRigCorrection
    {
        public HeadRigCorrection(float lengthScale, RigQuaternion boomTurn, RigQuaternion headTurn, string rigHash = "")
        {
            LengthScale = lengthScale;
            BoomTurn = boomTurn;
            HeadTurn = headTurn;
            RigHash = rigHash;
        }

        /// <summary>RigEdit.Hash of the rig edit it was measured for ("" unknown): a changed rig edit is measured again.</summary>
        public string RigHash { get; }

        public HeadRigCorrection For(string rigHash) => new HeadRigCorrection(LengthScale, BoomTurn, HeadTurn, rigHash);

        public float LengthScale { get; }
        public RigQuaternion BoomTurn { get; }
        public RigQuaternion HeadTurn { get; }

        public bool IsIdentity(float tolerance = 1e-4f) =>
            Math.Abs(LengthScale - 1f) < tolerance
            && Math.Abs(RigQuaternion.Dot(BoomTurn, RigQuaternion.Identity)) > 1f - tolerance
            && Math.Abs(RigQuaternion.Dot(HeadTurn, RigQuaternion.Identity)) > 1f - tolerance;

        public Dictionary<string, object?> ToMap() => new Dictionary<string, object?>
        {
            ["lengthScale"] = (double)LengthScale,
            ["boomTurn"] = List(BoomTurn),
            ["headTurn"] = List(HeadTurn),
            ["rigHash"] = RigHash,
        };

        public static HeadRigCorrection Parse(object? json, string where)
        {
            if (!(json is Dictionary<string, object?> map)) throw new ManifestException($"{where}: \"headRig\" must be an object.");
            var scale = map.TryGetValue("lengthScale", out var s) && s is double d ? (float)d : throw new ManifestException($"{where}: \"headRig.lengthScale\" must be a number.");
            if (!(scale > 0) || float.IsInfinity(scale)) throw new ManifestException($"{where}: \"headRig.lengthScale\" must be above 0.");
            var hash = map.TryGetValue("rigHash", out var h) && h is string text ? text : "";
            return new HeadRigCorrection(scale, Turn(map, "boomTurn", where), Turn(map, "headTurn", where), hash);
        }

        private static RigQuaternion Turn(Dictionary<string, object?> map, string name, string where)
        {
            if (!(map.TryGetValue(name, out var value) && value is List<object?> list && list.Count == 4 && list.All(v => v is double)))
                throw new ManifestException($"{where}: \"headRig.{name}\" must be 4 numbers.");
            var q = new RigQuaternion((float)(double)list[0]!, (float)(double)list[1]!, (float)(double)list[2]!, (float)(double)list[3]!);
            if (q.Length() < 1e-6f) throw new ManifestException($"{where}: \"headRig.{name}\" is not a rotation (all zero).");
            return q.Normalized();
        }

        private static List<object?> List(RigQuaternion q) => new List<object?> { (double)q.X, (double)q.Y, (double)q.Z, (double)q.W };
    }

    /// <summary>
    /// The game's head look aims a "boom" from the boom base bone (AnimalSetup.Necks[0], or its parent for a biped) to where
    /// the head should be; its length and angles are measured once per species on the original skeleton, so a rig edit of the
    /// neck or head leaves the head aimed at the original spot. Measure says how the edit changed that geometry; Apply turns
    /// and scales the species' boom by it, keeping the game's hand tuning (pitch offsets, multipliers).
    /// </summary>
    public static class HeadRig
    {
        /// <summary>The game's conversion from a bone's frame to the boom root (VLib ArmatureUtil BoneRotationZUpToUnityConversion).</summary>
        public static readonly RigQuaternion ZUp = new RigQuaternion(0, 0.70710678f, 0.70710678f, 0);
        /// <summary>BoneRotationZDownToUnityConversion, for rigs marked rigUpsideDown.</summary>
        public static readonly RigQuaternion ZDown = new RigQuaternion(-0.70710678f, 0, 0, 0.70710678f);

        /// <summary>From the rest pose before and after the edit: the boom base's parent, the boom base's position, and the head.</summary>
        public static HeadRigCorrection Measure(RigPose baseParent0, RigVector3 base0, RigPose head0, RigPose baseParent1, RigVector3 base1, RigPose head1)
        {
            var d0 = Local(baseParent0, head0.Position - base0);
            var d1 = Local(baseParent1, head1.Position - base1);
            var l0 = d0.Length();
            if (l0 < 1e-6f || d1.Length() < 1e-6f) throw new ArgumentException("the head sits on the boom base; the boom has no length");
            var headTurn = baseParent1.Rotation.Inverse() * head1.Rotation * head0.Rotation.Inverse() * baseParent0.Rotation;
            return new HeadRigCorrection(d1.Length() / l0, FromTo(d0, d1), headTurn.Normalized());
        }

        /// <summary>
        /// The species' boom after the edit. boomOrientation is HeadNeckControlRig.Config.boomArmOrientationFromRoot,
        /// boomPitchDegrees its BoomArmOrientationFromRootPitchOffset, headFromBoom its headOrientationFromBoomArm.
        /// </summary>
        public static void Apply(HeadRigCorrection correction, bool upsideDown, float boomPitchDegrees,
            ref float lengthRaw, ref RigQuaternion boomOrientation, ref RigQuaternion headFromBoom)
        {
            var conv = upsideDown ? ZDown : ZUp;
            var boomTurn = conv.Inverse() * correction.BoomTurn * conv; // into the boom root's frame
            var tuned = boomOrientation * PitchX(boomPitchDegrees);
            var headTurn = boomTurn.Inverse() * conv.Inverse() * correction.HeadTurn * conv;
            headFromBoom = (tuned.Inverse() * headTurn * tuned * headFromBoom).Normalized();
            boomOrientation = (boomTurn * boomOrientation).Normalized();
            lengthRaw *= correction.LengthScale;
        }

        private static RigVector3 Local(RigPose parent, RigVector3 world)
        {
            var scale = (parent.Scale.X + parent.Scale.Y + parent.Scale.Z) / 3f; // the game's rootScale: the mean lossy scale
            var v = parent.Rotation.Inverse().Rotate(world);
            return new RigVector3(v.X / scale, v.Y / scale, v.Z / scale);
        }

        private static RigQuaternion PitchX(float degrees)
        {
            var half = degrees * (float)Math.PI / 360f;
            return new RigQuaternion((float)Math.Sin(half), 0, 0, (float)Math.Cos(half));
        }

        /// <summary>The shortest rotation taking direction a to direction b.</summary>
        private static RigQuaternion FromTo(RigVector3 a, RigVector3 b)
        {
            a = Unit(a);
            b = Unit(b);
            var dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            if (dot < -0.999999f)
            {
                // Opposite: half a turn about any axis at right angles to a.
                var axis = Math.Abs(a.X) < 0.9f ? Cross(new RigVector3(1, 0, 0), a) : Cross(new RigVector3(0, 1, 0), a);
                axis = Unit(axis);
                return new RigQuaternion(axis.X, axis.Y, axis.Z, 0);
            }
            var c = Cross(a, b);
            return new RigQuaternion(c.X, c.Y, c.Z, 1 + dot).Normalized();
        }

        private static RigVector3 Cross(RigVector3 a, RigVector3 b) => new RigVector3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        private static RigVector3 Unit(RigVector3 v)
        {
            var l = v.Length();
            return new RigVector3(v.X / l, v.Y / l, v.Z / l);
        }
    }
}
