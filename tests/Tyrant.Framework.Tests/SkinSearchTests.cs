using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SkinSearchTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("tiger", true)]
    [InlineData("SUNSET", true)]
    [InlineData("sun tig", true)] // every word must match, in any order
    [InlineData("tig sun", true)]
    [InlineData("test skins", true)] // the mod's name counts too
    [InlineData("stripes", false)]
    [InlineData("tiger stripes", false)]
    public void Every_word_must_appear_in_the_skin_or_mod_name(string query, bool expected)
    {
        Assert.Equal(expected, SkinSearch.Matches(query, "Sunset tiger", "Test skins"));
    }

    [Fact]
    public void Vanilla_skins_have_no_mod_name()
    {
        Assert.True(SkinSearch.Matches("harle", "Harlequin", null));
        Assert.False(SkinSearch.Matches("test", "Harlequin", null));
    }
}
