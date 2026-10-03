using System.Buffers.Binary;
using System.Text;

namespace Tyrant.Core.Catalog;

/// <summary>A JSON object stored in catalog data (e.g. AssetBundleRequestOptions).</summary>
public sealed record CatalogJsonObject(string ClassName, string Json);

/// <summary>Decodes values written by Addressables' SerializedObjectDecoder (catalog keys and extra data).</summary>
internal static class CatalogBinaryReader
{
    private enum ObjectType : byte
    {
        AsciiString = 0,
        UnicodeString = 1,
        UInt16 = 2,
        UInt32 = 3,
        Int32 = 4,
        Hash128 = 5,
        Type = 6,
        JsonObject = 7,
    }

    public static object ReadObject(byte[] data, int offset)
    {
        var type = (ObjectType)data[offset++];
        switch (type)
        {
            case ObjectType.AsciiString:
            {
                var length = ReadInt32(data, ref offset);
                return Encoding.ASCII.GetString(data, offset, length);
            }
            case ObjectType.UnicodeString:
            {
                var length = ReadInt32(data, ref offset);
                return Encoding.Unicode.GetString(data, offset, length);
            }
            case ObjectType.UInt16:
                return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
            case ObjectType.UInt32:
                return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
            case ObjectType.Int32:
                return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
            case ObjectType.Hash128:
            case ObjectType.Type:
            {
                var length = data[offset++];
                return Encoding.ASCII.GetString(data, offset, length);
            }
            case ObjectType.JsonObject:
            {
                var length = data[offset++];
                offset += length; // assembly name
                length = data[offset++];
                var className = Encoding.ASCII.GetString(data, offset, length);
                offset += length;
                var jsonLength = ReadInt32(data, ref offset);
                return new CatalogJsonObject(className, Encoding.Unicode.GetString(data, offset, jsonLength));
            }
            default:
                throw new FormatException($"Unknown catalog object type {(byte)type} at offset {offset - 1}.");
        }
    }

    public static int ReadInt32(byte[] data, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        return value;
    }
}
