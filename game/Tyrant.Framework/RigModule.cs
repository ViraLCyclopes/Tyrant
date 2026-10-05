using System;
using System.Collections.Generic;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Rig edits in game. For animals wearing a skin or species entry with a "rig", each edited bone gets its offset composed
    /// on the animator's value every frame, after the animation and before the game copies the bones into its IK and growth
    /// job tree (a prefix on PKSolverScheduler.OnBeforeLateUpdate): the game's grounder, growth and FABRIK then work on the
    /// edited skeleton. Bones the growth job writes are composed again after the game wrote its tree back (step 2), when that
    /// is switched on.
    /// </summary>
    internal static class RigModule
    {
        /// <summary>Step 2: re-apply edits on bones the growth job puts back (rig spike round 4 decides; Tyrant's Check agrees).</summary>
        internal const bool ReapplyAfterGrowth = false;

        private sealed class Bone
        {
            public Transform Transform = null!;
            public RigOffset Offset;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public bool Wrote;
        }

        private sealed class Animal
        {
            public Component Owner = null!;
            public readonly List<Bone> Bones = new List<Bone>();
        }

        private static readonly Dictionary<int, Animal> Animals = new Dictionary<int, Animal>();
        private static readonly List<int> Gone = new List<int>();
        private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);
        private static bool _patched;

        public static void Start(HarmonyLib.Harmony harmony)
        {
            if (_patched) return;
            var scheduler = AccessTools.TypeByName("PrehistoricKingdom.IK.PKSolverScheduler");
            var before = scheduler == null ? null : AccessTools.Method(scheduler, "OnBeforeLateUpdate");
            if (before == null)
            {
                FrameworkMod.Log.Warning("Rig edits are off: the game's PKSolverScheduler.OnBeforeLateUpdate was not found (game updated?).");
                return;
            }
            harmony.Patch(before, prefix: new HarmonyMethod(typeof(RigModule), nameof(AfterAnimation)));
            if (ReapplyAfterGrowth)
            {
                var after = AccessTools.Method(scheduler, "OnAfterPostLateUpdate");
                if (after != null) harmony.Patch(after, postfix: new HarmonyMethod(typeof(RigModule), nameof(AfterWriteBack)));
                else FrameworkMod.Log.Warning("Rig edits on growth bones are off: PKSolverScheduler.OnAfterPostLateUpdate was not found (game updated?).");
            }
            _patched = true;
        }

        /// <summary>The animal's rig edit (null: none); called whenever its model is chosen (spawn, skin change).</summary>
        public static void Set(object animal, IReadOnlyDictionary<string, RigOffset>? rig)
        {
            if (!(animal is Component owner) || owner == null) return;
            var id = owner.GetInstanceID();
            Animals.Remove(id); // the animator writes the bones again next frame: nothing to undo
            if (rig == null || rig.Count == 0) return;
            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var t in owner.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            var entry = new Animal { Owner = owner };
            foreach (var pair in rig)
            {
                if (byName.TryGetValue(pair.Key, out var t)) entry.Bones.Add(new Bone { Transform = t, Offset = pair.Value });
                else if (Missing.Add(pair.Key)) FrameworkMod.Log.Warning($"Rig edit bone '{pair.Key}' is not on {owner.name}; it is skipped (game updated?).");
            }
            if (entry.Bones.Count > 0) Animals[id] = entry;
        }

        public static void AfterAnimation()
        {
            try
            {
                Compose(afterGrowth: false);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Error("Rig edits failed this frame: " + ex.Message);
            }
        }

        public static void AfterWriteBack()
        {
            try
            {
                Compose(afterGrowth: true);
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Error("Rig edits on growth bones failed this frame: " + ex.Message);
            }
        }

        /// <summary>
        /// Composes each offset on a bone value someone else wrote since our last write, so it never stacks (an animal the game
        /// did not animate this frame keeps what we wrote). After the write-back only growth can have put a channel back, and it
        /// never sets rotation: position and scale are composed again only where they differ from what was written.
        /// </summary>
        private static void Compose(bool afterGrowth)
        {
            if (Animals.Count == 0) return;
            Gone.Clear();
            foreach (var pair in Animals)
            {
                if (pair.Value.Owner == null)
                {
                    Gone.Add(pair.Key);
                    continue;
                }
                foreach (var bone in pair.Value.Bones)
                {
                    var t = bone.Transform;
                    if (t == null) continue;
                    var p = t.localPosition;
                    var r = t.localRotation;
                    var s = t.localScale;
                    if (bone.Wrote && p == bone.Position && r == bone.Rotation && s == bone.Scale) continue;
                    if (afterGrowth && !bone.Wrote) continue; // step 1 has not run for it yet
                    var o = bone.Offset;
                    var position = new RigVector3(p.x, p.y, p.z);
                    var rotation = new RigQuaternion(r.x, r.y, r.z, r.w);
                    var scale = new RigVector3(s.x, s.y, s.z);
                    if (!afterGrowth) o.Apply(ref position, ref rotation, ref scale);
                    else
                    {
                        if (p != bone.Position) position = o.Move + o.Rotate.Rotate(o.Scale * position);
                        if (s != bone.Scale) scale = o.Scale * scale;
                        rotation = new RigQuaternion(r.x, r.y, r.z, r.w);
                    }
                    bone.Position = new Vector3(position.X, position.Y, position.Z);
                    bone.Rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
                    bone.Scale = new Vector3(scale.X, scale.Y, scale.Z);
                    t.localPosition = bone.Position;
                    t.localRotation = bone.Rotation;
                    t.localScale = bone.Scale;
                    bone.Wrote = true;
                }
            }
            foreach (var id in Gone) Animals.Remove(id);
        }
    }
}
