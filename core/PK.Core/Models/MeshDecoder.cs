using System.Buffers.Binary;
using System.Numerics;
using AssetsTools.NET;

namespace PK.Core.Models;

/// <summary>Decodes a Unity Mesh object (uncompressed or compressed) into <see cref="MeshData"/>.</summary>
public static class MeshDecoder
{
    private const int PositionChannel = 0, NormalChannel = 1, ColorChannel = 3, Uv0Channel = 4, WeightChannel = 12, BoneIndexChannel = 13;

    public static MeshData Decode(AssetTypeValueField mesh)
    {
        var nameField = mesh["m_Name"];
        var name = nameField.IsDummy ? "" : nameField.AsString;
        try
        {
            return mesh["m_MeshCompression"].AsByte == 0 ? DecodeUncompressed(mesh, name) : DecodeCompressed(mesh, name);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException
                                       or NullReferenceException or InvalidCastException or OverflowException)
        {
            throw new FormatException($"Mesh '{name}' could not be decoded: {ex.Message}", ex);
        }
    }

    private static MeshData DecodeUncompressed(AssetTypeValueField mesh, string name)
    {
        var vertexData = mesh["m_VertexData"];
        var vertexCount = (int)vertexData["m_VertexCount"].AsUInt;
        var bytes = vertexData["m_DataSize"].AsByteArray ?? [];
        if (bytes.Length == 0 && vertexCount > 0)
        {
            var stream = mesh["m_StreamData.path"];
            throw new NotSupportedException(!stream.IsDummy && stream.AsString.Length > 0
                ? $"Mesh '{name}' keeps its vertex data streamed in a .resS file, which is not supported yet."
                : $"Mesh '{name}' has no vertex data.");
        }

        var channels = vertexData["m_Channels.Array"].Children
            .Select(c => new VertexChannel(c["stream"].AsByte, c["offset"].AsByte, c["format"].AsByte, c["dimension"].AsByte & 0xF))
            .ToArray();
        var reader = new VertexBufferReader(bytes, vertexCount, channels);
        if (!reader.Has(PositionChannel)) throw new NotSupportedException($"Mesh '{name}' has no positions.");

        var positions = new Vector3[vertexCount];
        var normals = reader.Has(NormalChannel) ? new Vector3[vertexCount] : [];
        var uv = reader.Has(Uv0Channel) ? new Vector2[vertexCount] : [];
        var colors = reader.Has(ColorChannel) ? new Vector4[vertexCount] : [];
        var skin = reader.Has(BoneIndexChannel) ? new BoneWeight4[vertexCount] : [];
        for (var v = 0; v < vertexCount; v++)
        {
            positions[v] = Vec3(reader.Read(PositionChannel, v));
            if (normals.Length > 0) normals[v] = Vec3(reader.Read(NormalChannel, v));
            if (uv.Length > 0)
            {
                var t = reader.Read(Uv0Channel, v);
                uv[v] = new Vector2(t[0], t.Length > 1 ? t[1] : 0);
            }
            if (colors.Length > 0)
            {
                var c = reader.Read(ColorChannel, v);
                colors[v] = new Vector4(c[0], c.Length > 1 ? c[1] : 0, c.Length > 2 ? c[2] : 0, c.Length > 3 ? c[3] : 1);
            }
            if (skin.Length > 0)
            {
                var weights = reader.Has(WeightChannel) ? reader.Read(WeightChannel, v) : [1f];
                skin[v] = BoneWeight4.From(reader.Read(BoneIndexChannel, v), weights);
            }
        }

        var indexSize = IndexSize(mesh);
        var ib = mesh["m_IndexBuffer.Array"].AsByteArray ?? [];
        var indices = new uint[ib.Length / indexSize];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = indexSize == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(ib.AsSpan(i * 2)) : BinaryPrimitives.ReadUInt32LittleEndian(ib.AsSpan(i * 4));

        return Build(mesh, name, positions, normals, uv, colors, skin, indices, indexSize);
    }

