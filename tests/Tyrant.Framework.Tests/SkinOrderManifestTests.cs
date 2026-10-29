using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SkinOrderManifestTests
{
    [Fact]
    public void Skin_order_round_trips()
    {
        var m = ModManifest.Parse("""{"format":1,"id":"mod","name":"M","skinOrder":{"tyrant.mod.rex":["b","a"]}}""");
        Assert.Equal(new[] { "b", "a" }, m.SkinOrders["tyrant.mod.rex"]);

        var again = ModManifest.Parse(m.ToJson());
        Assert.Equal(new[] { "b", "a" }, again.SkinOrders["tyrant.mod.rex"]);
    }

    [Fact]
    public void No_skin_order_writes_none()
    {
        var m = ModManifest.Parse("""{"format":1,"id":"mod","name":"M"}""");
        Assert.DoesNotContain("\"skinOrder\"", m.ToJson());
    }

    [Theory]
    [InlineData("""{"k":[1]}""")]
    [InlineData("[]")]
    [InlineData("""{"k":["a","a"]}""")]
    public void A_bad_skin_order_is_an_error_for_Check_and_ignored_in_game(string block)
    {
        var json = $$"""{"format":1,"id":"mod","name":"M","skinOrder":{{block}}}""";
        Assert.Throws<ManifestException>(() => ModManifest.Parse(json));

        var skipped = new List<string>();
        var m = ModManifest.Parse(json, skipped);
        Assert.Empty(m.SkinOrders);
        Assert.Single(skipped);
        Assert.StartsWith("\"skinOrder\" was ignored:", skipped[0]);
    }
}
