using PK.Core.Models;

namespace PK.Core.Tests;

public class PackedBitVectorTests
{
    [Fact]
    public void Unpacks_four_bit_ints()
    {
        var v = new PackedBitVector(4, 0, 0, [0x21, 0x0F], 4);
        Assert.Equal(new[] { 1, 2, 15, 0 }, v.UnpackInts());
    }

    [Fact]
    public void Unpacks_ints_that_cross_byte_boundaries()
    {
        // 5 (101), 6 (110), 7 (111) packed LSB-first in 3-bit slots = 0b1_1111_0101 = 0x1F5
        var v = new PackedBitVector(3, 0, 0, [0xF5, 0x01], 3);
        Assert.Equal(new[] { 5, 6, 7 }, v.UnpackInts());
    }

    [Fact]
    public void Unpacks_floats_with_range_and_start()
    {
        var v = new PackedBitVector(4, 2f, 10f, [0x21, 0x0F], 4);
        var f = v.UnpackFloats(2, 8);
        Assert.Equal(new[] { 10f + 2f / 15f * 1, 10f + 2f / 15f * 2, 12f, 10f }, f, new FloatComparer(1e-5f));
    }

    [Fact]
    public void Unpacks_floats_from_a_start_item()
    {
        var v = new PackedBitVector(4, 15f, 0f, [0x21, 0x0F], 4);
        Assert.Equal(new[] { 15f, 0f }, v.UnpackFloats(1, 4, start: 2, numChunks: 2), new FloatComparer(1e-5f));
    }

    [Fact]
    public void Zero_bit_vector_yields_start_values()
    {
        var v = new PackedBitVector(3, 0, 0.5f, [], 0);
        Assert.Equal(new[] { 0.5f, 0.5f, 0.5f }, v.UnpackFloats(1, 4));
        Assert.Equal(new[] { 0, 0, 0 }, v.UnpackInts());
    }

    private sealed class FloatComparer(float tolerance) : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => Math.Abs(x - y) <= tolerance;
        public int GetHashCode(float obj) => 0;
    }
}
