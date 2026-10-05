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

    private static ImportedMesh Mesh(string name, int lod, params string[] joints) =>
        new(lod, name, ModelFixture.Triangle(name), joints, [], ["Carch"]);

    [Fact]
    public void Of_several_meshes_for_a_lod_the_one_on_the_games_skeleton_is_used_and_the_rest_ignored()
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var game = new HashSet<string>(["Hip", "Tail"]);

        var picked = ModelBuilder.PickMeshes([Mesh("allosaurus_L0: ", 0, "Allosaurus_L0", "def_c_tail3_joint"), Mesh("AllosaurusAnax_LOD00", 0, "Hip", "Tail")], game, warnings, errors);

        Assert.Empty(errors);
        Assert.Equal("AllosaurusAnax_LOD00", Assert.Single(picked).Name);
        Assert.Contains(warnings, w => w.Contains("allosaurus_L0") && w.Contains("ignored"));
    }

    [Fact]
    public void Two_meshes_on_the_games_skeleton_for_one_lod_still_ask_to_be_joined()
    {
        var errors = new List<string>();

        ModelBuilder.PickMeshes([Mesh("Body", 0, "Hip", "Tail"), Mesh("Spikes", 0, "Hip")], new HashSet<string>(["Hip", "Tail"]), [], errors);

        Assert.Contains(errors, e => e.Contains("Body") && e.Contains("Spikes") && e.Contains("Ctrl+J"));
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
        var other = game with { Renderers = [game.Renderers[0] with { Materials = [new MaterialModel("Stego", []), new MaterialModel("Eyes", [])] }] };

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

    private static readonly Dictionary<string, RigOffset> TailRig = new()
    {
        ["Tail"] = new RigOffset { Move = new RigVector3(0, 0, -0.5f), Rotate = RigQuaternion.Identity, Scale = RigVector3.One },
    };

    /// <summary>Like Setup, but the .glb's skeleton carries the rig edit (as Blender exports it after Apply rig edit).</summary>
    private static (string ModDir, string Glb, PrefabModel Game) SetupEdited()
    {
        var (dir, glb, game) = Setup();
        var renderer = game.Renderers[0];
        var m = renderer.Mesh;
        var edited = new MeshData
        {
            Name = m.Name, Positions = m.Positions, Normals = m.Normals, Uv0 = m.Uv0, Colors = m.Colors, Skin = m.Skin, Indices = m.Indices,
            SubMeshes = m.SubMeshes, BindPoses = RigBinds.Edited(game, renderer, TailRig), BlendShapes = m.BlendShapes,
        };
        GltfModelWriter.WriteGlb(game, renderer with { Mesh = edited }, Path.Combine(dir, glb), [new GltfMaterial("Carch")]);
        return (dir, glb, game);
    }

    [Fact]
    public void A_rig_puts_edited_bind_poses_into_the_tmesh_and_its_hash_into_the_report()
    {
        var (dir, glb, game) = SetupEdited();
        var expected = RigBinds.Edited(game, game.Renderers[0], TailRig);

        var report = ModelBuilder.Build(dir, glb, game, rig: TailRig);

        Assert.Empty(report.Errors);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("rest pose"));
        Assert.Equal(RigEdit.Hash(TailRig), report.RigHash);
        using (var stream = File.OpenRead(Path.Combine(dir, ModelFiles.Lod(glb, 0))))
        {
            var tmesh = TMesh.Read(stream);
            Assert.Equal(2 * 16, tmesh.BindPoses.Length);
            var back = ModelBuilder.FromTMesh(tmesh, game.Renderers[0].Mesh.BindPoses).BindPoses;
            for (var b = 0; b < 2; b++) Assert.True(Near(expected[b], back[b]), $"bone {b}");
        }
        Assert.False(ModelBuilder.IsStale(dir, glb, TailRig));
        Assert.True(ModelBuilder.IsStale(dir, glb, null));
    }

    [Fact]
    public void Without_a_rig_the_tmesh_stays_format_1_with_the_games_bind_poses()
    {
        var (dir, glb, game) = Setup();

        var report = ModelBuilder.Build(dir, glb, game);

        Assert.Equal("", report.RigHash);
        using var stream = File.OpenRead(Path.Combine(dir, ModelFiles.Lod(glb, 0)));
        Assert.Empty(TMesh.Read(stream).BindPoses);
        Assert.False(ModelBuilder.IsStale(dir, glb, null));
    }

    [Fact]
    public void A_glb_on_the_games_skeleton_built_with_a_rig_warns_about_the_edited_skeleton()
    {
        var (dir, glb, game) = Setup();

        var report = ModelBuilder.Build(dir, glb, game, rig: TailRig);

        Assert.Contains(report.Warnings, w => w.Contains("edited skeleton"));
    }

    private static bool Near(System.Numerics.Matrix4x4 a, System.Numerics.Matrix4x4 b)
    {
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 4; c++)
                if (MathF.Abs(a[r, c] - b[r, c]) > 1e-4f) return false;
        return true;
    }
}
