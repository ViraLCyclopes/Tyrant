using System.Buffers.Binary;

namespace Tyrant.Core.Models;

/// <summary>One of Unity's vertex attribute slots (0 position, 1 normal, 2 tangent, 3 color, 4 uv0 … 12 blend weight, 13 blend indices).</summary>
public readonly record struct VertexChannel(int Stream, int Offset, int Format, int Dimension);

/// <summary>Reads Unity vertex data: streams packed back to back, each starting on a 16-byte boundary.</summary>
public sealed class VertexBufferReader
{
    private readonly byte[] _data;
    private readonly IReadOnlyList<VertexChannel> _channels;
    private readonly int[] _streamOffset;
    private readonly int[] _stride;

    public VertexBufferReader(byte[] data, int vertexCount, IReadOnlyList<VertexChannel> channels)
    {
        _data = data;
        _channels = channels;
        VertexCount = vertexCount;

        var used = channels.Where(c => c.Dimension > 0).ToList();
        var streams = used.Count == 0 ? 0 : used.Max(c => c.Stream) + 1;
        _stride = new int[streams];
        foreach (var c in used)
            _stride[c.Stream] = Math.Max(_stride[c.Stream], c.Offset + FormatSize(c.Format) * c.Dimension);

        _streamOffset = new int[streams];
        var offset = 0;
        for (var s = 0; s < streams; s++)
        {
            _streamOffset[s] = offset;
            offset += _stride[s] * vertexCount;
            if (s < streams - 1) offset = (offset + 15) & ~15;
        }
        if (offset > data.Length)
            throw new FormatException($"Vertex data is {data.Length} bytes but its layout needs {offset}.");
    }

    public int VertexCount { get; }

    public bool Has(int channel) => channel < _channels.Count && _channels[channel].Dimension > 0;

    public float[] Read(int channel, int vertex)
    {
        var c = _channels[channel];
        var size = FormatSize(c.Format);
        var basePos = _streamOffset[c.Stream] + vertex * _stride[c.Stream] + c.Offset;
        var result = new float[c.Dimension];
        for (var k = 0; k < c.Dimension; k++) result[k] = Component(basePos + k * size, c.Format);
        return result;
    }

    public static int FormatSize(int format) => format switch
    {
        0 or 10 or 11 => 4,          // Float32, UInt32, SInt32
        1 or 4 or 5 or 8 or 9 => 2,  // Float16, UNorm16, SNorm16, UInt16, SInt16
        2 or 3 or 6 or 7 => 1,       // UNorm8, SNorm8, UInt8, SInt8
        _ => throw new NotSupportedException($"Vertex attribute format {format} is not supported."),
    };

    private float Component(int pos, int format)
    {
        var span = _data.AsSpan(pos);
        return format switch
        {
            0 => BinaryPrimitives.ReadSingleLittleEndian(span),
            1 => (float)BinaryPrimitives.ReadHalfLittleEndian(span),
            2 => span[0] / 255f,
            3 => Math.Max((sbyte)span[0] / 127f, -1f),
            4 => BinaryPrimitives.ReadUInt16LittleEndian(span) / 65535f,
            5 => Math.Max(BinaryPrimitives.ReadInt16LittleEndian(span) / 32767f, -1f),
            6 => span[0],
            7 => (sbyte)span[0],
            8 => BinaryPrimitives.ReadUInt16LittleEndian(span),
            9 => BinaryPrimitives.ReadInt16LittleEndian(span),
            10 => BinaryPrimitives.ReadUInt32LittleEndian(span),
            11 => BinaryPrimitives.ReadInt32LittleEndian(span),
            _ => throw new NotSupportedException($"Vertex attribute format {format} is not supported."),
        };
    }
}
