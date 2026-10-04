using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaterialSwapTests
{
    private static MaterialSwap Swap(params string[] replaced) => new(name => replaced.Contains(name, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Every_matching_property_is_swapped_and_others_are_left()
    {
        var swap = Swap("T_Fence_D");

        var plan = swap.Plan(1, [("_MainTex", "T_Fence_D"), ("_BaseMap", "t_fence_d"), ("_BumpMap", "T_Fence_N"), ("_Detail", null)]);

        Assert.Equal([("_MainTex", "T_Fence_D"), ("_BaseMap", "t_fence_d")], plan);
    }

    [Fact]
    public void Seen_materials_are_skipped()
    {
        var swap = Swap("T_Fence_D");
        swap.Plan(1, [("_MainTex", "T_Fence_D")]);

        Assert.Empty(swap.Plan(1, [("_MainTex", "T_Fence_D")]));
        Assert.Single(swap.Plan(2, [("_MainTex", "T_Fence_D")]));
    }

    [Fact]
    public void A_material_without_replaced_textures_is_remembered_too()
    {
        var calls = 0;
        var swap = new MaterialSwap(_ => { calls++; return false; });
        swap.Plan(5, [("_MainTex", "T_Rock_D")]);
        swap.Plan(5, [("_MainTex", "T_Rock_D")]);

        Assert.Equal(1, calls);
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
