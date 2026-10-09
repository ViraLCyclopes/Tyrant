using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// Sets one serialized field of a component from a prefab override, by reflection: no Unity types are needed, so it is
    /// tested on plain objects. Value-type structs on the way are copied back up the path.
    /// </summary>
    public static class PrefabFieldSetter
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static bool TrySet(object component, string fieldPath, object? value, out string? error)
        {
            error = null;
            var segments = new List<(string Name, int? Index)>();
            foreach (var part in fieldPath.Split('.'))
            {
                var open = part.IndexOf('[');
                if (open < 0) segments.Add((part, null));
                else if (part.EndsWith("]") && int.TryParse(part.Substring(open + 1, part.Length - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var i))
                    segments.Add((part.Substring(0, open), i));
                else
                {
                    error = $"'{fieldPath}' is not a field path (use name.name[0].name).";
                    return false;
                }
            }
            try
            {
                Set(component, segments, 0, value);
                return true;
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = $"It could not be set ({ex.Message}).";
                return false;
            }
        }

        // Returns the container (boxed if it is a struct) after setting, so the caller writes a struct back.
        private static object Set(object container, List<(string Name, int? Index)> path, int at, object? value)
        {
            var (name, index) = path[at];
            var field = Find(container.GetType(), name) ?? throw new InvalidOperationException($"{container.GetType().Name} has no field '{name}'.");
            var last = at == path.Count - 1;
            if (index is int i)
            {
                if (!(field.GetValue(container) is IList list)) throw new InvalidOperationException($"'{name}' is not a list.");
                if (i < 0 || i >= list.Count) throw new InvalidOperationException($"'{name}' has {list.Count} items; there is no item {i}.");
                var elementType = list is Array array ? array.GetType().GetElementType()! : field.FieldType.GetGenericArguments().FirstOrDefault() ?? typeof(object);
                var element = last ? Convert(value, elementType) : Set(list[i] ?? throw new InvalidOperationException($"'{name}[{i}]' is empty."), path, at + 1, value);
                list[i] = element;
                return container;
            }
            if (last)
            {
                if (typeof(IList).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(string))
                    throw new InvalidOperationException($"'{name}' is a {Describe(field.FieldType)}; set its items, e.g. {name}[0].");
                field.SetValue(container, Convert(value, field.FieldType));
                return container;
            }
            var child = field.GetValue(container) ?? throw new InvalidOperationException($"'{name}' is empty.");
            field.SetValue(container, Set(child, path, at + 1, value));
            return container;
        }

        private static FieldInfo? Find(Type? type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Instance);
                if (field != null) return field;
            }
            return null;
        }

        public static bool TryConvert(object? value, Type type, out object? result, out string? error)
        {
            try
            {
                result = Convert(value, type);
                error = null;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                result = null;
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                result = null;
                error = $"It could not be set ({ex.Message}).";
                return false;
            }
        }

        private static object? Convert(object? value, Type type)
        {
            if (type == typeof(string))
                return value as string ?? throw new InvalidOperationException("It needs text.");
            if (type == typeof(bool))
                return value is bool b ? b : throw new InvalidOperationException("It needs true or false.");
            if (type.IsEnum)
            {
                if (value is string s)
                    return Enum.GetNames(type).Contains(s) ? Enum.Parse(type, s) : throw new InvalidOperationException($"{type.Name} has no value '{s}' (it has {string.Join(", ", Enum.GetNames(type))}).");
                if (value is double d && d == Math.Floor(d))
                {
                    var boxed = Enum.ToObject(type, (long)d);
                    return Enum.IsDefined(type, boxed) ? boxed : throw new InvalidOperationException($"{type.Name} has no value {d}.");
                }
                throw new InvalidOperationException($"It needs one of {string.Join(", ", Enum.GetNames(type))}.");
            }
            if (type == typeof(char) || type == typeof(IntPtr) || type == typeof(UIntPtr))
                throw new InvalidOperationException($"A {Describe(type)} cannot be set from a prefab setting.");
            if (type.IsPrimitive)
            {
                if (!(value is double number)) throw new InvalidOperationException("It needs a number.");
                if (type == typeof(float))
                {
                    if (!double.IsInfinity(number) && !double.IsNaN(number) && Math.Abs(number) > float.MaxValue)
                        throw new InvalidOperationException($"{number} is out of range for a Single.");
                    return (float)number;
                }
                if (type == typeof(double)) return number;
                if (number != Math.Floor(number)) throw new InvalidOperationException($"It needs a whole number, not {number}.");
                try
                {
                    return System.Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    throw new InvalidOperationException($"{number} is out of range for a {type.Name}.");
                }
            }
            if (value is Dictionary<string, object?> parts && type.IsValueType)
            {
                var result = Activator.CreateInstance(type)!;
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                if (fields.Length == 0) throw new InvalidOperationException($"A {Describe(type)} has no public fields to set.");
                foreach (var key in parts.Keys)
                    if (!fields.Any(f => f.Name == key)) throw new InvalidOperationException($"It has no field '{key}'.");
                foreach (var field in fields)
                {
                    if (!parts.TryGetValue(field.Name, out var part)) throw new InvalidOperationException($"It needs '{field.Name}' too.");
                    field.SetValue(result, Convert(part, field.FieldType));
                }
                return result;
            }
            throw new InvalidOperationException($"A {Describe(type)} cannot be set from a prefab setting.");
        }

        private static string Describe(Type type) => type.IsGenericType ? type.Name.Substring(0, type.Name.IndexOf('`')) : type.Name;
    }
}
