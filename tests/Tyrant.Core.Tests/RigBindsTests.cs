using System.Numerics;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Models;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class RigBindsTests
{
    private static Vector3 WorldPos(Matrix4x4 bind)
    {
        Matrix4x4.Invert(bind, out var world);
        return world.Translation;
    }

    private static int Index(RendererModel r, string name) => r.Bones.Select((b, i) => (b, i)).First(x => x.b.Name == name).i;

    private static Dictionary<string, RigOffset> Rig(string bone, RigVector3? move = null, RigVector3? scale = null)
    {
        var offset = RigOffset.Identity;
        if (move is { } m) offset.Move = m;
        if (scale is { } s) offset.Scale = s;
        return new Dictionary<string, RigOffset> { [bone] = offset };
    }

    [Fact]
    public void Without_a_rig_the_binds_are_the_games()
    {
        var model = IkFixture.Prefab();
        var r = ModelBuilder.GameRenderers(model)[0];

        Assert.Same(r.Mesh.BindPoses, RigBinds.Edited(model, r, null));
        Assert.Same(r.Mesh.BindPoses, RigBinds.Edited(model, r, new Dictionary<string, RigOffset>()));
    }

    [Fact]
    public void A_moved_bone_moves_itself_and_its_children_but_not_its_parent()
    {
        var model = IkFixture.Prefab();
        var r = ModelBuilder.GameRenderers(model)[0];

        var edited = RigBinds.Edited(model, r, Rig("Calve.L", move: new RigVector3(0, -0.2f, 0)));

        var game = r.Mesh.BindPoses;
        // The femur is bound turned, so "down" in its space is not world down: compare against the femur's own axis.
        Matrix4x4.Invert(game[Index(r, "Femur.L")], out var femur);
        var down = Vector3.TransformNormal(new Vector3(0, -0.2f, 0), femur);
        foreach (var name in new[] { "Calve.L", "Foot.L", "Heel.L" })
            Assert.True(Vector3.Distance(WorldPos(game[Index(r, name)]) + down, WorldPos(edited[Index(r, name)])) < 1e-4f, name);
        Assert.Equal(game[Index(r, "Femur.L")], edited[Index(r, "Femur.L")]);
        Assert.Equal(game[Index(r, "Calve.R")], edited[Index(r, "Calve.R")]); // an untouched branch is bit-identical
    }

    [Fact]
    public void A_scaled_bone_spreads_its_children()
    {
        var model = IkFixture.Prefab();
        var r = ModelBuilder.GameRenderers(model)[0];

        var edited = RigBinds.Edited(model, r, Rig("Calve.L", scale: new RigVector3(2, 2, 2)));

        var game = r.Mesh.BindPoses;
        var gameFoot = (WorldPos(game[Index(r, "Foot.L")]) - WorldPos(game[Index(r, "Calve.L")])).Length();
        var editedFoot = (WorldPos(edited[Index(r, "Foot.L")]) - WorldPos(edited[Index(r, "Calve.L")])).Length();
        Assert.Equal(2 * gameFoot, editedFoot, 3);
        // The game's rule scales the bone's own offset from its parent too (position = move + rotate(scale ⊙ p)); a scale
        // about the bone's head (as Blender makes) comes with the move that keeps it in place.
        var femur = WorldPos(game[Index(r, "Femur.L")]);
        Assert.Equal(2 * (WorldPos(game[Index(r, "Calve.L")]) - femur).Length(), (WorldPos(edited[Index(r, "Calve.L")]) - femur).Length(), 3);
    }

    [Fact]
    public void An_offset_on_a_node_outside_the_skin_moves_the_skinned_bones_below_it()
    {
        var model = IkFixture.Prefab();
        var r = ModelBuilder.GameRenderers(model)[0];
        Assert.DoesNotContain(r.Bones, b => b.Name == "Animal");

        var edited = RigBinds.Edited(model, r, Rig("Animal", move: new RigVector3(0, 0.5f, 0)));

        Assert.Equal(0.5f, WorldPos(edited[Index(r, "Hip")]).Y - WorldPos(r.Mesh.BindPoses[Index(r, "Hip")]).Y, 4);
    }

    [Fact]
    public void A_bone_the_skeleton_does_not_have_changes_nothing()
    {
        var model = IkFixture.Prefab();
        var r = ModelBuilder.GameRenderers(model)[0];

        var edited = RigBinds.Edited(model, r, Rig("Tail.099", move: new RigVector3(0, 1, 0)));

        Assert.Equal(r.Mesh.BindPoses, edited);
    }

    [Fact]
    public void Apply_composes_the_offsets_on_the_prefab_skeleton_for_previews()
    {
        var model = IkFixture.Prefab();
        var rig = Rig("Calve.L", move: new RigVector3(0, -0.2f, 0));

        var edited = RigBinds.Apply(model, rig);

        var calve = edited.Root.DepthFirst().First(n => n.Name == "Calve.L");
        var gameCalve = model.Root.DepthFirst().First(n => n.Name == "Calve.L");
        Assert.Equal(gameCalve.LocalPosition.Y - 0.2f, calve.LocalPosition.Y, 5);
        Assert.Contains(ModelBuilder.GameRenderers(edited)[0].Bones, b => ReferenceEquals(b, calve));
        Assert.Same(edited.Root, ModelBuilder.GameRenderers(edited)[0].Owner);
        Assert.Same(model, RigBinds.Apply(model, null));
    }
}
