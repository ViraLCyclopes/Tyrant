using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tyrant.Framework.Core
{
    /// <summary>Applies serialized leaf edits to an isolated graph, including private/backing fields, structs and curve keys.</summary>
    public sealed class AnimalFieldEditor
    {
        private readonly Func<object, bool> _external;
        private readonly Func<object, object> _clone;
        private readonly Dictionary<object, object> _copies = new Dictionary<object, object>(Identity.Instance);
        private readonly HashSet<object> _owned = new HashSet<object>(Identity.Instance);
        private static readonly MethodInfo Memberwise = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public AnimalFieldEditor(object source, object copy, Func<object, bool> external, Func<object, object> clone)
        { _copies[source] = copy; _owned.Add(copy); _external = external; _clone = clone; }

        private static IEnumerable<FieldInfo> Fields(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (t.FullName == "UnityEngine.Object" || t.FullName == "UnityEngine.ScriptableObject") yield break;
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!f.IsNotSerialized && !f.IsLiteral && (f.IsPublic || f.GetCustomAttributes(false).Any(a =>
                        a.GetType().Name == "SerializeField" || a.GetType().Name == "OdinSerializeAttribute" || a.GetType().Name == "SerializeReference"))) yield return f;
            }
        }

        private object Copy(object value, bool forceExternal = false)
        {
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal) return value;
            if (_owned.Contains(value)) return value;
            if (_copies.TryGetValue(value, out var existing)) return existing;
            if (_external(value) && !forceExternal) return value;
            object copy;
            if (value is Array array) copy = array.Clone();
            else if (value is IDictionary) copy = Activator.CreateInstance(type)!;
            else if (value is IList) copy = Activator.CreateInstance(type)!;
            else copy = _clone(value);
            if (!type.IsValueType) { _copies[value] = copy; _owned.Add(copy); }
            if (value is IDictionary dictionary)
            {
                var output = (IDictionary)copy;
                foreach (DictionaryEntry pair in dictionary) output.Add(pair.Key, pair.Value == null ? null : Copy(pair.Value));
            }
            else if (value is IList list)
            {
                var output = (IList)copy;
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    var next = item == null ? null : Copy(item);
                    if (value is Array) output[i] = next; else output.Add(next);
                }
            }
            else foreach (var f in Fields(type))
            {
                var item = f.GetValue(value);
                if (item != null) f.SetValue(copy, Copy(item));
            }
            return copy;
        }

        private static FieldInfo? Field(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    ?? t.GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        private static object ConvertValue(string value, Type type)
        {
            if (type.IsEnum)
            {
                // Flags are serialized as comma-separated names by the game dumper.
                var parsed = Enum.Parse(type, value, false);
                if (!type.IsDefined(typeof(FlagsAttribute), false) && !Enum.IsDefined(type, parsed)) throw new ManifestException("Invalid " + type.Name + ": " + value);
                return parsed;
            }
            if (type == typeof(string)) return value;
            if (type == typeof(bool)) return value == "true" ? true : value == "false" ? false : throw new ManifestException("Expected true or false.");
            if (type == typeof(float))
            {
                var f = float.Parse(value, CultureInfo.InvariantCulture);
                if (float.IsInfinity(f) || float.IsNaN(f)) throw new ManifestException("Value exceeds the game's float range.");
                return f;
            }
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        /// <summary>Applies each editable row that differs from its template value, and each row <paramref name="always"/> picks even when it does not.</summary>
        public void Apply(object root, IEnumerable<AnimalField> fields, Func<AnimalField, bool>? always = null)
        {
            foreach (var field in fields.Where(f => f.Editable && (f.Value != f.TemplateValue || (always != null && always(f)))))
            {
                field.Validate();
                try { Set(root, Parts(field.Path), 0, type => ConvertValue(field.Value, type)); }
                catch (Exception ex) { throw new ManifestException(field.Path + ": " + ex.GetBaseException().Message); }
            }
        }

        /// <summary>
        /// Puts <paramref name="value"/> at <paramref name="path"/>, copying everything above it on the way down exactly like an edit, so
        /// back-references (Parent, animalData) point at the copies. The value is the caller's own: later edits under it change it in place.
        /// </summary>
        public void Replace(object root, string path, object value)
        {
            _owned.Add(value);
            try
            {
                Set(root, Parts(path), 0, type => type.IsInstanceOfType(value) ? value
                    : throw new ManifestException("Expected " + type.Name + ", not " + value.GetType().Name + "."));
            }
            catch (Exception ex) { throw new ManifestException(path + ": " + ex.GetBaseException().Message); }
        }

        private static string[] Parts(string path) => path.Substring(1).Split('/').Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToArray();

        private object Set(object target, string[] path, int index, Func<Type, object> leaf)
        {
            var name = path[index];
            if (target is IDictionary dictionary)
            {
                if (!dictionary.Contains(name)) throw new ManifestException("Dictionary key no longer exists: " + name);
                var child = dictionary[name] ?? throw new ManifestException("Null dictionary value.");
                dictionary[name] = index == path.Length - 1 ? leaf(child.GetType()) : Set(Copy(child, true), path, index + 1, leaf);
                return target;
            }
            if (target is IList list)
            {
                var at = int.Parse(name, CultureInfo.InvariantCulture);
                if (at < 0 || at >= list.Count) throw new ManifestException("Array index no longer exists.");
                var child = list[at] ?? throw new ManifestException("Null array element.");
                list[at] = index == path.Length - 1 ? leaf(child.GetType()) : Set(Copy(child, true), path, index + 1, leaf);
                return target;
            }
            var field = Field(target.GetType(), name);
            var property = field == null ? target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
            if (field == null && (property == null || !property.CanRead || !property.CanWrite)) throw new MissingMemberException(target.GetType().FullName, name);
            var type = field?.FieldType ?? property!.PropertyType;
            object? next;
            if (index == path.Length - 1) next = leaf(type);
            else
            {
                var child = field != null ? field.GetValue(target) : property!.GetValue(target, null);
                if (child == null) throw new ManifestException("Cannot descend into null " + name);
                next = Set(Copy(child, true), path, index + 1, leaf);
            }
            if (field != null) field.SetValue(target, next); else property!.SetValue(target, next, null);
            return target;
        }

        public static object ShallowClone(object value) => Memberwise.Invoke(value, null)!;
        private sealed class Identity : IEqualityComparer<object>
        {
            public static readonly Identity Instance = new Identity();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
