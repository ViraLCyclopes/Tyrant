using System.Numerics;
using PK.Core.Models;
using static PK.Core.Tests.MeshFields;

namespace PK.Core.Tests;

public class MeshDecoderTests
{
    /// <summary>3 vertices: stream0 pos+normal (stride 24), stream1 uv (stride 8), stream2 weights UNorm16×4 + indices UInt16×4 (stride 16).</summary>
    private static byte[] TriangleVertexBytes()
    {
        var data = new byte[160];
        var w = new BinaryWriter(new MemoryStream(data));
        Vector3[] pos = [new(0, 0, 0), new(0, 1, 0), new(1, 0, 0)];
        foreach (var p in pos) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); w.Write(0f); w.Write(0f); w.Write(-1f); }
        w.Seek(80, SeekOrigin.Begin);
        w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f); w.Write(1f); w.Write(0f);
        w.Seek(112, SeekOrigin.Begin);
        (ushort[] W, ushort[] I)[] skin = [([65535, 0, 0, 0], [0, 0, 0, 0]), ([65535, 0, 0, 0], [1, 0, 0, 0]), ([32767, 32768, 0, 0], [0, 1, 0, 0])];
        foreach (var (weights, indices) in skin) { foreach (var x in weights) w.Write(x); foreach (var x in indices) w.Write(x); }
        return data;
    }

    private static AssetsTools.NET.AssetTypeValueField[] TriangleChannels() =>
    [
        Channel(0, 0, 0, 3), Channel(0, 12, 0, 3), Channel(0, 0, 0, 0), Channel(0, 0, 0, 0), Channel(1, 0, 0, 2),
        Channel(0, 0, 0, 0), Channel(0, 0, 0, 0), Channel(0, 0, 0, 0), Channel(0, 0, 0, 0), Channel(0, 0, 0, 0),
        Channel(0, 0, 0, 0), Channel(0, 0, 0, 0), Channel(2, 0, 4, 4), Channel(2, 8, 8, 4),
    ];

    private static byte[] UShorts(params ushort[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

    [Fact]
    public void Decodes_uncompressed_skinned_mesh_with_blend_shape()
    {
        var field = Mesh("Tri", VertexData(3, TriangleVertexBytes(), TriangleChannels()), UShorts(0, 1, 2), [SubMesh(0, 3)],
            shapes: Shapes(("Infant", [(1u, 0f, 0.5f, 0f)])), bindPoseCount: 2);

        var mesh = MeshDecoder.Decode(field);

        Assert.Equal("Tri", mesh.Name);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(new Vector3(0, 1, 0), mesh.Positions[1]);
        Assert.Equal(new Vector3(0, 0, -1), mesh.Normals[2]);
        Assert.Equal(new Vector2(0, 1), mesh.Uv0[1]);
        Assert.Equal(new Vector2(1, 0), mesh.Uv0[2]);
        Assert.Empty(mesh.Colors);
        Assert.Equal(1, mesh.Skin[1].I0);
        Assert.Equal(1f, mesh.Skin[1].W0, 4);
        Assert.Equal(0.5f, mesh.Skin[2].W0, 3);
        Assert.Equal(1, mesh.Skin[2].I1);
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices);
        Assert.Equal(new SubMesh(0, 3, 0), Assert.Single(mesh.SubMeshes));
        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(2, mesh.BindPoses.Length);
        Assert.Equal(1f, mesh.BindPoses[1].M41);
        var shape = Assert.Single(mesh.BlendShapes);
        Assert.Equal("Infant", shape.Name);
        Assert.Equal(new[] { 1 }, shape.VertexIndices);
        Assert.Equal(new Vector3(0, 0.5f, 0), shape.PositionDeltas[0]);
    }

    [Fact]
    public void Uses_32_bit_indices_and_converts_first_byte_to_first_index()
    {
        var indices = new uint[] { 0, 1, 2, 2, 1, 0 }.SelectMany(BitConverter.GetBytes).ToArray();
        var field = Mesh("Tri32", VertexData(3, TriangleVertexBytes(), TriangleChannels()), indices, [SubMesh(0, 3), SubMesh(12, 3)], indexFormat: 1);

        var mesh = MeshDecoder.Decode(field);

        Assert.Equal(6, mesh.Indices.Length);
        Assert.Equal(3, mesh.SubMeshes[1].FirstIndex);
        Assert.Equal(2, mesh.TriangleCount);
    }

    [Fact]
    public void Decodes_compressed_mesh()
    {
        var compressed = Obj("m_CompressedMesh",
            Packed("m_Vertices", [0, 0, 0, 0, 1, 0, 1, 0, 0], 8, range: 255, start: 0),
            Packed("m_UV", [0, 0, 0, 1, 1, 0], 8, range: 255, start: 0),
            Packed("m_Normals", [0, 0, 0, 0, 0, 0], 1, range: 0, start: 0),
            Packed("m_Tangents", [], 1, range: 0, start: 0),
            Packed("m_Weights", [31, 31, 15, 16], 5),
            Packed("m_NormalSigns", [1, 1, 0], 1),
            Packed("m_TangentSigns", [], 1),
            Packed("m_FloatColors", [], 1, range: 0, start: 0),
            Packed("m_BoneIndices", [0, 1, 0, 1], 4),
            Packed("m_Triangles", [0, 1, 2], 4),
            U32("m_UVInfo", 0));
        var field = Mesh("Packed", VertexData(0, []), [], [SubMesh(0, 3)], compression: 1, compressed: compressed);

        var mesh = MeshDecoder.Decode(field);

        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(new Vector3(1, 0, 0), mesh.Positions[2]);
        Assert.Equal(new Vector2(0, 1), mesh.Uv0[1]);
        Assert.Equal(new Vector3(0, 0, 1), mesh.Normals[0]);
        Assert.Equal(new Vector3(0, 0, -1), mesh.Normals[2]);
        Assert.Equal(1, mesh.Skin[1].I0);
        Assert.Equal(15f / 31f, mesh.Skin[2].W0, 4);
        Assert.Equal(16f / 31f, mesh.Skin[2].W1, 4);
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices);
    }

    [Fact]
    public void Compressed_skin_fills_the_fourth_weight_when_three_do_not_sum_to_31()
    {
        var skin = MeshDecoder.DecodeCompressedSkin([10, 10, 5], [3, 4, 5, 6], 1);
        Assert.Equal(new BoneWeight4(3, 4, 5, 6, 10f / 31, 10f / 31, 5f / 31, 6f / 31), skin[0]);
    }

    [Fact]
    public void Streamed_vertex_data_is_not_supported()
    {
        var field = Mesh("Dung", VertexData(3, []), UShorts(0, 1, 2), [SubMesh(0, 3)], streamPath: "archive:/CAB-1/CAB-1.resS");
        var ex = Assert.Throws<NotSupportedException>(() => MeshDecoder.Decode(field));
        Assert.Contains("streamed", ex.Message);
    }

    [Fact]
    public void Non_triangle_topology_is_not_supported()
    {
        var field = Mesh("Lines", VertexData(3, TriangleVertexBytes(), TriangleChannels()), UShorts(0, 1, 2), [SubMesh(0, 3, topology: 3)]);
        Assert.Throws<NotSupportedException>(() => MeshDecoder.Decode(field));
    }

    [Fact]
    public void Truncated_index_data_throws_FormatException()
    {
        var field = Mesh("Bad", VertexData(3, TriangleVertexBytes(), TriangleChannels()), [1], [SubMesh(0, 3)],
            shapes: Shapes(("Infant", [(9u, 0f, 0f, 0f)])));
        Assert.Throws<FormatException>(() => MeshDecoder.Decode(field));
    }

    [Fact]
    public void Compressed_colour_count_must_match_vertex_count()
    {
        var compressed = Obj("m_CompressedMesh",
            Packed("m_Vertices", [0, 0, 0, 0, 1, 0, 1, 0, 0], 8, range: 255, start: 0),
            Packed("m_FloatColors", [255, 255, 255, 255], 8, range: 1, start: 0),
            Packed("m_Triangles", [0, 1, 2], 4),
            U32("m_UVInfo", 0));
        var field = Mesh("BadColours", VertexData(0, []), [], [SubMesh(0, 3)], compression: 1, compressed: compressed);

        Assert.Throws<FormatException>(() => MeshDecoder.Decode(field));
    }
}
