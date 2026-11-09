using System.Reflection;
using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class CompiledAccessTests
{
    /// <summary>Like the game's float3: a struct of three public fields with a three-number constructor.</summary>
    public struct Float3
    {
        public float x, y, z;

        public Float3(float x, float y, float z) => (this.x, this.y, this.z) = (x, y, z);
    }

    /// <summary>Like the game's VirtualValueTransform: a struct that is only a handle; its values live elsewhere.</summary>
    public struct Handle
    {
        public static readonly Dictionary<int, Float3> Store = [];
        private readonly int _id;

        public Handle(int id) => _id = id;

        public Float3 localPosition
        {
            get => Store[_id];
            set => Store[_id] = value;
        }

        public bool IsCreated => _id != 0;
        public int TransformID => _id * 10;
    }

    public sealed class Holder
    {
        public Float3 Value { get; set; }
        public int Number => 7;
    }

    private static PropertyInfo Property<T>(string name) => typeof(T).GetProperty(name)!;
    private static FieldInfo Field(string name) => typeof(Float3).GetField(name)!;

    [Fact]
    public void A_struct_handles_vector_is_read_and_written_through_its_box()
    {
        object boxed = new Handle(3);
        Handle.Store[3] = new Float3(1, 2, 3);
        var read = CompiledAccess.VectorReader(Property<Handle>("localPosition"), Field("x"), Field("y"), Field("z"));
        var write = CompiledAccess.VectorWriter(Property<Handle>("localPosition"));

        read(boxed, out var x, out var y, out var z);
        Assert.Equal((1f, 2f, 3f), (x, y, z));

        write(boxed, 4, 5, 6);
        Assert.Equal((4f, 5f, 6f), (Handle.Store[3].x, Handle.Store[3].y, Handle.Store[3].z));
    }

    [Fact]
    public void Plain_values_of_a_struct_are_read()
    {
        Assert.True(CompiledAccess.Getter<bool>(Property<Handle>("IsCreated"))(new Handle(2)));
        Assert.False(CompiledAccess.Getter<bool>(Property<Handle>("IsCreated"))(new Handle(0)));
        Assert.Equal(20, CompiledAccess.Getter<int>(Property<Handle>("TransformID"))(new Handle(2)));
    }

    [Fact]
    public void A_class_works_the_same_way()
    {
        var holder = new Holder { Value = new Float3(1, 2, 3) };
        var read = CompiledAccess.VectorReader(Property<Holder>("Value"), Field("x"), Field("y"), Field("z"));

        CompiledAccess.VectorWriter(Property<Holder>("Value"))(holder, 7, 8, 9);
        read(holder, out var x, out var y, out var z);

        Assert.Equal((7f, 8f, 9f), (x, y, z));
        Assert.Equal(7, CompiledAccess.Getter<int>(Property<Holder>("Number"))(holder));
    }

    [Fact]
    public void A_vector_without_a_three_number_constructor_is_refused()
    {
        Assert.Throws<MissingMethodException>(() => CompiledAccess.VectorWriter(typeof(string).GetProperty("Length")!));
    }
}
