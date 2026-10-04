using Tyrant.Core.Models;
using Tyrant.Core.ModelReplacements;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModelBuilderTests
{
    private static (string ModDir, string Glb, PrefabModel Game) Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "mod");
        var game = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        var glb = Path.Combine(dir, "models", "carch-1a2b3c4d.glb");
        GltfModelWriter.WriteGlb(game, game.Renderers[0], glb, [new GltfMaterial("Carch")]);
        return (dir, "models/carch-1a2b3c4d.glb", game with { Renderers = [game.Renderers[0] with { Materials = [new MaterialModel("Carch", [])] }] });
    }

    [Fact]
    public void A_fitting_model_gets_a_tmesh_per_lod_and_a_report()
    {
        var (dir, glb, game) = Setup();

        var report = ModelBuilder.Build(dir, glb, game);

        Assert.Empty(report.Errors);
        using (var stream = File.OpenRead(Path.Combine(dir, ModelFiles.Lod(glb, 0))))
        {
            var tmesh = TMesh.Read(stream);
            Assert.Equal(3, tmesh.VertexCount);
            Assert.Equal(2, tmesh.BoneCount);
            Assert.Equal(ModelBuilder.Stamp(Path.Combine(dir, glb)), tmesh.SourceStamp);
            Assert.Equal("Infant", Assert.Single(tmesh.Shapes).Name);
        }
        Assert.Equal(3, Assert.Single(report.Lods).Vanilla);
        Assert.NotNull(ModelBuilder.ReadReport(dir, glb));
    }

    [Fact]
    public void A_model_with_errors_writes_no_tmesh()
    {
        var (dir, glb, game) = Setup();
        var other = game with { Renderers = [game.Renderers[0] with { Materials = [new MaterialModel("Stego", [])] }] };

        var report = ModelBuilder.Build(dir, glb, other);

        Assert.NotEmpty(report.Errors);
        Assert.False(File.Exists(Path.Combine(dir, ModelFiles.Lod(glb, 0))));
        Assert.NotEmpty(ModelBuilder.ReadReport(dir, glb)!.Errors); // Check reads the errors from the report
    }

    [Fact]
    public void Each_game_lod_gets_its_own_decimated_mesh()
    {
        var (dir, glb, game) = Setup();
        var r0 = game.Renderers[0];
        var lod1 = r0 with
        {
            Name = "Carch_LOD01",
            Mesh = new MeshData
            {
                Name = "Carch_LOD01", Positions = r0.Mesh.Positions[..2], Normals = r0.Mesh.Normals[..2], Uv0 = r0.Mesh.Uv0[..2], Colors = [],
                Skin = r0.Mesh.Skin[..2], Indices = [], SubMeshes = [new SubMesh(0, 0, 0)], BindPoses = r0.Mesh.BindPoses, BlendShapes = r0.Mesh.BlendShapes,
            },
        };

        var report = ModelBuilder.Build(dir, glb, game with { Renderers = [lod1, r0] }); // LOD order comes from the names, not the list

        Assert.Equal(2, report.Lods.Count);
        Assert.Equal([3, 2], report.Lods.Select(l => l.Vanilla));
        Assert.True(File.Exists(Path.Combine(dir, ModelFiles.Lod(glb, 1))));
    }

    [Fact]
    public void A_glb_changed_after_building_is_stale()
    {
        var (dir, glb, game) = Setup();
        ModelBuilder.Build(dir, glb, game);
        Assert.False(ModelBuilder.IsStale(dir, glb));

        File.SetLastWriteTimeUtc(Path.Combine(dir, glb), DateTime.UtcNow.AddMinutes(5));

        Assert.True(ModelBuilder.IsStale(dir, glb));
    }

    [Fact]
    public void A_glb_that_was_never_built_is_stale()
    {
        var (dir, glb, _) = Setup();

        Assert.True(ModelBuilder.IsStale(dir, glb));
    }
}
