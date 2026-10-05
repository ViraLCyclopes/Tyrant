using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Rig edits in game, in the game's own animal pipeline (PrehistoricKingdom.IK.PKSolverScheduler):
    /// <list type="number">
    /// <item>Before the game copies the bones into its job tree (prefix on OnBeforeLateUpdate): each edited bone the animator
    /// wrote this frame gets its offset composed on the animated value.</item>
    /// <item>After the growth job, before IK phase B and the write-back (postfix on OnAfterLateUpdate): where the growth job
    /// put a bone's position or scale back in the game's virtual bone tree, the offset is composed on the growth value there,
    /// so the game's IK and the rendered mesh both see it (rig spike round 5).</item>
    /// <item>After the write-back (postfix on OnAfterPostLateUpdate): each bone's final value is noted, so step 1 can tell a
    /// frame the animator wrote from one it skipped (offscreen animals keep last frame's values).</item>
    /// </list>
    /// </summary>
    internal static class RigModule
    {
        private const float Same = 1e-6f;

        private sealed class Bone
        {
            public Transform Transform = null!;
            public RigOffset Offset;
            public bool HasFinal;
            public Vector3 FinalPosition, CopiedPosition;
            public Quaternion FinalRotation;
            public Vector3 FinalScale, CopiedScale;
            public object? Virtual; // the game's VLib.VirtualValueTransform for this bone (a handle on native memory)
        }

        private sealed class Animal
        {
            public Component Owner = null!;
            public readonly List<Bone> Bones = new List<Bone>();
            public int RootId;
        }

        private static readonly Dictionary<int, Animal> Animals = new Dictionary<int, Animal>();
        private static readonly List<int> Gone = new List<int>();
        private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);
        private static bool _patched;
        private static int _failures;

        // The game's virtual bone tree, by reflection (the repo has no game assemblies).
        private static PropertyInfo? _native, _valueCopy, _transformTree, _localPosition, _localScale, _isCreated, _transformId;
        private static FieldInfo? _skeleton, _x, _y, _z;
        private static MethodInfo? _tryGet, _tryGetBone;
        private static Type? _float3;
        private static object? _rootBone;

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
            var final = AccessTools.Method(scheduler, "OnAfterPostLateUpdate");
            if (final != null) harmony.Patch(final, postfix: new HarmonyMethod(typeof(RigModule), nameof(AfterWriteBack)));
            var growth = AccessTools.Method(scheduler, "OnAfterLateUpdate");
            if (growth != null && Reflect()) harmony.Patch(growth, postfix: new HarmonyMethod(typeof(RigModule), nameof(AfterGrowth)));
            else FrameworkMod.Log.Warning("Rig edits on bones the game's growth sets are off: the game's bone tree was not found (game updated?).");
            _patched = true;
        }

        private static bool Reflect()
        {
            var animal = AccessTools.TypeByName("PrehistoricKingdom.Animal");
            _native = animal == null ? null : AccessTools.Property(animal, "Native");
            _valueCopy = _native == null ? null : AccessTools.Property(_native.PropertyType, "ValueCopy");
            _skeleton = _valueCopy == null ? null : AccessTools.Field(_valueCopy.PropertyType, "skeleton");
            _transformTree = _skeleton == null ? null : AccessTools.Property(_skeleton.FieldType, "TransformTree");
            var virtualType = AccessTools.TypeByName("VLib.VirtualValueTransform");
            _tryGet = _transformTree == null || virtualType == null ? null
                : AccessTools.Method(_transformTree.PropertyType, "TryGetTransformUNSAFE", new[] { typeof(Transform), virtualType.MakeByRefType() });
            var boneEnum = AccessTools.TypeByName("AnimalsV2.Internal.Systems.Animation.Enum.AnimalBone");
            _tryGetBone = _skeleton == null || boneEnum == null || virtualType == null ? null
                : AccessTools.Method(_skeleton.FieldType, "TryGetBoneTransform", new[] { boneEnum, virtualType.MakeByRefType() });
            try
            {
                _rootBone = boneEnum == null ? null : Enum.Parse(boneEnum, "Root");
            }
            catch (ArgumentException)
            {
                _rootBone = null;
            }
            if (virtualType != null)
            {
                _localPosition = AccessTools.Property(virtualType, "localPosition");
                _localScale = AccessTools.Property(virtualType, "localScale");
                _isCreated = AccessTools.Property(virtualType, "IsCreated");
                _transformId = AccessTools.Property(virtualType, "TransformID");
            }
            _float3 = _localPosition?.PropertyType;
            _x = _float3 == null ? null : AccessTools.Field(_float3, "x");
            _y = _float3 == null ? null : AccessTools.Field(_float3, "y");
            _z = _float3 == null ? null : AccessTools.Field(_float3, "z");
            return _tryGet != null && _localPosition != null && _localScale != null && _isCreated != null && _transformId != null
                && _float3 != null && _x != null && _y != null && _z != null && _localScale.PropertyType == _float3;
        }

        /// <summary>The animal's rig edit (null: none); called whenever its model is chosen (spawn, skin change).</summary>
        public static void Set(object animal, IReadOnlyDictionary<string, RigOffset>? rig)
        {
            if (!(animal is Component owner) || owner == null) return;
            var id = owner.GetInstanceID();
            Animals.Remove(id); // the animator and the growth write the bones again next frame: nothing to undo
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

        // ---- step 1: on the animator's values ----

        public static void AfterAnimation()
        {
            try
            {
                ForEachAnimal(ComposeAnimated);
            }
            catch (Exception ex)
            {
                Fail("Rig edits failed this frame: ", ex);
            }
        }

        private static void ComposeAnimated(Animal animal)
        {
            foreach (var bone in animal.Bones)
            {
                var t = bone.Transform;
                if (t == null) continue;
                var p = t.localPosition;
                var r = t.localRotation;
                var s = t.localScale;
                // Not written by the animator since last frame's end (an animal it skipped): it still wears the edit.
                if (!(bone.HasFinal && p == bone.FinalPosition && r == bone.FinalRotation && s == bone.FinalScale))
                {
                    var position = new RigVector3(p.x, p.y, p.z);
                    var rotation = new RigQuaternion(r.x, r.y, r.z, r.w);
                    var scale = new RigVector3(s.x, s.y, s.z);
                    bone.Offset.Apply(ref position, ref rotation, ref scale);
                    p = new Vector3(position.X, position.Y, position.Z);
                    s = new Vector3(scale.X, scale.Y, scale.Z);
                    t.localPosition = p;
                    t.localRotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
                    t.localScale = s;
                }
                bone.CopiedPosition = p; // what the game copies into its tree now
                bone.CopiedScale = s;
            }
        }

        // ---- step 2: on the growth job's values, in the game's tree ----

        public static void AfterGrowth()
        {
            try
            {
                ForEachAnimal(ComposeGrowth);
            }
            catch (Exception ex)
            {
                Fail("Rig edits on growth bones failed this frame: ", ex);
            }
        }

        private static void ComposeGrowth(Animal animal)
        {
            foreach (var bone in animal.Bones)
            {
                var v = VirtualOf(animal, bone);
                if (v == null) continue;
                var p = Read(_localPosition!.GetValue(v, null));
                var s = Read(_localScale!.GetValue(v, null));
                var o = bone.Offset;
                // Only where the growth job wrote over what step 1 left (never the grounder's root, never a bone it left alone).
                if (!Near(p, bone.CopiedPosition, Same))
                {
                    var g = new RigVector3(p.x, p.y, p.z);
                    var composed = o.Move + o.Rotate.Rotate(o.Scale * g);
                    _localPosition.SetValue(v, Make(composed.X, composed.Y, composed.Z), null);
                }
                if (!Near(s, bone.CopiedScale, 1e-4f))
                {
                    var composed = o.Scale * new RigVector3(s.x, s.y, s.z);
                    _localScale!.SetValue(v, Make(composed.X, composed.Y, composed.Z), null);
                }
            }
        }

        /// <summary>The bone's handle in the game's tree; checked every time (a respawned skeleton gets a new tree).</summary>
        private static object? VirtualOf(Animal animal, Bone bone)
        {
            var t = bone.Transform;
            if (t == null) return null;
            if (bone.Virtual != null && (bool)_isCreated!.GetValue(bone.Virtual, null) && (int)_transformId!.GetValue(bone.Virtual, null) == t.GetInstanceID())
                return bone.RootIdIsThis(animal) ? null : bone.Virtual;
            bone.Virtual = null;
            var native = _native!.GetValue(animal.Owner, null);
            var skeleton = _skeleton!.GetValue(_valueCopy!.GetValue(native, null));
            var tree = _transformTree!.GetValue(skeleton, null);
            var args = new object?[] { t, null };
            if (!(bool)_tryGet!.Invoke(tree, args) || args[1] == null) return null; // not built yet: asked again next frame
            if (animal.RootId == 0 && _tryGetBone != null && _rootBone != null)
            {
                var root = new object?[] { _rootBone, null };
                if ((bool)_tryGetBone.Invoke(skeleton, root) && root[1] != null) animal.RootId = (int)_transformId!.GetValue(root[1], null);
            }
            bone.Virtual = args[1];
            return bone.RootIdIsThis(animal) ? null : bone.Virtual;
        }

        private static bool RootIdIsThis(this Bone bone, Animal animal) => animal.RootId != 0 && bone.Transform != null && bone.Transform.GetInstanceID() == animal.RootId;

        // ---- after the write-back: what each bone ends the frame as ----

        public static void AfterWriteBack()
        {
            try
            {
                ForEachAnimal(animal =>
                {
                    foreach (var bone in animal.Bones)
                    {
                        var t = bone.Transform;
                        if (t == null) continue;
                        bone.FinalPosition = t.localPosition;
                        bone.FinalRotation = t.localRotation;
                        bone.FinalScale = t.localScale;
                        bone.HasFinal = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Fail("Rig edits failed this frame: ", ex);
            }
        }

        private static void ForEachAnimal(Action<Animal> each)
        {
            if (Animals.Count == 0) return;
            Gone.Clear();
            foreach (var pair in Animals)
            {
                if (pair.Value.Owner == null) Gone.Add(pair.Key);
                else each(pair.Value);
            }
            foreach (var id in Gone) Animals.Remove(id);
        }

        private static Vector3 Read(object float3) => new Vector3((float)_x!.GetValue(float3), (float)_y!.GetValue(float3), (float)_z!.GetValue(float3));

        private static object Make(float x, float y, float z) => Activator.CreateInstance(_float3!, x, y, z);

        private static bool Near(Vector3 a, Vector3 b, float tolerance) =>
            Math.Abs(a.x - b.x) <= tolerance && Math.Abs(a.y - b.y) <= tolerance && Math.Abs(a.z - b.z) <= tolerance;

        private static void Fail(string what, Exception ex)
        {
            if (_failures++ < 5) FrameworkMod.Log.Error(what + ex.Message);
        }
    }
}
