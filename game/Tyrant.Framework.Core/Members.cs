using System;
using System.Reflection;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// A field or a property by name, whichever the type really has (uGUI's Toggle.onValueChanged is a field; Slider's,
    /// Button's and TMP_InputField's events are properties). Plain reflection: asking Harmony's AccessTools for the wrong
    /// kind logs a warning every time.
    /// </summary>
    public static class Members
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        /// <summary>The instance field or property <paramref name="name"/> of <paramref name="type"/> or a base type; null if none.</summary>
        public static MemberInfo? Find(Type type, string name) => Find(type, name, Instance);

        private static MemberInfo? Find(Type type, string name, BindingFlags flags)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var only = flags | BindingFlags.DeclaredOnly;
                var field = t.GetField(name, only);
                if (field != null) return field;
                var property = t.GetProperty(name, only);
                if (property != null && property.GetIndexParameters().Length == 0) return property;
            }
            return null;
        }

        public static Type TypeOf(MemberInfo member) => member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;

        public static object? Get(object target, string name) => Read(Find(target.GetType(), name), target);

        public static object? GetStatic(Type type, string name) => Read(Find(type, name, Static), null);

        /// <summary>Sets the member; false when there is none or it is read-only.</summary>
        public static bool Set(object target, string name, object? value)
        {
            switch (Find(target.GetType(), name))
            {
                case FieldInfo field when !field.IsInitOnly && !field.IsLiteral:
                    field.SetValue(target, value);
                    return true;
                case PropertyInfo property when property.CanWrite:
                    property.SetValue(target, value, null);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Puts a new, empty instance of the member's type in it (an event without the original's listeners); null if it cannot.</summary>
        public static object? Replace(object target, string name)
        {
            var member = Find(target.GetType(), name);
            if (member == null) return null;
            var fresh = Activator.CreateInstance(TypeOf(member));
            return Set(target, name, fresh) ? fresh : null;
        }

        private static object? Read(MemberInfo? member, object? target) => member switch
        {
            FieldInfo field => field.GetValue(target),
            PropertyInfo property when property.CanRead => property.GetValue(target, null),
            _ => null,
        };
    }
}