    private static MeshData DecodeCompressed(AssetTypeValueField mesh, string name)
    {
        var cm = mesh["m_CompressedMesh"];
        var raw = PackedBitVector.FromField(cm["m_Vertices"]).UnpackFloats(3, 12);
        var vertexCount = raw.Length / 3;
        var positions = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++) positions[i] = new Vector3(raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);

        Vector2[] uv = [];
        var uvVector = PackedBitVector.FromField(cm["m_UV"]);
        if (uvVector.NumItems > 0)
        {
            var uvInfoField = cm["m_UVInfo"];
            var uvInfo = uvInfoField.IsDummy ? 0u : uvInfoField.AsUInt;
            var dim = uvInfo == 0 ? 2 : (uvInfo & 4) != 0 ? 1 + (int)(uvInfo & 3) : 0;
            if (dim > 0)
            {
                var f = uvVector.UnpackFloats(dim, dim * 4, 0, vertexCount);
                uv = new Vector2[vertexCount];
                for (var i = 0; i < vertexCount; i++) uv[i] = new Vector2(f[i * dim], dim > 1 ? f[i * dim + 1] : 0);
            }
        }

        Vector3[] normals = [];
        var normalVector = PackedBitVector.FromField(cm["m_Normals"]);
        if (normalVector.NumItems > 0)
        {
            var nd = normalVector.UnpackFloats(2, 8);
            var signs = PackedBitVector.FromField(cm["m_NormalSigns"]).UnpackInts();
            normals = new Vector3[nd.Length / 2];
            for (var i = 0; i < normals.Length; i++)
            {
                float x = nd[i * 2], y = nd[i * 2 + 1], zSquared = 1 - x * x - y * y, z;
                if (zSquared >= 0)
                {
                    z = MathF.Sqrt(zSquared);
                }
                else
                {
                    var n = Vector3.Normalize(new Vector3(x, y, 0));
                    (x, y, z) = (n.X, n.Y, 0f);
                }
                normals[i] = new Vector3(x, y, signs[i] == 0 ? -z : z);
            }
            if (normals.Length != vertexCount) throw new FormatException($"Mesh '{name}' has {normals.Length} normals for {vertexCount} vertices.");
        }

        Vector4[] colors = [];
        var colorVector = PackedBitVector.FromField(cm["m_FloatColors"]);
        if (colorVector.NumItems > 0)
        {
            var c = colorVector.UnpackFloats(1, 4);
            colors = new Vector4[c.Length / 4];
            for (var i = 0; i < colors.Length; i++) colors[i] = new Vector4(c[i * 4], c[i * 4 + 1], c[i * 4 + 2], c[i * 4 + 3]);
            if (colors.Length != vertexCount) throw new FormatException($"Mesh '{name}' has {colors.Length} colours for {vertexCount} vertices.");
        }

        var weightVector = PackedBitVector.FromField(cm["m_Weights"]);
        var skin = weightVector.NumItems > 0
            ? DecodeCompressedSkin(weightVector.UnpackInts(), PackedBitVector.FromField(cm["m_BoneIndices"]).UnpackInts(), vertexCount)
            : [];

