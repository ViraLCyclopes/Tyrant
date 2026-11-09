using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Tyrant.Framework.Core
{
    /// <summary>Reads a three-number value (the game's float3) of an object without boxing it.</summary>
    public delegate void ReadVector(object target, out float x, out float y, out float z);

    /// <summary>
    /// Members of game types (known only by reflection) as compiled calls. Reflection's GetValue and SetValue box every value
    /// and take microseconds each; rig edits read and write several per bone, per animal, per frame. The target is passed
    /// boxed; a struct target is used in place (never copied), so a setter that writes through a handle still lands.
    /// </summary>
    public static class CompiledAccess
    {
        /// <summary>target.property as a value of type <typeparamref name="TValue"/>.</summary>
        public static Func<object, TValue> Getter<TValue>(PropertyInfo property)
        {
            var target = Expression.Parameter(typeof(object), "target");
            Expression value = Expression.Property(Instance(target, property), property);
            if (value.Type != typeof(TValue)) value = Expression.Convert(value, typeof(TValue));
            return Expression.Lambda<Func<object, TValue>>(value, target).Compile();
        }

        /// <summary>target.property (a struct with three number fields) into x, y and z.</summary>
        public static ReadVector VectorReader(PropertyInfo property, FieldInfo x, FieldInfo y, FieldInfo z)
        {
            var target = Expression.Parameter(typeof(object), "target");
            var outs = new[]
            {
                Expression.Parameter(typeof(float).MakeByRefType(), "x"),
                Expression.Parameter(typeof(float).MakeByRefType(), "y"),
                Expression.Parameter(typeof(float).MakeByRefType(), "z"),
            };
            var vector = Expression.Variable(property.PropertyType, "vector");
            var body = Expression.Block(new[] { vector },
                Expression.Assign(vector, Expression.Property(Instance(target, property), property)),
                Expression.Assign(outs[0], Number(Expression.Field(vector, x))),
                Expression.Assign(outs[1], Number(Expression.Field(vector, y))),
                Expression.Assign(outs[2], Number(Expression.Field(vector, z))));
            return Expression.Lambda<ReadVector>(body, target, outs[0], outs[1], outs[2]).Compile();
        }

        /// <summary>target.property = new Vector(x, y, z), with the vector type's three-number constructor.</summary>
        public static Action<object, float, float, float> VectorWriter(PropertyInfo property)
        {
            var constructor = property.PropertyType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) })
                ?? throw new MissingMethodException(property.PropertyType.FullName, ".ctor(float, float, float)");
            if (!property.CanWrite) throw new MissingMethodException(property.DeclaringType?.FullName, "set_" + property.Name);
            var target = Expression.Parameter(typeof(object), "target");
            var x = Expression.Parameter(typeof(float), "x");
            var y = Expression.Parameter(typeof(float), "y");
            var z = Expression.Parameter(typeof(float), "z");
            var body = Expression.Assign(Expression.Property(Instance(target, property), property), Expression.New(constructor, x, y, z));
            return Expression.Lambda<Action<object, float, float, float>>(body, target, x, y, z).Compile();
        }

        /// <summary>The boxed target as its own type: a struct is used inside its box, not copied out of it.</summary>
        private static Expression Instance(ParameterExpression target, MemberInfo member)
        {
            var type = member.DeclaringType ?? throw new ArgumentException("The member has no declaring type.", nameof(member));
            return type.IsValueType ? Expression.Unbox(target, type) : Expression.Convert(target, type);
        }

        private static Expression Number(Expression value) => value.Type == typeof(float) ? value : Expression.Convert(value, typeof(float));
    }
}
