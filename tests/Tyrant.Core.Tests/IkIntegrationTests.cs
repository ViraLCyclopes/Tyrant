using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Models;
using Tyrant.Core.Species;

namespace Tyrant.Core.Tests;

/// <summary>The real game's FABRIK chains (TYRANT_GAME_DIR): bipeds have a head and two legs, quadrupeds a head and four.</summary>
[Trait("Category", "Integration")]
public class IkIntegrationTests(RealGameIndex real) : IClassFixture<RealGameIndex>
{
    private PrefabModel Read(string name)
    {
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(real.Index), name);
        using var session = new AssetSession(real.Install);
        return new ModelExporter().ReadPrefab(session, species.Prefab);
    }

    [SkippableFact]
    public void Every_animal_reads_three_or_five_chains_without_failures()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        using var session = new AssetSession(real.Install);
        var withChains = 0;
        foreach (var species in SpeciesCatalog.FromIndex(real.Index))
        {
            var prefab = new ModelExporter().ReadPrefab(session, species.Prefab);
            session.Release();
            Assert.True(prefab.IkFailures.Count == 0, $"{species.DisplayName}: {string.Join(" | ", prefab.IkFailures)}");
            if (prefab.IkChains.Count == 0) continue;
            withChains++;
            Assert.True(prefab.IkChains.Count is 3 or 5, $"{species.DisplayName}: {prefab.IkChains.Count} chains");
            Assert.Single(prefab.IkChains, c => c.Kind == IkChainKind.Head);
        }
        Assert.True(withChains >= 60, $"only {withChains} species have IK chains");
    }

    [SkippableFact]
    public void Carcharodontosaurus_has_a_head_and_two_legs()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var ik = BlenderIkReader.From(Read("Carcharodontosaurus"))!;

        Assert.Equal(["Head", "Leg L", "Leg R"], ik.Chains.Select(c => c.Name).Order());
        var head = ik.Chains.Single(c => c.Kind == "head");
        Assert.Equal(["Spine.003", "Neck", "Neck.001", "Neck.002", "Neck.003", "Neck.004", "Head"], head.Joints.Select(j => j.Name));
        Assert.True(head.MatchHeadRotation);
        var leg = ik.Chains.Single(c => c.Name == "Leg L");
        Assert.Equal(["Femur.L", "Calve.L", "Foot.L", "Heel.L"], leg.Joints.Select(j => j.Name));
        Assert.Equal(new BlenderIkControls("ctrl_foot.L", "ctrl_knee.L", null), leg.Controls);
        Assert.Equal("Pelvis", BlenderIkReader.PoleFrom(leg));
    }

    [SkippableFact]
    public void Stegosaurus_has_front_and_back_legs()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var ik = BlenderIkReader.From(Read("Stegosaurus Stenops"))!;

        Assert.Equal(["Back leg L", "Back leg R", "Front leg L", "Front leg R", "Head"], ik.Chains.Select(c => c.Name).Order());
        var front = ik.Chains.Single(c => c.Name == "Front leg L");
        Assert.Equal("Arm.L", front.Joints[0].Name);
        Assert.Equal(new BlenderIkControls("ctrl_hand.L", "ctrl_elbow.L", null), front.Controls);
        Assert.Equal("the leg's bend", BlenderIkReader.PoleFrom(front)); // Stegosaurus has no forces
    }

    [SkippableFact]
    public void Panthera_front_legs_take_their_pole_from_the_spine()
    {
        Skip.If(RealGameIndex.GameDir is null, "TYRANT_GAME_DIR not set");
        var ik = BlenderIkReader.From(Read("pantheraspelaea"))!;

        Assert.Equal("Spine.002", BlenderIkReader.PoleFrom(ik.Chains.Single(c => c.Name == "Front leg R")));
    }
}
