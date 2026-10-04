using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class TMeshTests
{
    internal static TMesh Sample(bool index32 = false) => new()
    {
        Name = "Carch_LOD00", SourceStamp = "1234|5678", VertexCount = 3,
        Positions = [0, 0, 0, 0, 1, 0, 1, 0, 0], Normals = [0, 0, -1, 0, 0, -1, 0, 0, -1], Uv0 = [0, 0, 0, 1, 1, 0],
        Colors = [], BoneIndices = [0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0], BoneWeights = [1, 0, 0, 0, 1, 0, 0, 0, 0.5f, 0.5f, 0, 0], BoneCount = 2,
        Indices = [0, 1, 2], SubMeshStarts = [0], SubMeshCounts = [3], Index32 = index32,
        Shapes = [new TMeshShape { Name = "Infant", PositionDeltas = [0, 0, 0, 0, 0.5f, 0, 0, 0, 0], NormalDeltas = new float[9] }],
        BoundsMin = [0, 0, 0], BoundsMax = [1, 1, 0],
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_mesh_survives_a_round_trip(bool index32)
    {
        using var stream = new MemoryStream();
        Sample(index32).Write(stream);
        stream.Position = 0;

        var back = TMesh.Read(stream);

        Assert.Equal(("Carch_LOD00", "1234|5678", 3, index32, 2), (back.Name, back.SourceStamp, back.VertexCount, back.Index32, back.BoneCount));
        Assert.Equal(Sample().Positions, back.Positions);
        Assert.Equal(Sample().BoneWeights, back.BoneWeights);
        Assert.Equal("Infant", Assert.Single(back.Shapes).Name);
        Assert.Equal(0.5f, back.Shapes[0].PositionDeltas[4]);
    }

    [Fact]
    public void A_truncated_tmesh_is_refused_with_a_reason()
    {
        using var full = new MemoryStream();
        Sample().Write(full);
        using var cut = new MemoryStream(full.ToArray()[..20]);

        Assert.Contains("cut short", Assert.Throws<TMeshException>(() => TMesh.Read(cut)).Message);
    }

    [Fact]
    public void A_file_from_a_newer_tyrant_is_refused()
    {
        using var stream = new MemoryStream();
        Sample().Write(stream);
        var bytes = stream.ToArray();
        bytes[4] = 99; // the version follows the 4-byte magic

        Assert.Contains("newer Tyrant", Assert.Throws<TMeshException>(() => TMesh.Read(new MemoryStream(bytes))).Message);
    }

    [Fact]
    public void Lod_and_report_files_sit_next_to_the_glb()
    {
        Assert.Equal("models/carch-1a2b.lod2.tmesh", ModelFiles.Lod("models/carch-1a2b.glb", 2));
        Assert.Equal("models/carch-1a2b.model.json", ModelFiles.Report("models/carch-1a2b.glb"));
    }
}
