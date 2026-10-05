using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class BlenderIkTests
{
    private static PrefabModel Quadruped()
    {
        var root = new SkeletonNode("Animal", Vector3.Zero, Quaternion.Identity, Vector3.One);
        SkeletonNode Add(string name, Vector3 at, SkeletonNode parent)
        {
            var n = new SkeletonNode(name, at, Quaternion.Identity, Vector3.One, parent);
            parent.Children.Add(n);
            return n;
        }
        var hip = Add("Hip", new Vector3(0, 1, 0), root);
        IkChain Leg(string a, string b, string c, Vector3 at) // no ".L"/".R" in the names: the side comes from X
        {
            var j0 = Add(a, at, hip);
            var j1 = Add(b, new Vector3(0, -0.5f, 0.1f), j0);
            var j2 = Add(c, new Vector3(0, -0.5f, 0), j1);
            return new IkChain(IkChainKind.Limb, 1f, [new IkJoint(j0, 0.5f, []), new IkJoint(j1, 0.5f, []), new IkJoint(j2, 0, [])], new Vector3(0.1f, 0, 0), false);
        }
        var neck = Add("Neck", new Vector3(0, 0.2f, 1.2f), hip);
        var head = Add("Head", new Vector3(0, 0.2f, 0.3f), neck);
        return new PrefabModel("Animal", root, [], [])
        {
            IkChains =
            [
                Leg("ArmA", "ForearmA", "HandA", new Vector3(-0.3f, 0, 1f)),   // front, Unity -X = left
                Leg("ArmB", "ForearmB", "HandB", new Vector3(0.3f, 0, 1f)),    // front, right
                Leg("FemurA", "CalveA", "FootA", new Vector3(-0.3f, 0, -1f)),  // back, left
                Leg("FemurB", "CalveB", "FootB", new Vector3(0.3f, 0, -1f)),   // back, right
                new IkChain(IkChainKind.Head, 1f, [new IkJoint(neck, 0.4f, []), new IkJoint(head, 0, [])], Vector3.Zero, false),
            ],
        };
    }

    [Fact]
    public void Bipeds_get_legs_and_a_head_with_the_controls_named_after_them()
    {
        var ik = BlenderIkReader.From(IkFixture.Prefab())!;

        Assert.Equal(["Leg L", "Leg R", "Head"], ik.Chains.Select(c => c.Name));
        Assert.Equal(new BlenderIkControls("ctrl_foot.L", "ctrl_knee.L", null), ik.Chains[0].Controls);
        Assert.Equal(new BlenderIkControls("ctrl_head", null, "ctrl_look"), ik.Chains[2].Controls);
        Assert.Equal("L", ik.Chains[0].Side);
        Assert.Null(ik.Chains[2].Side);
        Assert.Equal("limb", ik.Chains[0].Kind);
        Assert.Equal("head", ik.Chains[2].Kind);
        Assert.True(ik.Chains[2].MatchHeadRotation);
        Assert.Equal(["Femur.L", "Calve.L", "Foot.L", "Heel.L"], ik.Chains[0].Joints.Select(j => j.Name));
    }

    [Fact]
    public void Quadrupeds_front_legs_are_the_two_furthest_forward_and_get_hands_and_elbows()
    {
        var ik = BlenderIkReader.From(Quadruped())!;

        Assert.Equal(["Front leg L", "Front leg R", "Back leg L", "Back leg R", "Head"], ik.Chains.Select(c => c.Name));
        Assert.Equal(new BlenderIkControls("ctrl_hand.L", "ctrl_elbow.L", null), ik.Chains[0].Controls);
        Assert.Equal(new BlenderIkControls("ctrl_foot.R", "ctrl_knee.R", null), ik.Chains[3].Controls);
        Assert.True(ik.Chains[0].Front);
        Assert.False(ik.Chains[2].Front);
    }

    [Fact]
    public void Offsets_and_force_directions_are_written_in_gltf_space()
    {
        var quad = BlenderIkReader.From(Quadruped())!;
        Assert.Equal([-0.1f, 0f, 0f], quad.Chains[0].EndOffset); // Unity +X → glTF -X

        var ik = BlenderIkReader.From(IkFixture.Prefab())!;
        var force = Assert.Single(ik.Chains[0].Forces);
        Assert.Equal(("Calve.L", "Hip", 0.4f), (force.Joint, force.Bone, force.Strength));
        Assert.Equal([0f, 0f, 1f], force.Direction.Select(v => v == 0 ? 0f : v)); // -0 → 0
    }

    [Fact]
    public void The_pole_comes_from_the_knees_forces_or_the_legs_bend()
    {
        var ik = BlenderIkReader.From(IkFixture.Prefab())!;

        Assert.Equal("Hip", BlenderIkReader.PoleFrom(ik.Chains[0]));
        Assert.Equal("the leg's bend", BlenderIkReader.PoleFrom(ik.Chains[1]));
        Assert.Null(BlenderIkReader.PoleFrom(ik.Chains[2]));
    }

    [Fact]
    public void A_prefab_without_chains_has_no_ik()
    {
        Assert.Null(BlenderIkReader.From(new PrefabModel("Fence", new SkeletonNode("Fence", Vector3.Zero, Quaternion.Identity, Vector3.One), [], [])));
    }

    private sealed record Ctx(FakeGame Game, Workspace Ws, GameInstall Install, AssetIndex Index, IReadOnlyList<SpeciesSkins> Species, FakeAssetReader Reader) : IDisposable
    {
        public void Dispose() => Game.Dispose();
        public BlenderProjectResult Write(BlenderOpenRequest r) => BlenderProjectWriter.Write(r, Ws, Install, Index, Species, Reader, @"C:\Tyrant\tyrant.exe");
    }

    private static Ctx Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        SkinDumps.Write(ws.DataDir);
        return new Ctx(game, ws, install, new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] }, SpeciesSkinsReader.Load(ws),
            new FakeAssetReader { PrefabModelToReturn = IkFixture.Prefab() });
    }

    [Fact]
    public void Open_in_blender_writes_the_chains_and_builds_controls_by_default()
    {
        using var c = Setup();

        var project = BlenderProjectFile.Read(c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false)).ProjectFile);

        Assert.True(project.IkOnOpen);
        Assert.Equal(3, project.Ik!.Chains.Count);
    }

    [Fact]
    public void Without_ik_controls_the_chains_are_still_written_for_later()
    {
        using var c = Setup();

        var project = BlenderProjectFile.Read(c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false) { Ik = false }).ProjectFile);

        Assert.False(project.IkOnOpen);
        Assert.Equal(3, project.Ik!.Chains.Count);
    }

    [Fact]
    public void A_project_written_before_ik_reads_with_controls_on_and_no_chains()
    {
        using var c = Setup();
        var file = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false)).ProjectFile;
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json.Remove("ik");
        json.Remove("ikOnOpen");
        File.WriteAllText(file, json.ToJsonString());

        var project = BlenderProjectFile.Read(file);

        Assert.True(project.IkOnOpen);
        Assert.Null(project.Ik);
    }

    [Fact]
    public void Refresh_ik_writes_the_chains_into_an_older_project()
    {
        using var c = Setup();
        var file = c.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false)).ProjectFile;
        BlenderProjectFile.Write(file, BlenderProjectFile.Read(file) with { Ik = null });

        var count = BlenderService.RefreshIk(c.Install, c.Index, c.Species, c.Reader, file);

        Assert.Equal(3, count);
        Assert.Equal(3, BlenderProjectFile.Read(file).Ik!.Chains.Count);
    }
}
