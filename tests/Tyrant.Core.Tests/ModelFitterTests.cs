using System.Numerics;
using Tyrant.Core.Models;
using Tyrant.Core.ModelReplacements;

namespace Tyrant.Core.Tests;

public class ModelFitterTests
{
    private static readonly RendererModel Game = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true).Renderers[0];

    private static ImportedMesh Imported(string[]? joints = null, string[]? materials = null, Matrix4x4[]? binds = null, BlendShape[]? shapes = null,
        BoneWeight4[]? skin = null, int extraVertices = 0)
    {
        var mesh = ModelFixture.Triangle(name: "Carch_LOD00");
        var ibm = binds ?? mesh.BindPoses;
        var m = new MeshData
        {
            Name = mesh.Name,
            Positions = [.. mesh.Positions, .. Enumerable.Repeat(new Vector3(5, 5, 5), extraVertices)],
            Normals = [.. mesh.Normals, .. Enumerable.Repeat(Vector3.UnitY, extraVertices)],
            Uv0 = [.. mesh.Uv0, .. Enumerable.Repeat(Vector2.Zero, extraVertices)],
            Colors = [],
            Skin = [.. skin ?? mesh.Skin, .. Enumerable.Repeat(new BoneWeight4(0, 0, 0, 0, 1, 0, 0, 0), extraVertices)],
            Indices = mesh.Indices, SubMeshes = mesh.SubMeshes, BindPoses = ibm, BlendShapes = shapes ?? mesh.BlendShapes,
        };
        return new ImportedMesh(0, "Carch_LOD00", m, joints ?? ["Hip", "Tail"], ibm, materials ?? ["Carch"]);
    }

    [Fact]
    public void Bones_are_matched_by_name_into_the_games_order()
    {
        var swapped = Imported(joints: ["Tail", "Hip"], skin: [new(1, 0, 0, 0, 1, 0, 0, 0), new(0, 0, 0, 0, 1, 0, 0, 0), new(1, 0, 0, 0, 0.5f, 0.5f, 0, 0)],
            binds: [ModelFixture.Triangle().BindPoses[1], Matrix4x4.Identity]);

        var fit = ModelFitter.Fit(swapped, Game, ["Carch"]);

        Assert.Empty(fit.Errors);
        Assert.Equal(0, fit.Mesh!.Skin[0].I0); // "Hip" was joint 1 in the file; it is the game's bone 0
        Assert.Equal(1, fit.Mesh.Skin[1].I0);  // "Tail" was joint 0 in the file; it is the game's bone 1
        Assert.Equal(Game.Mesh.BindPoses, fit.Mesh.BindPoses);
        Assert.Empty(fit.Warnings);
    }

    [Fact]
    public void Weights_on_a_bone_the_game_lacks_are_dropped_and_the_rest_rebalanced()
    {
        // vertex 0: Hip; vertex 1: Crest only; vertex 2: half Hip, half Crest
        var fit = ModelFitter.Fit(Imported(joints: ["Hip", "Crest"], extraVertices: 6), Game, ["Carch"]); // a few stray vertices in a bigger mesh

        Assert.Empty(fit.Errors);
        Assert.Contains(fit.Warnings, w => w.Contains("Crest") && w.Contains("dropped"));
        Assert.Equal((0, 1f), (fit.Mesh!.Skin[2].I0, fit.Mesh.Skin[2].W0)); // what is left is rebalanced to a full weight
        Assert.Equal((0, 1f), (fit.Mesh.Skin[1].I0, fit.Mesh.Skin[1].W0)); // no weight left: it borrows its nearest weighted neighbour's (vertex 0)
    }

    [Fact]
    public void Blenders_neutral_bone_is_dropped_with_a_plain_warning()
    {
        var skin = new BoneWeight4[] { new(0, 0, 0, 0, 1, 0, 0, 0), new(2, 0, 0, 0, 1, 0, 0, 0), new(1, 0, 0, 0, 1, 0, 0, 0) };

        var fit = ModelFitter.Fit(Imported(joints: ["Hip", "Tail", "neutral_bone"], skin: skin, extraVertices: 6), Game, ["Carch"]);

        Assert.Empty(fit.Errors);
        Assert.Contains(fit.Warnings, w => w.Contains("1 vertex") && w.Contains("no weight on the armature"));
        Assert.Equal((0, 1f), (fit.Mesh!.Skin[1].I0, fit.Mesh.Skin[1].W0));
    }

    [Fact]
    public void A_mesh_weighted_to_another_skeleton_is_an_error()
    {
        var fit = ModelFitter.Fit(Imported(joints: ["Wing", "Crest"]), Game, ["Carch"]);

        Assert.Null(fit.Mesh);
        Assert.Contains(fit.Errors, e => e.Contains("game's skeleton") && e.Contains("Wing"));
    }

    [Fact]
    public void A_moved_rest_pose_is_a_warning()
    {
        var moved = Imported(binds: [Matrix4x4.CreateTranslation(0.3f, 0, 0), ModelFixture.Triangle().BindPoses[1]]);

        var fit = ModelFitter.Fit(moved, Game, ["Carch"]);

        Assert.NotNull(fit.Mesh);
        Assert.Contains(fit.Warnings, w => w.Contains("rest pose") && w.Contains("Hip"));
    }

    [Fact]
    public void A_missing_growth_shape_key_is_an_error()
    {
        var fit = ModelFitter.Fit(Imported(shapes: []), Game, ["Carch"]);

        Assert.Contains(fit.Errors, e => e.Contains("Infant") && e.Contains("growth") && e.Contains("Voxel Remesh"));
    }

    [Fact]
    public void Extra_shape_keys_are_dropped_with_a_warning()
    {
        var shapes = new[] { ModelFixture.Triangle().BlendShapes[0], new BlendShape("Smile", [0], [Vector3.UnitY * 0.1f], [Vector3.Zero]) };

        var fit = ModelFitter.Fit(Imported(shapes: shapes), Game, ["Carch"]);

        Assert.Equal(["Infant"], fit.Mesh!.BlendShapes.Select(s => s.Name));
        Assert.Contains(fit.Warnings, w => w.Contains("Smile"));
    }

    [Fact]
    public void A_growth_key_that_moves_much_more_than_the_games_is_a_warning()
    {
        // The game's "Infant" key moves vertex 1 by 0.5; a Basis reshaped in Blender without its keys gives far larger deltas.
        var stale = new[] { new BlendShape("Infant", [0, 1, 2], [new Vector3(0, 3, 0), new Vector3(0, 3, 0), new Vector3(0, 3, 0)], new Vector3[3]) };

        var fit = ModelFitter.Fit(Imported(shapes: stale), Game, ["Carch"]);

        Assert.NotNull(fit.Mesh);
        Assert.Contains(fit.Warnings, w => w.Contains("Infant") && w.Contains("applied twice") && w.Contains("Sculpt Mode") && w.Contains("Basis selected"));
        Assert.DoesNotContain(fit.Warnings, w => w.Contains("outside Edit Mode"));
        Assert.DoesNotContain(fit.Warnings, w => w.Contains("reshape the growth keys too"));
    }

    [Fact]
    public void A_growth_key_like_the_games_is_fine()
    {
        Assert.DoesNotContain(ModelFitter.Fit(Imported(), Game, ["Carch"]).Warnings, w => w.Contains("growth keys"));
    }

    [Fact]
    public void A_material_the_game_does_not_have_is_an_error()
    {
        var fit = ModelFitter.Fit(Imported(materials: ["Gold"]), Game, ["Carch", "Eyes"]); // one game material would take any name

        Assert.Contains(fit.Errors, e => e.Contains("Gold") && e.Contains("Carch"));
    }

    [Fact]
    public void Parts_are_put_in_the_games_material_order()
    {
        var mesh = ModelFixture.Triangle(name: "Carch_LOD00");
        var twoParts = new MeshData
        {
            Name = mesh.Name, Positions = mesh.Positions, Normals = mesh.Normals, Uv0 = mesh.Uv0, Colors = [], Skin = mesh.Skin,
            Indices = [0, 1, 2, 2, 1, 0], SubMeshes = [new SubMesh(0, 3, 0), new SubMesh(3, 3, 0)], BindPoses = mesh.BindPoses, BlendShapes = mesh.BlendShapes,
        };

        var fit = ModelFitter.Fit(new ImportedMesh(0, "Carch_LOD00", twoParts, ["Hip", "Tail"], mesh.BindPoses, ["Eyes", "Carch"]), Game, ["Carch", "Eyes"]);

        Assert.Equal(new uint[] { 2, 1, 0, 0, 1, 2 }, fit.Mesh!.Indices); // the file's "Carch" part first, as the game has it
        Assert.Equal([new SubMesh(0, 3, 0), new SubMesh(3, 3, 0)], fit.Mesh.SubMeshes);
    }

    [Fact]
    public void A_much_heavier_mesh_is_a_warning()
    {
        var tiny = Game with
        {
            Mesh = new MeshData
            {
                Name = "tiny", Positions = [Vector3.Zero], Normals = [Vector3.UnitY], Uv0 = [Vector2.Zero], Colors = [], Skin = [new(0, 0, 0, 0, 1, 0, 0, 0)],
                Indices = [], SubMeshes = [new SubMesh(0, 0, 0)], BindPoses = Game.Mesh.BindPoses, BlendShapes = Game.Mesh.BlendShapes,
            },
        };

        var fit = ModelFitter.Fit(Imported(extraVertices: 2), tiny, ["Carch"]); // 5 vertices against the game's 1

        Assert.Contains(fit.Warnings, w => w.Contains("vertices") && w.Contains("frame rate"));
    }

    [Fact]
    public void A_material_with_blenders_copy_suffix_is_the_games()
    {
        var fit = ModelFitter.Fit(Imported(materials: ["Eyes.001"]), Game, ["Carch", "Eyes"]);

        Assert.Empty(fit.Errors);
        Assert.Equal([new SubMesh(0, 0, 0), new SubMesh(0, 3, 0)], fit.Mesh!.SubMeshes); // the part went to "Eyes"
    }

    [Fact]
    public void With_one_game_material_any_name_is_taken()
    {
        var fit = ModelFitter.Fit(Imported(materials: ["JWE_Body"]), Game, ["Carch"]);

        Assert.Empty(fit.Errors);
    }

    [Fact]
    public void With_several_game_materials_a_foreign_name_is_still_an_error_naming_the_fix()
    {
        var fit = ModelFitter.Fit(Imported(materials: ["JWE_Body"]), Game, ["Carch", "Eyes"]);

        var error = Assert.Single(fit.Errors);
        Assert.Contains("JWE_Body", error);
        Assert.Contains("Use game material", error);
    }
}
