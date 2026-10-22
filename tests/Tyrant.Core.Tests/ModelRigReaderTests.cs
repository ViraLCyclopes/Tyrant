using System.Numerics;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Models;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModelRigReaderTests
{
    private const float Degrees = MathF.PI / 180;

    private static string TempGlb(string name = "Fixture.glb") => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), name);

    private static SkeletonNode Node(string name, Vector3 at, SkeletonNode parent, Quaternion? rotation = null, float scale = 1f)
    {
        var node = new SkeletonNode(name, at, rotation ?? Quaternion.Identity, new Vector3(scale), parent);
        parent.Children.Add(node);
        return node;
    }

    private static Matrix4x4 Local(SkeletonNode n) =>
        Matrix4x4.CreateScale(n.LocalScale) * Matrix4x4.CreateFromQuaternion(n.LocalRotation) * Matrix4x4.CreateTranslation(n.LocalPosition);

    private static Matrix4x4 Inverse(Matrix4x4 m) => Matrix4x4.Invert(m, out var inverse) ? inverse : throw new InvalidOperationException();

    /// <summary>
    /// A 16-bone animal laid out as the game's are: the root saved far from the origin and turned (the glTF writer drops
    /// that), the armature node "Carch" turned -90° about X (scaled <paramref name="armatureScale"/>), the renderer on a
    /// mesh node beside it, unskinned *_end tips. The hip and a femur are bound turned against the prefab's pose.
    /// The bind poses are in <paramref name="meshFrame"/> (K) against the prefab's frame, so a Blender export of it is in
    /// the frame G = K⁻¹ against the game's mesh.
    /// </summary>
    private static PrefabModel Game(Matrix4x4? meshFrame = null, float armatureScale = 1f)
    {
        var root = new SkeletonNode("Fixture", new Vector3(1955, 99, 1127), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f), Vector3.One);
        var carch = Node("Carch", Vector3.Zero, root, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -90 * Degrees), armatureScale);
        var meshNode = Node("Fixture_LOD0", Vector3.Zero, root, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -90 * Degrees));
        var main = Node("MainBone", new Vector3(0, 0.05f, 1.2f), carch, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 90 * Degrees));
        var hip = Node("Hip", new Vector3(0, 0.1f, 0), main);
        var neck1 = Node("Neck1", new Vector3(0, 0.3f, 0.4f), hip, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -20 * Degrees));
        var neck2 = Node("Neck2", new Vector3(0, 0.25f, 0.2f), neck1);
        var neck3 = Node("Neck3", new Vector3(0, 0.2f, 0.2f), neck2, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 5 * Degrees));
        var head = Node("Head", new Vector3(0, 0.15f, 0.25f), neck3);
        Node("Head_end", new Vector3(0, 0, 0.3f), head);
        var tail1 = Node("Tail1", new Vector3(0, 0, -0.5f), hip);
        var tail2 = Node("Tail2", new Vector3(0, 0.02f, -0.4f), tail1, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 8 * Degrees));
        var tail3 = Node("Tail3", new Vector3(0, 0, -0.35f), tail2);
        var tail4 = Node("Tail4", new Vector3(0, -0.01f, -0.3f), tail3);
        Node("Tail4_end", new Vector3(0, 0, -0.2f), tail4);
        SkeletonNode[] Leg(string side, float x)
        {
            var femur = Node($"Leg.{side}", new Vector3(x, -0.1f, 0), hip);
            var shin = Node($"Shin.{side}", new Vector3(0, -0.5f, 0.1f), femur);
            var foot = Node($"Foot.{side}", new Vector3(0, -0.45f, -0.1f), shin);
            return [femur, shin, foot];
        }
        var left = Leg("L", 0.25f);
        var right = Leg("R", -0.25f);
        SkeletonNode[] bones = [main, hip, neck1, neck2, neck3, head, tail1, tail2, tail3, tail4, .. left, .. right];

        Matrix4x4 BindLocal(SkeletonNode n) =>
            (n == hip ? Matrix4x4.CreateRotationY(0.25f) * Matrix4x4.CreateRotationX(0.15f)
                : n == left[0] ? Matrix4x4.CreateRotationZ(0.3f) : Matrix4x4.Identity) * Local(n);
        // The prefab's frame (the glTF writer's): the root keeps only its scale.
        Matrix4x4 BindWorld(SkeletonNode n) => n == carch ? Local(carch) : BindLocal(n) * BindWorld(n.Parent!);
        var k = meshFrame ?? Matrix4x4.Identity;
        Matrix4x4[] binds = [.. bones.Select(b => Inverse(BindWorld(b) * k))];
        var renderer = new RendererModel("Fixture_LOD0", ModelFixture.SplitQuad(binds), bones, meshNode);
        return new PrefabModel("Fixture", root, [renderer], []);
    }

    private static RendererModel Renderer(PrefabModel game) => ModelBuilder.GameRenderers(game)[0];

    private static Dictionary<string, RigOffset> Rig(params (string Bone, Vector3 Move, Quaternion Rotate, float Scale)[] bones)
    {
        var rig = new Dictionary<string, RigOffset>(StringComparer.Ordinal);
        foreach (var (bone, move, rotate, scale) in bones)
            rig[bone] = new RigOffset { Move = move.ToRig(), Rotate = rotate.ToRig(), Scale = new RigVector3(scale, scale, scale) };
        return rig;
    }

    private static (string, Vector3, Quaternion, float) Move(string bone, float x, float y, float z) => (bone, new Vector3(x, y, z), Quaternion.Identity, 1f);

    private static (string, Vector3, Quaternion, float) Turn(string bone, Vector3 axis, float degrees) =>
        (bone, Vector3.Zero, Quaternion.CreateFromAxisAngle(axis, degrees * Degrees), 1f);

    /// <summary>R from the brief: Neck1 moved and turned 10° about X, Tail1 turned -15° about Y.</summary>
    private static Dictionary<string, RigOffset> R() => Rig(
        ("Neck1", new Vector3(0, 0.05f, 0.02f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 10 * Degrees), 1f),
        Turn("Tail1", Vector3.UnitY, -15));

    /// <summary>
    /// What Blender writes for the game made with a rig edit: the edited bind skeleton moved into its frame (file = edited
    /// world · G, G = K⁻¹), each bone's own rig scale dropped (its rest pose cannot hold it), the uniform scale of the frame
    /// kept. <paramref name="leaveOut"/> bones are not in the file.
    /// </summary>
    private static string Export(PrefabModel game, IReadOnlyDictionary<string, RigOffset>? rig, Matrix4x4? meshFrame = null, params string[] leaveOut)
    {
        var renderer = Renderer(game);
        var edited = RigBinds.Edited(game, renderer, rig);
        var g = Inverse(meshFrame ?? Matrix4x4.Identity);
        var bones = new List<SkeletonNode>();
        var binds = new List<Matrix4x4>();
        for (var i = 0; i < renderer.Bones.Count; i++)
        {
            if (leaveOut.Contains(renderer.Bones[i].Name)) continue;
            var world = Inverse(edited[i]) * g;
            var unedited = Inverse(renderer.Mesh.BindPoses[i]) * g;
            Matrix4x4.Decompose(world, out _, out var rotation, out var position);
            var size = MathF.Cbrt(MathF.Abs(unedited.GetDeterminant()));
            bones.Add(renderer.Bones[i]);
            binds.Add(Inverse(Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position)));
        }
        var path = TempGlb();
        var file = renderer with { Mesh = ModelFixture.SplitQuad([.. binds]), Bones = bones };
        GltfModelWriter.WriteGlb(game, file, path);
        return path;
    }

    private static ModelSkeleton Skeleton(string glb, PrefabModel game) => GlbModelReader.ReadSkeleton(glb, game);

    private static float Angle(Quaternion q) => 2 * MathF.Atan2(new Vector3(q.X, q.Y, q.Z).Length(), MathF.Abs(q.W)) / Degrees;

    private static void AssertRig(IReadOnlyDictionary<string, RigOffset> expected, IReadOnlyDictionary<string, RigOffset> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (bone, want) in expected)
        {
            var got = actual[bone];
            Assert.True(Vector3.Distance(want.Move.ToNumerics(), got.Move.ToNumerics()) < 1e-4f, $"{bone} move {got.Move} for {want.Move}");
            var difference = got.Rotate.ToNumerics() * Quaternion.Conjugate(want.Rotate.ToNumerics());
            Assert.True(Angle(difference) < 0.01f, $"{bone} rotate {got.Rotate} for {want.Rotate}");
            Assert.Equal(want.Scale, got.Scale);
        }
    }

    [Fact]
    public void A_model_written_for_a_rig_edit_reads_back_its_moves_and_turns()
    {
        var game = Game();

        var reading = ModelRigReader.Read(Skeleton(Export(game, R()), game), game, saved: null, rigFile: null, "Fixture");

        Assert.Equal(RigSource.Bones, reading.Source);
        AssertRig(R(), reading.Rig);
        Assert.Empty(reading.Warnings);
    }

    [Fact]
    public void A_top_bone_move_is_read_as_a_rig_edit_not_as_the_frame()
    {
        var game = Game();
        var rig = Rig(Move("MainBone", 0, 0.1f, 0));

        var reading = ModelRigReader.Read(Skeleton(Export(game, rig), game), game, null, null, "Fixture");

        Assert.Equal(RigSource.Bones, reading.Source);
        AssertRig(rig, reading.Rig);
    }

    [Fact]
    public void The_game_skeleton_reads_as_no_rig_edit()
    {
        var game = Game();

        var reading = ModelRigReader.Read(Skeleton(Export(game, null), game), game, null, null, "Fixture");

        Assert.Equal(RigSource.GameSkeleton, reading.Source);
        Assert.Empty(reading.Rig);
        Assert.Empty(reading.Warnings);
    }

    [Fact]
    public void Changes_below_the_tolerance_count_as_none()
    {
        var game = Game();
        var rig = Rig(Move("Neck2", 0.5f * ModelRigReader.MoveTolerance, 0, 0), Turn("Tail3", Vector3.UnitX, 0.5f * ModelRigReader.TurnToleranceDegrees));

        var reading = ModelRigReader.Read(Skeleton(Export(game, rig), game), game, null, null, "Fixture");

        Assert.Equal(RigSource.GameSkeleton, reading.Source);
        Assert.Empty(reading.Rig);
    }

    private static Dictionary<string, RigOffset> WithScaledTail()
    {
        var saved = R();
        var tail = saved["Tail1"];
        tail.Scale = new RigVector3(1.3f, 1.3f, 1.3f);
        saved["Tail1"] = tail;
        return saved;
    }

    [Fact]
    public void A_saved_rig_edit_with_scale_counts_as_the_same_when_the_bones_match()
    {
        var game = Game();
        var saved = WithScaledTail();
        var file = Skeleton(Export(game, saved), game);

        var reading = ModelRigReader.Read(file, game, saved, rigFile: null, "Fixture");

        Assert.True(ModelRigReader.Matches(file, game, saved));
        Assert.Equal(RigSource.Saved, reading.Source);
        Assert.Same(saved, reading.Rig);
        Assert.Empty(reading.Warnings);
    }

    [Fact]
    public void A_matching_rig_file_wins_and_keeps_its_scale()
    {
        var game = Game();
        var rigFile = WithScaledTail();
        var saved = Rig(Turn("Head", Vector3.UnitX, 30));

        var reading = ModelRigReader.Read(Skeleton(Export(game, rigFile), game), game, saved, rigFile, "Fixture", "Fixture.glb.rig.json");

        Assert.Equal(RigSource.RigFile, reading.Source);
        Assert.Same(rigFile, reading.Rig);
        Assert.Equal(new RigVector3(1.3f, 1.3f, 1.3f), reading.Rig["Tail1"].Scale);
        Assert.Empty(reading.Warnings);
    }

    [Fact]
    public void A_rig_file_that_does_not_match_the_bones_is_ignored_with_a_warning()
    {
        var game = Game();
        var rigFile = Rig(Turn("Head", Vector3.UnitX, 30));

        var reading = ModelRigReader.Read(Skeleton(Export(game, R()), game), game, null, rigFile, "Fixture", "Fixture.glb.rig.json");

        Assert.Equal(RigSource.Bones, reading.Source);
        AssertRig(R(), reading.Rig);
        Assert.Equal(["Fixture.glb.rig.json does not match this model's bones (it may be from an older export), so the rig edit was read from the bones and has no scale."],
            reading.Warnings);
    }

    [Fact]
    public void Reading_bones_over_a_scaled_saved_rig_edit_warns_that_scale_is_dropped()
    {
        var game = Game();
        var saved = WithScaledTail();
        var rig = Rig(Turn("Head", Vector3.UnitX, 30));

        var reading = ModelRigReader.Read(Skeleton(Export(game, rig), game), game, saved, null, "Fixture");

        Assert.Equal(RigSource.Bones, reading.Source);
        AssertRig(rig, reading.Rig);
        Assert.Equal(["The current rig edit scales Tail1; reading this model's bones cannot see scale, so using it drops that scale. Export with Export model for Tyrant… in Blender to keep it."],
            reading.Warnings);
    }

    [Fact]
    public void A_plain_export_of_a_scaled_bone_reads_as_moves_on_its_descendants()
    {
        var game = Game();
        var rig = Rig(("Tail1", Vector3.Zero, Quaternion.Identity, 1.3f));

        var file = Skeleton(Export(game, rig), game);
        var reading = ModelRigReader.Read(file, game, null, null, "Fixture");

        Assert.True(ModelRigReader.Matches(file, game, rig)); // the file built for the scaled rig edit still matches it
        // The scale also moves Tail1's own head (move + rotate(scale ⊙ p)); every descendant moves 0.3 × its local position.
        var tail = Renderer(game).Bones.Where(b => b.Name.StartsWith("Tail", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Tail1", "Tail2", "Tail3", "Tail4"], reading.Rig.Keys.Order());
        foreach (var bone in tail)
        {
            var got = reading.Rig[bone.Name];
            Assert.True(Angle(got.Rotate.ToNumerics()) < 0.01f, bone.Name);
        }
        Assert.True(reading.Rig["Tail3"].Move.ToNumerics().Length() > 0.3f * 0.35f - 1e-3f);
    }

    [Fact]
    public void Bones_missing_from_the_file_are_left_out_and_listed()
    {
        var game = Game();

        var reading = ModelRigReader.Read(Skeleton(Export(game, R(), leaveOut: "Tail2"), game), game, null, null, "Fixture");

        Assert.Equal(RigSource.Bones, reading.Source);
        AssertRig(R(), reading.Rig);
        Assert.Contains(reading.Warnings, w => w.Contains("'Tail2'", StringComparison.Ordinal));
    }

    [Fact]
    public void File_joints_that_are_not_the_games_skinned_bones_are_ignored()
    {
        // A default FBX export adds *_end leaf bones whose names are the game's unskinned tips, placed elsewhere.
        var game = Game();
        var renderer = Renderer(game);
        var tip = game.Root.DepthFirst().First(n => n.Name == "Tail4_end");
        var file = renderer with
        {
            Bones = [.. renderer.Bones, tip],
            Mesh = ModelFixture.SplitQuad([.. renderer.Mesh.BindPoses, Matrix4x4.CreateTranslation(0.3f, -2, 0.5f)]),
        };
        var path = TempGlb();
        GltfModelWriter.WriteGlb(game, file, path);

        var reading = ModelRigReader.Read(Skeleton(path, game), game, null, null, "Fixture");

        Assert.Equal(RigSource.GameSkeleton, reading.Source);
        Assert.Empty(reading.Warnings);
    }

    [Fact]
    public void The_main_meshs_skeleton_is_read_when_an_accessory_comes_first_in_the_file()
    {
        // Eyes skinned to two of the game's bones, first in the file, placed where the main mesh's bones are not.
        var game = Game();
        var renderer = Renderer(game);
        var eyes = new RendererModel("Eyes", ModelFixture.Triangle(name: "Eyes"), [.. renderer.Bones.Where(b => b.Name is "Neck3" or "Head")], renderer.Owner);
        var path = TempGlb();
        GltfModelWriter.WriteGlb(game, [eyes, renderer], path);

        var reading = ModelRigReader.Read(Skeleton(path, game), game, null, null, "Fixture");

        Assert.Equal(RigSource.GameSkeleton, reading.Source);
        Assert.Empty(reading.Warnings); // no bones "missing": the eyes are not the model
    }

    [Fact]
    public void A_skeleton_far_from_the_game_warns_it_may_be_another_species()
    {
        var game = Game();
        string[] turned = ["Hip", "Neck1", "Neck2", "Neck3", "Head", "Tail1", "Tail2", "Tail3", "Leg.L", "Shin.L", "Leg.R", "Shin.R"];
        var rig = Rig([.. turned.Select(b => Turn(b, Vector3.UnitZ, 20))]);
        rig["Hip"] = rig["Hip"] with { Move = new RigVector3(0, 0.3f, 0) };

        var reading = ModelRigReader.Read(Skeleton(Export(game, rig), game), game, null, null, "Tyrannosaurus");

        Assert.Equal(RigSource.Bones, reading.Source);
        Assert.Equal(["This model's skeleton is very different from Tyrannosaurus's (12 bones moved, up to 30 cm): was it exported from another species' project?"],
            reading.Warnings);

        // Fewer than 10 bones turned is a rig edit, however large a share of the skeleton.
        var fewer = Rig([.. turned.Take(9).Select(b => Turn(b, Vector3.UnitZ, 20))]);
        Assert.Empty(ModelRigReader.Read(Skeleton(Export(game, fewer), game), game, null, null, "Tyrannosaurus").Warnings);
    }

    [Fact]
    public void A_scaled_bind_pose_prefab_reads_its_edit_through_the_expected_frame()
    {
        // As Panthera atrox: bind poses scaled 100× and turned against the prefab's frame, the armature node scaled; Blender
        // writes the file upright and rescaled (game bind skeleton × G).
        var k = Matrix4x4.CreateRotationX(90 * Degrees) * Matrix4x4.CreateScale(0.01f) * Matrix4x4.CreateTranslation(0.0004f, 0, 0.0007f);
        var game = Game(k, armatureScale: 1.0490f);
        var rig = R();
        rig["MainBone"] = new RigOffset { Move = new RigVector3(0, 0, 0.1f), Rotate = RigQuaternion.Identity, Scale = RigVector3.One };

        var reading = ModelRigReader.Read(Skeleton(Export(game, rig, k), game), game, null, null, "Fixture");
        var unedited = ModelRigReader.Read(Skeleton(Export(game, null, k), game), game, null, null, "Fixture");

        Assert.Equal(RigSource.Bones, reading.Source);
        AssertRig(rig, reading.Rig);
        Assert.Equal(RigSource.GameSkeleton, unedited.Source);
    }

    [Fact]
    public void A_rig_file_sits_beside_the_model_with_its_full_name()
    {
        var glb = TempGlb();
        Directory.CreateDirectory(Path.GetDirectoryName(glb)!);
        File.WriteAllText(glb, "");

        Assert.Null(ModelRigReader.RigFilePathFor(glb));
        File.WriteAllText(glb + ".rig.json", "{}");
        Assert.Equal(glb + ".rig.json", ModelRigReader.RigFilePathFor(glb));
    }
}
