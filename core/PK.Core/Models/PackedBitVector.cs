using AssetsTools.NET;

namespace PK.Core.Models;

/// <summary>Unity's PackedBitVector: values quantized to BitSize bits, packed LSB-first (used by compressed meshes).</summary>
public sealed record PackedBitVector(int NumItems, float Range, float Start, byte[] Data, int BitSize)
{
    public static readonly PackedBitVector Empty = new(0, 0, 0, [], 0);

    public static PackedBitVector FromField(AssetTypeValueField field)
    {
        if (field.IsDummy) return Empty;
        var range = field["m_Range"];
        var start = field["m_Start"];
        return new PackedBitVector(
            (int)field["m_NumItems"].AsUInt,
            range.IsDummy ? 0 : range.AsFloat,
            start.IsDummy ? 0 : start.AsFloat,
            field["m_Data.Array"].AsByteArray ?? [],
            field["m_BitSize"].AsByte);
    }

    public float[] UnpackFloats(int itemCountInChunk, int chunkStride, int start = 0, int numChunks = -1)
    {
        if (numChunks == -1) numChunks = NumItems / itemCountInChunk;
        var total = numChunks * itemCountInChunk;
        var result = new float[total];
        if (BitSize == 0)
        {
            Array.Fill(result, Start);
            return result;
        }

        var bitPos = BitSize * start;
        var indexPos = bitPos / 8;
        bitPos %= 8;
        var scale = 1.0f / Range;
        var max = (1 << BitSize) - 1;
        // Unity stores exactly itemCountInChunk values per chunk; chunkStride only mirrors Unity's API.
        for (var i = 0; i < total; i++)
        {
            var x = ReadBits(ref indexPos, ref bitPos);
            result[i] = x / (scale * max) + Start;
        }
        return result;
    }

    public int[] UnpackInts()
    {
        var result = new int[NumItems];
        if (BitSize == 0) return result;
        int indexPos = 0, bitPos = 0;
        for (var i = 0; i < NumItems; i++) result[i] = (int)ReadBits(ref indexPos, ref bitPos);
        return result;
    }

    private uint ReadBits(ref int indexPos, ref int bitPos)
    {
        uint x = 0;
        var bits = 0;
        while (bits < BitSize)
        {
            x |= (uint)((Data[indexPos] >> bitPos) << bits);
            var num = Math.Min(BitSize - bits, 8 - bitPos);
            bitPos += num;
            bits += num;
            if (bitPos == 8)
            {
                indexPos++;
                bitPos = 0;
            }
        }
        return x & (uint)((1L << BitSize) - 1);
    }
}
