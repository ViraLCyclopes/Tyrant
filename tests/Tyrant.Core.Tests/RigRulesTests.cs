using Tyrant.Core.Rigging;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class RigRulesTests
{
    private static readonly RigInfo Info = new(["Pelvis", "Jaw", "Neck.002", "Calve.L"], ["Pelvis"], ["Neck.002"], ["Calve.L"], []);

    private static Dictionary<string, RigOffset> Rig(params string[] bones) => bones.ToDictionary(b => b, _ => RigOffset.Identity);

    [Fact]
    public void A_skins_neck_or_head_edit_warns_that_the_head_look_is_per_species()
    {
        var info = Info with { HeadLook = ["Spine", "Neck.001", "Neck.002", "Head"] };

        var (_, skin) = RigRules.Problems(Rig("Neck.002", "Jaw"), info, hasModel: true, "Skin long", perSkin: true);
        var (_, species) = RigRules.Problems(Rig("Neck.002"), info, hasModel: true, "Rig edit of Styracosaurus");
        var (_, elsewhere) = RigRules.Problems(Rig("Jaw"), info, hasModel: true, "Skin long", perSkin: true);

        Assert.Contains(skin, w => w.StartsWith("Skin long", StringComparison.Ordinal) && w.Contains("Neck.002") && w.Contains("whole species"));
        Assert.DoesNotContain(species, w => w.Contains("head look"));
        Assert.DoesNotContain(elsewhere, w => w.Contains("head look"));
    }

    [Fact]
    public void Bones_not_in_the_skeleton_are_errors()
    {
        var (errors, _) = RigRules.Problems(Rig("Jaw", "Tail.099"), Info, hasModel: true, "Skin long");

        Assert.Contains(errors, e => e.Contains("Tail.099") && e.StartsWith("Skin long", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains("Jaw"));
    }

    [Fact]
    public void Bones_the_animations_move_are_warnings()
    {
        var (errors, warnings) = RigRules.Problems(Rig("Pelvis"), Info, hasModel: true, "Skin long");

        Assert.Empty(errors);
        Assert.Contains(warnings, w => w.Contains("Pelvis") && w.Contains("animations"));
    }

    [Fact]
    public void Bones_the_growth_positions_or_scales_follow_the_frameworks_support()
    {
        var (errors, warnings) = RigRules.Problems(Rig("Neck.002", "Calve.L"), Info, hasModel: true, "Skin long");

        var lines = RigLimits.GrowthBonesSupported ? warnings : errors;
        Assert.Contains(lines, l => l.Contains("Neck.002") && l.Contains("Calve.L") && l.Contains("growth"));
    }

    [Fact]
    public void A_rig_edit_without_its_own_model_is_a_note()
    {
        var (_, warnings) = RigRules.Problems(Rig("Jaw"), Info, hasModel: false, "Carcharodontosaurus");

        Assert.Contains(warnings, w => w.Contains("no model of its own"));
        Assert.DoesNotContain(RigRules.Problems(Rig("Jaw"), Info, hasModel: true, "x").Warnings, w => w.Contains("no model of its own"));
    }
}
