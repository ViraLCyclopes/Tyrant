using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaterialStrengthTests
{
    [Fact]
    public void The_spread_is_stable_per_animal_and_stays_in_range()
    {
        Assert.Equal(MaterialStrength.Spread(42), MaterialStrength.Spread(42));
        Assert.NotEqual(MaterialStrength.Spread(42), MaterialStrength.Spread(43));
        var all = Enumerable.Range(0, 1000).Select(i => MaterialStrength.Spread((ulong)i)).ToList();
        Assert.All(all, s => Assert.True(s >= -1f && s < 1f));
        Assert.InRange(all.Average(), -0.1, 0.1);
    }

    [Fact]
    public void Strength_varies_around_the_set_value_and_is_clamped()
    {
        Assert.Equal(0.4f, MaterialStrength.For(0.4f, 0f, 7));
        Assert.Equal(0.4f, MaterialStrength.For(0.4f, 0.15f, null));
        var id = Enumerable.Range(0, 10000).Select(i => (ulong)i).First(i => MaterialStrength.Spread(i) > 0.2f);
        Assert.InRange(MaterialStrength.For(0.4f, 0.15f, id), 0.34f, 0.46f);
        Assert.Equal(1f, MaterialStrength.For(0.95f, 1f, id));
    }
}
