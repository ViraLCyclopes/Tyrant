using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaterialSwapTests
{
    private static MaterialSwap Swap(params string[] replaced) => new(name => replaced.Contains(name, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Every_matching_property_is_swapped_and_others_are_left()
    {
        var swap = Swap("T_Fence_D");

        var plan = swap.Plan([("_MainTex", "T_Fence_D"), ("_BaseMap", "t_fence_d"), ("_BumpMap", "T_Fence_N"), ("_Detail", null)]);

        Assert.Equal([("_MainTex", "T_Fence_D"), ("_BaseMap", "t_fence_d")], plan);
    }

    [Fact]
    public void Each_material_is_looked_at_once()
    {
        var swap = Swap("T_Fence_D");

        Assert.True(swap.FirstLook(1));
        Assert.False(swap.FirstLook(1));
        Assert.True(swap.FirstLook(2));
    }

    [Fact]
    public void Slots_are_not_read_for_a_material_seen_before()
    {
        // The module reads a material's texture slots (costly Unity calls) only after FirstLook says it is new.
        var swap = Swap("T_Fence_D");
        swap.FirstLook(5);
        var read = 0;
        IEnumerable<(string, string?)> Slots() { read++; yield return ("_MainTex", "T_Fence_D"); }

        var plan = swap.FirstLook(5) ? swap.Plan(Slots()) : [];

        Assert.Empty(plan);
        Assert.Equal(0, read);
    }

    [Fact]
    public void Nothing_to_do_without_replacements()
    {
        Assert.False(new MaterialSwap(_ => false, hasReplacements: false).HasWork);
        Assert.True(Swap("x").HasWork);
    }

    [Theory]
    [InlineData("_MainTex", SlotKind.Color)]
    [InlineData("_BaseMap", SlotKind.Color)]
    [InlineData("_BumpMap", SlotKind.Normal)]
    [InlineData("_NormalMap", SlotKind.Normal)]
    [InlineData("_MaskMap", SlotKind.Data)]
    public void Property_names_say_what_kind_of_texture_they_hold(string property, SlotKind kind)
    {
        Assert.Equal(kind, MaterialSwap.KindOf(property));
    }
}
