using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Tyrant.Dumper.Serialization
{
    /// <summary>Identity of an engine object as written in references.</summary>
    public sealed class EngineObjectInfo
    {
        public EngineObjectInfo(string type, string name, long id)
        {
            Type = type;
            Name = name;
            Id = id;
        }

        public string Type { get; }
        public string Name { get; }
        public long Id { get; }
    }

    /// <summary>Everything engine-specific the serializer needs; the Unity plugin implements it.</summary>
    public interface IDumpAdapter
    {
        /// <summary>True for engine objects (assets/components): written as references unless inline.</summary>
        bool IsEngineObject(object value);

        /// <summary>Engine base classes (UnityEngine.Object, ScriptableObject…) whose fields are bookkeeping, not game data.</summary>
        bool IsEngineBaseType(System.Type type);

        EngineObjectInfo Describe(object engineObject);

        /// <summary>Engine objects whose data is dumped to its own file (ScriptableObjects).</summary>
        bool IsDumpable(object engineObject);

        /// <summary>Engine objects embedded where referenced (small data assets such as localization entries).</summary>
        bool IsInline(object engineObject);

        /// <summary>Writes a custom JSON form for engine types whose data is not in fields; false to use fields.</summary>
        bool TryWriteSpecial(object value, JsonWriter writer);
    }

    /// <summary>Reference-identity comparer (netstandard2.0 has no ReferenceEqualityComparer).</summary>
    public sealed class RefEq : IEqualityComparer<object>
    {
        public static readonly RefEq Instance = new RefEq();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
