using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class VertexBufferReaderTests
{
    [Fact]
    public void Reads_channels_from_16_byte_aligned_streams()
    {
        // stream 0: float3 position (stride 12) × 3 = 36 bytes, padded to 48
        // stream 1: UNorm16×2 at 0 and UInt16×2 at 4 (stride 8) × 3 = 24 bytes → 72 total
        var data = new byte[72];
        var w = new BinaryWriter(new MemoryStream(data));
        for (var v = 0; v < 3; v++) { w.Write(v * 1f); w.Write(v * 2f); w.Write(v * 3f); }
        w.Seek(48, SeekOrigin.Begin);
        for (var v = 0; v < 3; v++) { w.Write((ushort)65535); w.Write((ushort)0); w.Write((ushort)(v + 7)); w.Write((ushort)9); }
        var reader = new VertexBufferReader(data, 3, [new(0, 0, 0, 3), new(1, 0, 4, 2), new(1, 4, 8, 2)]);

        Assert.Equal(new[] { 2f, 4f, 6f }, reader.Read(0, 2));
        Assert.Equal(new[] { 1f, 0f }, reader.Read(1, 1));
        Assert.Equal(new[] { 9f, 9f }, reader.Read(2, 2));
        Assert.True(reader.Has(1));
        Assert.False(reader.Has(5));
    }

    [Fact]
    public void Reads_half_floats_and_unorm8()
    {
        var data = new byte[8];
        BitConverter.TryWriteBytes(data.AsSpan(0), (Half)0.5f);
        BitConverter.TryWriteBytes(data.AsSpan(2), (Half)(-2f));
        data[4] = 255; data[5] = 0; data[6] = 51; data[7] = 255;
        var reader = new VertexBufferReader(data, 1, [new(0, 0, 1, 2), new(0, 4, 2, 4)]);

        Assert.Equal(new[] { 0.5f, -2f }, reader.Read(0, 0));
        Assert.Equal(new[] { 1f, 0f, 0.2f, 1f }, reader.Read(1, 0));
    }

    [Fact]
    public void Data_shorter_than_layout_throws_FormatException()
    {
        Assert.Throws<FormatException>(() => new VertexBufferReader(new byte[10], 3, [new(0, 0, 0, 3)]));
    }

    [Fact]
    public void Unknown_format_throws_NotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => new VertexBufferReader(new byte[64], 1, [new(0, 0, 42, 3)]));
    }
}
