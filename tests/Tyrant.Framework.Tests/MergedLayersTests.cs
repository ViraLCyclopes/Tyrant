using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MergedLayersTests
{
    private static MaterialOverride Block(string key, float value)
    {
        var block = new MaterialOverride();
        block.Settings[key] = MaterialValue.OfNumber(value);
        return block;
    }

    [Fact]
    public void The_same_layers_give_the_same_merged_object()
    {
        var cache = new MergedLayers();
        var skin = Block("_Smoothness", 0.5f);
        var species = Block("_RimIntensity", 1f);

        var first = cache.Get(null, null, null, skin, null, null, species);
        var second = cache.Get(null, null, null, skin, null, null, species);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(0.5f, first!.Settings["_Smoothness"].Number);
        Assert.Equal(1f, first.Settings["_RimIntensity"].Number);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Layers_are_told_apart_by_object_and_by_position()
    {
        var cache = new MergedLayers();
        var a = Block("_Smoothness", 0.1f);
        var b = Block("_Smoothness", 0.9f);
        var equalToA = Block("_Smoothness", 0.1f);

        var aOverB = cache.Get(a, null, null, null, null, null, b);
        var bOverA = cache.Get(b, null, null, null, null, null, a);
        var sameContent = cache.Get(equalToA, null, null, null, null, null, b);

        Assert.Equal(0.1f, aOverB!.Settings["_Smoothness"].Number);
        Assert.Equal(0.9f, bOverA!.Settings["_Smoothness"].Number);
        Assert.NotSame(aOverB, sameContent); // another object is another layer, even with the same content
        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void Clear_builds_a_new_result_with_the_same_content()
    {
        var cache = new MergedLayers();
        var skin = Block("_Smoothness", 0.5f);
        var before = cache.Get(null, null, null, skin, null, null, null);

        cache.Clear();
        Assert.Equal(0, cache.Count);
        var after = cache.Get(null, null, null, skin, null, null, null);

        Assert.Equal(1, cache.Count);
        Assert.NotSame(before, after);
        Assert.Equal(0.5f, after!.Settings["_Smoothness"].Number);
    }

    [Fact]
    public void No_layers_give_null_and_keep_nothing_and_empty_layers_give_null()
    {
        var cache = new MergedLayers();
        Assert.Null(cache.Get(null, null, null, null, null, null, null));
        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Get(new MaterialOverride(), null, null, null, null, null, null));
        Assert.Null(cache.Get(new MaterialOverride(), null, null, null, null, null, null));
    }

    [Fact]
    public void Results_match_merge_layers()
    {
        var cache = new MergedLayers();
        var live = new MaterialOverride { Iridescence = new IridescenceSettings { Scale = -0.5f } };
        var table = new MaterialOverride { Iridescence = new IridescenceSettings { Scale = 0.2f, Power = 0.3f } };
        table.Settings["_Smoothness"] = MaterialValue.OfNumber(0.4f);

        var cached = cache.Get(null, null, live, table, null, null, null)!;
        var merged = MaterialTable.MergeLayers(null, null, live, table, null, null, null)!;

        Assert.Equal(merged.Iridescence!.Scale, cached.Iridescence!.Scale);
        Assert.Equal(merged.Iridescence.Power, cached.Iridescence.Power);
        Assert.Equal(merged.Settings["_Smoothness"].Number, cached.Settings["_Smoothness"].Number);
    }

    [Fact]
    public void Past_capacity_the_results_are_let_go()
    {
        var cache = new MergedLayers();
        var kept = Block("_Smoothness", 0.5f);
        var first = cache.Get(kept, null, null, null, null, null, null);
        for (var i = 0; i < MergedLayers.Capacity; i++) cache.Get(Block("_Smoothness", i), null, null, null, null, null, null);

        Assert.True(cache.Count <= MergedLayers.Capacity);
        Assert.NotSame(first, cache.Get(kept, null, null, null, null, null, null));
    }
}
