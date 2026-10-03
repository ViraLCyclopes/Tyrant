using System.Numerics;
using AssetsTools.NET;

namespace Tyrant.Core.Tests;

/// <summary>Builds AssetsTools value trees shaped like Unity's Mesh type, for decoder tests (no game data).</summary>
public static class MeshFields
{
    public static AssetTypeValueField Prim(string name, AssetValueType type, object value) => new()
    {
        TemplateField = new AssetTypeTemplateField { Name = name, Type = type.ToString(), ValueType = type, HasValue = true, Children = [] },
        Value = value is string s ? new AssetTypeValue(s) : new AssetTypeValue(type, value),
        Children = [],
    };

    public static AssetTypeValueField Bytes(string name, byte[] data) => new()
    {
        TemplateField = new AssetTypeTemplateField { Name = name, Type = "TypelessData", ValueType = AssetValueType.ByteArray, Children = [] },
        Value = new AssetTypeValue(data, false),
        Children = [],
    };

    public static AssetTypeValueField Obj(string name, params AssetTypeValueField[] children) => new()
    {
        TemplateField = new AssetTypeTemplateField
        {
            Name = name, Type = name, ValueType = AssetValueType.None, Children = children.Select(c => c.TemplateField).ToList(),
        },
        Children = children.ToList(),
    };

    public static AssetTypeValueField Vector(string name, params AssetTypeValueField[] elements) => Obj(name, new AssetTypeValueField
    {
        TemplateField = new AssetTypeTemplateField { Name = "Array", Type = "Array", ValueType = AssetValueType.Array, IsArray = true, Children = [] },
        Value = new AssetTypeValue(AssetValueType.Array, new AssetTypeArrayInfo(elements.Length)),
        Children = elements.ToList(),
    });

    public static AssetTypeValueField U8(string name, int value) => Prim(name, AssetValueType.UInt8, (byte)value);
    public static AssetTypeValueField U32(string name, uint value) => Prim(name, AssetValueType.UInt32, value);
    public static AssetTypeValueField I32(string name, int value) => Prim(name, AssetValueType.Int32, value);
    public static AssetTypeValueField F(string name, float value) => Prim(name, AssetValueType.Float, value);
    public static AssetTypeValueField Bool(string name, bool value) => Prim(name, AssetValueType.Bool, value);
    public static AssetTypeValueField Str(string name, string value) => Prim(name, AssetValueType.String, value);
    public static AssetTypeValueField Vec3(string name, float x, float y, float z) => Obj(name, F("x", x), F("y", y), F("z", z));

    /// <summary>Unity matrix fields (eRC, column-vector convention) from a System.Numerics matrix (row-vector convention).</summary>
    public static AssetTypeValueField Matrix(string name, Matrix4x4 m) => Obj(name,
        F("e00", m.M11), F("e01", m.M21), F("e02", m.M31), F("e03", m.M41),
        F("e10", m.M12), F("e11", m.M22), F("e12", m.M32), F("e13", m.M42),
        F("e20", m.M13), F("e21", m.M23), F("e22", m.M33), F("e23", m.M43),
        F("e30", m.M14), F("e31", m.M24), F("e32", m.M34), F("e33", m.M44));

    public static AssetTypeValueField Channel(int stream, int offset, int format, int dimension) =>
        Obj("data", U8("stream", stream), U8("offset", offset), U8("format", format), U8("dimension", dimension));

    public static AssetTypeValueField SubMesh(uint firstByte, uint indexCount, uint baseVertex = 0, int topology = 0) =>
        Obj("data", U32("firstByte", firstByte), U32("indexCount", indexCount), I32("topology", topology), U32("baseVertex", baseVertex),
            U32("firstVertex", 0), U32("vertexCount", 0));

    public static byte[] PackBits(int[] values, int bitSize)
    {
        var data = new byte[(values.Length * bitSize + 7) / 8];
        var bit = 0;
        foreach (var value in values)
        {
            for (var b = 0; b < bitSize; b++, bit++)
                if (((value >> b) & 1) != 0) data[bit / 8] |= (byte)(1 << (bit % 8));
        }
        return data;
    }

    public static AssetTypeValueField Packed(string name, int[] values, int bitSize, float? range = null, float? start = null)
    {
        var children = new List<AssetTypeValueField> { U32("m_NumItems", (uint)values.Length) };
        if (range is not null) children.Add(F("m_Range", range.Value));
        if (start is not null) children.Add(F("m_Start", start.Value));
        children.Add(Obj("m_Data", Bytes("Array", PackBits(values, bitSize))));
        children.Add(U8("m_BitSize", bitSize));
        return Obj(name, children.ToArray());
    }

    public static AssetTypeValueField Shapes(params (string Name, (uint Index, float Dx, float Dy, float Dz)[] Vertices)[] channels)
    {
        var vertices = new List<AssetTypeValueField>();
        var shapes = new List<AssetTypeValueField>();
        var named = new List<AssetTypeValueField>();
        for (var c = 0; c < channels.Length; c++)
        {
            shapes.Add(Obj("data", U32("firstVertex", (uint)vertices.Count), U32("vertexCount", (uint)channels[c].Vertices.Length),
                Bool("hasNormals", true), Bool("hasTangents", false)));
            foreach (var v in channels[c].Vertices)
                vertices.Add(Obj("data", Vec3("vertex", v.Dx, v.Dy, v.Dz), Vec3("normal", 0, 0, 0), Vec3("tangent", 0, 0, 0), U32("index", v.Index)));
            named.Add(Obj("data", Str("name", channels[c].Name), U32("nameHash", 0), I32("frameIndex", c), I32("frameCount", 1)));
        }
        return Obj("m_Shapes", Vector("vertices", vertices.ToArray()), Vector("shapes", shapes.ToArray()),
            Vector("channels", named.ToArray()), Vector("fullWeights"));
    }

    public static AssetTypeValueField VertexData(uint vertexCount, byte[] data, params AssetTypeValueField[] channels) =>
        Obj("m_VertexData", U32("m_VertexCount", vertexCount), Vector("m_Channels", channels), Bytes("m_DataSize", data));

    public static AssetTypeValueField Mesh(string name, AssetTypeValueField vertexData, byte[] indexBuffer, AssetTypeValueField[] subMeshes,
        int indexFormat = 0, int compression = 0, AssetTypeValueField? compressed = null, AssetTypeValueField? shapes = null,
        int bindPoseCount = 0, string streamPath = "") =>
        Obj("Base",
            Str("m_Name", name),
            Vector("m_SubMeshes", subMeshes),
            shapes ?? Shapes(),
            Vector("m_BindPose", Enumerable.Range(0, bindPoseCount).Select(i => Matrix("data", Matrix4x4.CreateTranslation(i, 0, 0))).ToArray()),
            U8("m_MeshCompression", compression),
            I32("m_IndexFormat", indexFormat),
            Obj("m_IndexBuffer", Bytes("Array", indexBuffer)),
            vertexData,
            compressed ?? Obj("m_CompressedMesh"),
            Obj("m_StreamData", U32("offset", 0), U32("size", 0), Str("path", streamPath)));
}