        var indices = PackedBitVector.FromField(cm["m_Triangles"]).UnpackInts().Select(i => (uint)i).ToArray();
        return Build(mesh, name, positions, normals, uv, colors, skin, indices, IndexSize(mesh));
    }

    /// <summary>Weights are 5-bit (sum 31); a vertex ends when they reach 31 or after three, the fourth being implied.</summary>
    internal static BoneWeight4[] DecodeCompressedSkin(int[] weights, int[] boneIndices, int vertexCount)
    {
        var skin = new BoneWeight4[vertexCount];
        var idx = new int[4];
        var w = new float[4];
        int vertex = 0, boneIndexPos = 0, j = 0, sum = 0;
        for (var i = 0; i < weights.Length && vertex < vertexCount; i++)
        {
            w[j] = weights[i] / 31f;
            idx[j] = boneIndices[boneIndexPos++];
            j++;
            sum += weights[i];
            if (sum >= 31)
            {
                for (; j < 4; j++) { w[j] = 0; idx[j] = 0; }
            }
            else if (j == 3)
            {
                w[3] = (31 - sum) / 31f;
                idx[3] = boneIndices[boneIndexPos++];
                j = 4;
            }
            else
            {
                continue;
            }
            skin[vertex++] = new BoneWeight4(idx[0], idx[1], idx[2], idx[3], w[0], w[1], w[2], w[3]);
            j = 0;
            sum = 0;
        }
        return skin;
    }

    private static MeshData Build(AssetTypeValueField mesh, string name, Vector3[] positions, Vector3[] normals, Vector2[] uv,
        Vector4[] colors, BoneWeight4[] skin, uint[] indices, int indexSize)
    {
        var subMeshes = mesh["m_SubMeshes.Array"].Children.Select(s =>
        {
            if (s["topology"].AsInt != 0) throw new NotSupportedException($"Mesh '{name}' uses a non-triangle topology, which is not supported.");
            var sub = new SubMesh((int)(s["firstByte"].AsUInt / indexSize), (int)s["indexCount"].AsUInt, (int)s["baseVertex"].AsUInt);
            if (sub.FirstIndex + sub.IndexCount > indices.Length)
                throw new FormatException($"Mesh '{name}' has a sub-mesh beyond its {indices.Length} indices.");
            return sub;
        }).ToArray();

        return new MeshData
        {
            Name = name,
            Positions = positions,
            Normals = normals,
            Uv0 = uv,
            Colors = colors,
            Skin = skin,
            Indices = indices,
            SubMeshes = subMeshes,
            BindPoses = mesh["m_BindPose.Array"].Children.Select(UnityToGltf.ReadUnityMatrix).ToArray(),
            BlendShapes = ReadBlendShapes(mesh, name, positions.Length),
        };
    }

    private static BlendShape[] ReadBlendShapes(AssetTypeValueField mesh, string name, int vertexCount)
    {
        var shapes = mesh["m_Shapes"];
        if (shapes.IsDummy) return [];
        var vertices = shapes["vertices.Array"].Children;
        var frames = shapes["shapes.Array"].Children;
        var result = new List<BlendShape>();
        foreach (var channel in shapes["channels.Array"].Children)
        {
            var frameCount = channel["frameCount"].AsInt;
            if (frameCount <= 0) continue;
            var frame = frames[channel["frameIndex"].AsInt + frameCount - 1]; // full-weight frame
            var first = (int)frame["firstVertex"].AsUInt;
            var count = (int)frame["vertexCount"].AsUInt;
            var hasNormals = frame["hasNormals"].AsBool;
            var indices = new int[count];
            var deltas = new Vector3[count];
            var normalDeltas = new Vector3[count];
            for (var k = 0; k < count; k++)
            {
                var entry = vertices[first + k];
                indices[k] = (int)entry["index"].AsUInt;
                if (indices[k] >= vertexCount)
                    throw new FormatException($"Blend shape '{channel["name"].AsString}' of mesh '{name}' points at vertex {indices[k]} of {vertexCount}.");
                deltas[k] = UnityToGltf.ReadVector3(entry["vertex"]);
                normalDeltas[k] = hasNormals ? UnityToGltf.ReadVector3(entry["normal"]) : Vector3.Zero;
            }
            result.Add(new BlendShape(channel["name"].AsString, indices, deltas, normalDeltas));
        }
        return result.ToArray();
    }

    private static int IndexSize(AssetTypeValueField mesh) => mesh["m_IndexFormat"].AsInt == 0 ? 2 : 4;

    private static Vector3 Vec3(float[] v) => new(v[0], v.Length > 1 ? v[1] : 0, v.Length > 2 ? v[2] : 0);
}
