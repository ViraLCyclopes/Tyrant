using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SizeMathTests
{
    private static readonly (float, float)[] TRex = { (0f, 0.121f), (0.5f, 0.5f), (1f, 1f) };
    private static readonly (float, float)[] Allosaurus = { (0f, 0.164f), (1f, 1f) };
    private static readonly (float, float)[] Triceratops = { (0f, 0.2f), (1f, 0.77f), (2f, 1f) };
    private static readonly (float, float)[] Brachiosaurus = { (0f, 0.079f), (0.49f, 0.604f), (1f, 1f) };

    /// <summary>The game's final scale: curve(m) · (1 + (s·g − 1)·f(m)).</summary>
    private static double S((float, float)[] curve, float m, double s, double g) =>
        SizeMath.Evaluate(curve, m) * (1 + (s * g - 1) * SizeMath.Fade(m));

    [Fact]
    public void Fade_matches_game()
    {
        Assert.Equal(0.2f, SizeMath.Fade(0f), 4);
        Assert.Equal(1f, SizeMath.Fade(1f), 4);
        Assert.Equal(0.8f, SizeMath.Fade(0.5f), 4);
    }

    [Fact]
    public void Identity_sizes_keep_the_curve()
    {
        var rebuilt = SizeMath.Rebuild(TRex, 1, null);
        foreach (var m in new[] { 0f, 0.25f, 0.5f, 1f })
            Assert.Equal(SizeMath.Evaluate(TRex, m), SizeMath.Evaluate(rebuilt, m), 4);
    }

    [Fact]
    public void Adult_scales_final_size()
    {
        var rebuilt = SizeMath.Rebuild(Allosaurus, 0.85, null);
        foreach (var m in new[] { 0f, 0.3f, 0.7f, 1f })
            Assert.Equal(0.85 * SizeMath.Evaluate(Allosaurus, m), S(rebuilt, m, 0.85, 1), 3);
    }

    [Fact]
    public void Adult_keeps_scale_relative_to_adult_at_maturity()
    {
        var rebuilt = SizeMath.Rebuild(Allosaurus, 2, null);
        Assert.Equal(SizeMath.Evaluate(Allosaurus, 1), S(rebuilt, 1, 2, 1) / 2, 4);
    }

    [Fact]
    public void Baby_sets_the_hatchling_share()
    {
        var rebuilt = SizeMath.Rebuild(Allosaurus, 1, 0.3);
        Assert.Equal(0.3, S(rebuilt, 0, 1, 1), 4);
        Assert.Equal(1.0, S(rebuilt, 1, 1, 1), 4);
    }

    [Fact]
    public void Keys_past_one_kept()
    {
        var rebuilt = SizeMath.Rebuild(Triceratops, 1.2, null);
        Assert.Contains(rebuilt, k => k.Time == 2f);
        Assert.Equal(17, rebuilt.Count(k => k.Time <= 1f));
    }

    [Fact]
    public void Sex_multiplies_once()
    {
        var (male, female) = SizeMath.Sex(1.08, 1.12, new SizeSettings { Adult = 0.9, Male = 1.1 });
        Assert.Equal(1.0692, male, 4);
        Assert.Equal(1.008, female, 4);
    }

    [Fact]
    public void Extremes_include_babies_and_fade()
    {
        var rebuilt = SizeMath.Rebuild(Brachiosaurus, 0.6, null);
        var (min, _) = SizeMath.Extremes(rebuilt, new[] { 0.6 }, new[] { (1.0, 1.0) });
        Assert.True(min < 0.05f, "min was " + min);
    }

    [Fact]
    public void BackOff_brings_extremes_inside()
    {
        var skins = new[] { (1.0, 1.0) };
        var ranges = new[] { (0.85, 1.15) };
        var adult = SizeMath.BackOff(Allosaurus, 2, null, skins, 1.12, null, ranges);
        Assert.True(adult < 2 && adult >= 1);
        var rebuilt = SizeMath.Rebuild(Allosaurus, adult, null);
        var (male, female) = SizeMath.Sex(1, 1, new SizeSettings { Adult = adult, Male = 1.12 });
        var (min, max) = SizeMath.Extremes(rebuilt, new[] { male, female }, ranges);
        Assert.True(max <= 2f + 1e-4f && min >= 0.05f, $"extremes {min}..{max}");
    }

    [Fact]
    public void BackOff_returns_one_when_even_one_is_outside()
    {
        var adult = SizeMath.BackOff(Allosaurus, 2, null, new[] { (1.0, 1.0) }, 1, null, new[] { (0.1, 5.0) });
        Assert.Equal(1.0, adult);
    }
}
