using System;
using System.Collections.Generic;
using System.Linq;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>Mods' prefab overrides, set on each spawned animal while it is still inactive.</summary>
    internal static class PrefabModule
    {
        private static PrefabTable _table = PrefabTable.Build(Array.Empty<LoadedMod>(), out _);
        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);

        public static bool HasOverrides => _table.HasAny;

        public static void Start(IReadOnlyList<LoadedMod> mods)
        {
            _table = PrefabTable.Build(mods, out var messages);
            foreach (var message in messages) FrameworkMod.Log.Warning(message);
        }

        public static IReadOnlyList<PrefabField>? For(string? target) => _table.For(target);

        public static void Apply(GameObject instance, string target, IReadOnlyList<PrefabField> fields)
        {
            foreach (var field in fields)
            {
                string? error;
                try
                {
                    error = ApplyOne(instance, field);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                if (error != null && Warned.Add(target + "|" + field.Label))
                    FrameworkMod.Log.Warning($"Prefab setting {field.Label} of {target} was skipped: {error}");
            }
        }

        private static string? ApplyOne(GameObject instance, PrefabField field)
        {
            var transform = field.Object.Length == 0 ? instance.transform : instance.transform.Find(field.Object);
            if (transform == null) return $"the prefab has no object '{field.Object}'.";
            var hash = field.Component.IndexOf('#');
            var type = hash < 0 ? field.Component : field.Component.Substring(0, hash);
            var nth = 0;
            if (hash >= 0 && (!int.TryParse(field.Component.Substring(hash + 1), out nth) || nth < 0))
                return $"'{field.Component}' is not a valid component; use <Type> or <Type>#n with n from 0.";
            var component = transform.GetComponents<Component>().Where(c => c != null && c.GetType().Name == type).Skip(nth).FirstOrDefault();
            if (component == null) return $"'{(field.Object.Length == 0 ? "the root" : field.Object)}' has no {field.Component}.";
            switch (component)
            {
                case LODGroup lodGroup:
                    return SetLod(lodGroup, field);
                case Animator animator:
                    return SetAnimator(animator, field);
                default:
                    return PrefabFieldSetter.TrySet(component, field.Field, field.Value, out var error) ? null : error;
            }
        }

        private static string? SetLod(LODGroup group, PrefabField field)
        {
            const string prefix = "lods[", suffix = "].screenRelativeTransitionHeight";
            if (!field.Field.StartsWith(prefix) || !field.Field.EndsWith(suffix) || !int.TryParse(field.Field.Substring(prefix.Length, field.Field.Length - prefix.Length - suffix.Length), out var i))
                return "only lods[i].screenRelativeTransitionHeight can be set on a LODGroup.";
            var lods = group.GetLODs();
            if (i < 0 || i >= lods.Length) return $"the LODGroup has {lods.Length} levels; there is no level {i}.";
            if (!(field.Value is double height)) return "it needs a number.";
            lods[i].screenRelativeTransitionHeight = (float)height;
            group.SetLODs(lods);
            return null;
        }

        private static string? SetAnimator(Animator animator, PrefabField field)
        {
            if (field.Field == "applyRootMotion")
            {
                if (!(field.Value is bool on)) return "it needs true or false.";
                animator.applyRootMotion = on;
                return null;
            }
            if (field.Field == "cullingMode")
            {
                if (!PrefabFieldSetter.TryConvert(field.Value, typeof(AnimatorCullingMode), out var mode, out var error)) return error;
                animator.cullingMode = (AnimatorCullingMode)mode!;
                return null;
            }
            return "only applyRootMotion and cullingMode can be set on an Animator.";
        }
    }
}
