using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tyrant.Dumper.Serialization
{
    /// <summary>
    /// Writes an object's data as JSON following Unity/Odin serialization rules: instance fields that are public or
    /// marked [SerializeField] / [OdinSerialize] / [SerializeReference], minus [NonSerialized]; engine base-class fields are skipped.
    /// </summary>
    public sealed class DumpSerializer
    {
        public const int MaxDepth = 24;

        /// <summary>Values written per root object; shared references can otherwise multiply the output exponentially.</summary>
        public const int MaxValuesPerObject = 250_000;

        private static readonly HashSet<string> SerializeAttributes = new HashSet<string>
        {
            "SerializeField", "OdinSerializeAttribute", "SerializeReference",
        };

        private readonly Dictionary<Type, IReadOnlyList<FieldInfo>> _fieldCache = new Dictionary<Type, IReadOnlyList<FieldInfo>>();
        private readonly IDumpAdapter _adapter;
        private readonly Action<object> _onDumpable;
        private int _valuesLeft;

        public DumpSerializer(IDumpAdapter adapter, Action<object> onDumpable)
        {
            _adapter = adapter;
            _onDumpable = onDumpable;
        }

        public string SerializeRoot(object root)
        {
            var writer = new JsonWriter();
            var path = new HashSet<object>(RefEq.Instance) { root };
            _valuesLeft = MaxValuesPerObject;
            writer.StartObject();
            WriteHeader(writer, _adapter.Describe(root));
            WriteFields(writer, root, path, 1);
            writer.EndObject();
            return writer.ToString();
        }

        /// <summary>Data fields of a type, base classes first, stopping at the first engine base class.</summary>
        public static IReadOnlyList<FieldInfo> SerializedFields(Type type, Func<Type, bool>? isEngineBase = null)
        {
            isEngineBase ??= t => IsEngineNamespace(t.Namespace);
            var chain = new List<Type>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (t != type && isEngineBase(t)) break;
                chain.Add(t);
            }
            chain.Reverse();

            var fields = new List<FieldInfo>();
            foreach (var t in chain)
            {
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsNotSerialized || f.IsLiteral) continue;
                    if (f.IsPublic || f.GetCustomAttributes(false).Any(a => SerializeAttributes.Contains(a.GetType().Name)))
                        fields.Add(f);
                }
            }
            return fields;
        }

        public static string JsonName(FieldInfo field)
        {
            var name = field.Name;
            var end = name.IndexOf(">k__BackingField", StringComparison.Ordinal);
            return name.StartsWith("<", StringComparison.Ordinal) && end > 1 ? name.Substring(1, end - 1) : name;
        }

        private static bool IsEngineNamespace(string? ns) =>
            ns != null && (ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("Sirenix", StringComparison.Ordinal));

        private IReadOnlyList<FieldInfo> FieldsOf(Type type)
        {
            if (!_fieldCache.TryGetValue(type, out var fields))
                _fieldCache[type] = fields = SerializedFields(type, _adapter.IsEngineBaseType);
            return fields;
        }

        private void WriteHeader(JsonWriter writer, EngineObjectInfo info)
        {
            writer.Name("$type");
            writer.String(info.Type);
            writer.Name("$name");
            writer.String(info.Name);
            writer.Name("$id");
            writer.Number(info.Id);
        }

        private void WriteFields(JsonWriter writer, object obj, HashSet<object> path, int depth)
        {
            foreach (var field in FieldsOf(obj.GetType()))
            {
                object? value;
                try
                {
                    value = field.GetValue(obj);
                }
                catch (Exception ex)
                {
                    writer.Name(JsonName(field));
                    writer.String("$error: " + ex.GetType().Name);
                    continue;
                }
                writer.Name(JsonName(field));
                WriteValue(writer, value, path, depth);
            }
        }

        private void WriteValue(JsonWriter writer, object? value, HashSet<object> path, int depth)
        {
            if (value == null) { writer.Null(); return; }
            if (depth > MaxDepth || --_valuesLeft < 0) { writer.String("$truncated"); return; }

            switch (value)
            {
                case string s: writer.String(s); return;
                case bool b: writer.Bool(b); return;
                case char c: writer.String(c.ToString()); return;
                case float f: writer.Number(f); return;
                case double d: writer.Number(d); return;
                case decimal m: writer.Number((double)m); return;
                case Enum e: writer.String(e.ToString()); return;
                case ulong u: writer.Number(u); return;
                case sbyte _: case byte _: case short _: case ushort _: case int _: case uint _: case long _:
                    writer.Number(Convert.ToInt64(value)); return;
                case IntPtr _: case UIntPtr _: case Delegate _: writer.Null(); return;
                case Type t: writer.String(t.FullName); return;
            }

            if (_adapter.IsEngineObject(value))
            {
                if (_adapter.IsInline(value) && path.Add(value))
                {
                    writer.StartObject();
                    WriteHeader(writer, _adapter.Describe(value));
                    WriteFields(writer, value, path, depth + 1);
                    writer.EndObject();
                    path.Remove(value);
                    return;
                }
                if (_adapter.IsDumpable(value)) _onDumpable(value);
                WriteReference(writer, value);
                return;
            }

            if (_adapter.TryWriteSpecial(value, writer)) return;

            var isReference = !value.GetType().IsValueType;
            if (isReference && !path.Add(value)) { writer.String("$cycle"); return; }
            try
            {
                if (value is IDictionary dictionary)
                {
                    // Read entries first: a collection that throws mid-enumeration must not leave half-written JSON.
                    if (!TryRead(() => Entries(dictionary), out var entries, out var error))
                    {
                        writer.String(error);
                        return;
                    }
                    writer.StartObject();
                    foreach (var entry in entries)
                    {
                        writer.Name(KeyText(entry.Key));
                        WriteValue(writer, entry.Value, path, depth + 1);
                    }
                    writer.EndObject();
                }
                else if (value is IEnumerable sequence)
                {
                    if (!TryRead(() => sequence.Cast<object?>().ToList(), out var items, out var error))
                    {
                        writer.String(error);
                        return;
                    }
                    writer.StartArray();
                    foreach (var item in items) WriteValue(writer, item, path, depth + 1);
                    writer.EndArray();
                }
                else
                {
                    writer.StartObject();
                    WriteFields(writer, value, path, depth + 1);
                    writer.EndObject();
                }
            }
            finally
            {
                if (isReference) path.Remove(value);
            }
        }

        /// <summary>IDictionary's own enumerator yields DictionaryEntry (generic dictionaries' IEnumerable yields KeyValuePair).</summary>
        private static List<DictionaryEntry> Entries(IDictionary dictionary)
        {
            var entries = new List<DictionaryEntry>();
            var enumerator = dictionary.GetEnumerator();
            while (enumerator.MoveNext()) entries.Add(enumerator.Entry);
            return entries;
        }

        private static bool TryRead<T>(Func<List<T>> read, out List<T> items, out string error)
        {
            try
            {
                items = read();
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                items = new List<T>();
                error = "$error: " + ex.GetType().Name;
                return false;
            }
        }

        private void WriteReference(JsonWriter writer, object engineObject)
        {
            var info = _adapter.Describe(engineObject);
            writer.StartObject();
            writer.Name("$ref");
            writer.StartObject();
            writer.Name("type");
            writer.String(info.Type);
            writer.Name("name");
            writer.String(info.Name);
            writer.Name("id");
            writer.Number(info.Id);
            writer.EndObject();
            writer.EndObject();
        }

        private string KeyText(object key) =>
            key == null ? "null" : _adapter.IsEngineObject(key) ? _adapter.Describe(key).Name : Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
}
