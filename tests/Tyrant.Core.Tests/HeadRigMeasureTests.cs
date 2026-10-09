using System.Numerics;
using System.Text.Json;
using Tyrant.Core.Models;
using Tyrant.Core.Rigging;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class HeadRigMeasureTests
{
    /// <summary>Root → Spine → Neck.001 → Neck.002 → Neck.003 → Head, each 0.3 forward (z) of its parent; AnimalSetup lists the necks.</summary>
    private static PrefabModel Animal()
    {
        var root = new SkeletonNode("Styracosaurus", Vector3.Zero, Quaternion.Identity, Vector3.One);
        var spine = Add(root, "Spine", new Vector3(0, 1, 0));
        var n1 = Add(spine, "Neck.001", new Vector3(0, 0.1f, 0.3f));
        var n2 = Add(n1, "Neck.002", new Vector3(0, 0, 0.3f));
        var n3 = Add(n2, "Neck.003", new Vector3(0, 0, 0.3f));
        var head = Add(n3, "Head", new Vector3(0, 0, 0.3f));
        return new PrefabModel("Styracosaurus", root, [], []) { Setup = new AnimalSetupBones(head, [n1, n2, n3]) };
    }

    private static SkeletonNode Add(SkeletonNode parent, string name, Vector3 position)
    {
        var node = new SkeletonNode(name, position, Quaternion.Identity, Vector3.One, parent);
        parent.Children.Add(node);
        return node;
    }

    private static RigOffset Move(float x, float y, float z) => RigOffset.Identity with { Move = new RigVector3(x, y, z) };

    [Fact]
    public void Stretching_the_neck_lengthens_the_boom_by_the_base_to_head_ratio()
    {
        // Neck.002 → Neck.003 grows from 0.3 to 0.6: the base (Neck.001) → head distance grows from 0.9 to 1.2.
        var c = HeadRigMeasure.For(Animal(), new Dictionary<string, RigOffset> { ["Neck.003"] = Move(0, 0, 0.3f) }, quadrupedal: true);

        Assert.NotNull(c);
        Assert.Equal(1.2f / 0.9f, c!.LengthScale, 4);
        Assert.True(MathF.Abs(RigQuaternion.Dot(c.BoomTurn, RigQuaternion.Identity)) > 0.9999f);
    }

    [Fact]
    public void A_biped_measures_from_the_bone_above_the_first_neck()
    {
        // From Spine: the head is 0.1 up and 1.2 forward; raising Neck.001 by 0.2 moves the head too.
        var c = HeadRigMeasure.For(Animal(), new Dictionary<string, RigOffset> { ["Neck.001"] = Move(0, 0.2f, 0) }, quadrupedal: false);

        Assert.NotNull(c);
        var before = new Vector2(0.1f, 1.2f).Length();
        var after = new Vector2(0.3f, 1.2f).Length();
        Assert.Equal(after / before, c!.LengthScale, 4);
    }

    [Fact]
    public void Edits_that_do_not_change_the_boom_give_no_correction()
    {
        // Moving the whole neck from its base (quadruped): base and head move together.
        Assert.Null(HeadRigMeasure.For(Animal(), new Dictionary<string, RigOffset> { ["Neck.001"] = Move(0, 0.2f, 0) }, quadrupedal: true));
        Assert.Null(HeadRigMeasure.For(Animal(), new Dictionary<string, RigOffset> { ["Tail"] = Move(0, 1, 0) }, quadrupedal: true));
        Assert.Null(HeadRigMeasure.For(Animal(), null, quadrupedal: true));
        Assert.Null(HeadRigMeasure.For(Animal() with { Setup = null }, new Dictionary<string, RigOffset> { ["Head"] = Move(0, 1, 0) }, quadrupedal: true));
    }

    [Fact]
    public void Moving_the_head_onto_the_boom_base_gives_no_correction_rather_than_a_broken_one()
    {
        // Neck.002, Neck.003 and Head pulled back onto Neck.001: the edited boom has no length.
        var rig = new Dictionary<string, RigOffset> { ["Neck.002"] = Move(0, 0, -0.3f), ["Neck.003"] = Move(0, 0, -0.3f), ["Head"] = Move(0, 0, -0.3f) };

        Assert.Null(HeadRigMeasure.For(Animal(), rig, quadrupedal: true));
    }

    [Fact]
    public void The_head_look_bones_run_from_the_boom_base_to_the_head()
    {
        Assert.Equal(["Neck.001", "Neck.002", "Neck.003", "Head"], HeadRigMeasure.Bones(Animal(), quadrupedal: true));
        Assert.Equal(["Spine", "Neck.001", "Neck.002", "Neck.003", "Head"], HeadRigMeasure.Bones(Animal(), quadrupedal: false));
    }

    [Fact]
    public void AnimalSetup_head_and_necks_are_read_from_its_fields()
    {
        var model = Animal();
        var nodes = model.Root.DepthFirst().Select((n, i) => (Id: 100L + i, Node: n)).ToDictionary(p => p.Id, p => p.Node);
        long Id(string name) => nodes.First(p => p.Value.Name == name).Key;
        using var json = JsonDocument.Parse($$"""
            {"Head":{"m_FileID":0,"m_PathID":{{Id("Head")}}},"Necks":[{"m_FileID":0,"m_PathID":{{Id("Neck.001")}}},{"m_FileID":0,"m_PathID":{{Id("Neck.002")}}},{"m_FileID":0,"m_PathID":999}]}
            """);

        var setup = AnimalSetupReader.Parse(json.RootElement, id => nodes.GetValueOrDefault(id));

        Assert.Equal("Head", setup!.Head.Name);
        Assert.Equal(["Neck.001", "Neck.002"], setup.Necks.Select(n => n.Name));
        Assert.Null(AnimalSetupReader.Parse(JsonDocument.Parse("""{"Head":{"m_FileID":0,"m_PathID":999},"Necks":[]}""").RootElement, id => nodes.GetValueOrDefault(id)));
    }
}
