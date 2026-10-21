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
        Assert.True(max < 2f && min > 0.05f, $"extremes {min}..{max}");
    }

    [Fact]
    public void BackOff_returns_one_when_even_one_is_outside()
    {
        var adult = SizeMath.BackOff(Allosaurus, 2, null, new[] { (1.0, 1.0) }, 1, null, new[] { (0.1, 5.0) });
        Assert.Equal(1.0, adult);
    }

    [Fact]
    public void BackOff_keeps_inside_a_wider_limit()
    {
        var skins = new[] { (1.0, 1.0) };
        var ranges = new[] { (0.85, 1.15) };
        var limit = new SizeLimit(0.05f, 20f);

        Assert.Equal(4.0, SizeMath.BackOff(Allosaurus, 4, null, skins, null, null, ranges, limit));
        Assert.True(SizeMath.BackOff(Allosaurus, 4, null, skins, null, null, ranges) < 2, "the game's limit is the default");

        var adult = SizeMath.BackOff(Allosaurus, 20, null, skins, null, null, ranges, limit); // 20 x 1.15 would pass 20
        Assert.True(adult < 20 && adult > 15, "adult was " + adult);
        var (min, max) = SizeMath.Extremes(SizeMath.Rebuild(Allosaurus, adult, null), new[] { adult }, ranges);
        Assert.True(max <= limit.SafeMax && max > 19.9f && min >= limit.SafeMin, $"extremes {min}..{max}");
    }

    [Theory]
    [InlineData(0.3f, 1.5f, 0.05f, 2f)]      // within the game's: the game's
    [InlineData(0.3f, 2.964f, 0.05f, 3f)]    // up to the next 0.5
    [InlineData(0.3f, 2.9985f, 0.05f, 3.5f)] // past the 2.997 the game backs off to under 3: the next step
    [InlineData(0.3f, 1.999f, 0.05f, 2.5f)]  // past the game's own 1.998
    [InlineData(0.024f, 1f, 0.02f, 2f)]      // down to the next 0.01
    [InlineData(0.030015f, 1f, 0.02f, 2f)]   // under the 0.03003 the game backs off to over 0.03
    [InlineData(0.001f, 30f, 0.01f, 20f)]    // Tyrant's own bounds
    public void The_limit_follows_the_extremes(float min, float max, float lo, float hi)
    {
        var limit = SizeLimit.Fitting(min, max);

        Assert.Equal((lo, hi), (limit.Min, limit.Max));
        if (max <= 19.9f && min >= 0.011f) Assert.True(min >= limit.SafeMin && max <= limit.SafeMax, $"{limit} keeps {min}..{max} in its safe band");
        Assert.Equal(min < SizeLimit.LowestMin || max > SizeLimit.HighestMax, SizeLimit.PastBounds(min, max));
    }

    [Fact]
    public void Extremes_pinned_for_the_size_cards_copy()
    {
        // studio/src/lib/mods/editor/sizeMath.test.ts counts the same fixture with its copy of these maths: the numbers must match.
        (float, float)[] curve = { (0f, 0.25f), (1f, 1f) };
        var settings = new SizeSettings { Adult = 2, Baby = 0.1, Female = 1.2 };
        var sexes = new[] { (1.1, 1.0) }.SelectMany(k => { var (m, f) = SizeMath.Sex(k.Item1, k.Item2, settings); return new[] { m, f }; });

        var (min, max) = SizeMath.Extremes(SizeMath.Rebuild(curve, 2, 0.1), sexes, new[] { (0.925, 1.075), (0.5, 3.0), (0.85, 1.15) });

        Assert.Equal(0.17, min, 5);
        Assert.Equal(7.2, max, 4);
    }

    [Fact]
    public void Safe_limits_keep_their_margin_inside_the_limit()
    {
        Assert.Equal((0.05f, 2f), (SizeLimit.Game.Min, SizeLimit.Game.Max));
        Assert.Equal(2f * (1 - SizeMath.SafeMargin), SizeLimit.Game.SafeMax, 6);
        Assert.Equal(20f * (1 - SizeMath.SafeMargin), new SizeLimit(0.05f, 20f).SafeMax, 5);
        Assert.Equal(0.01f * (1 + SizeMath.SafeMargin), new SizeLimit(0.01f, 2f).SafeMin, 7);
        Assert.True(SizeLimit.Game.IsGame);
        Assert.False(new SizeLimit(0.05f, 20f).IsGame);
        Assert.True(new SizeLimit(0.05f, 20f).Contains(0.05f, 20f));
        Assert.False(SizeLimit.Game.Contains(0.05f, 2.01f));
    }

    // The dumped growth curves (time, value, in and out tangent): Hermite curves whose shape a straight line between keys loses.
    private static readonly (float Time, float Value, float In, float Out)[] VelociraptorGame =
        { (0f, 0.393908322f, 1.73721325f, 1.73721325f), (1f, 1f, 0.0356990322f, 0.0356990322f) };
    private static readonly (float Time, float Value, float In, float Out)[] BrachiosaurusGame =
        { (0f, 0.07884467f, 0.6078889f, 0.6078889f), (0.491450667f, 0.6044929f, 1.6740396f, 1.6740396f), (1f, 1f, 0.03651018f, 0.03651018f) };

    /// <summary>Unity's AnimationCurve.Evaluate for unweighted keys (as the growth curves are): Hermite between keys, clamped outside them.</summary>
    private static float Hermite((float Time, float Value, float In, float Out)[] keys, float t)
    {
        if (t <= keys[0].Time) return keys[0].Value;
        if (t >= keys[^1].Time) return keys[^1].Value;
        var i = 0;
        while (t > keys[i + 1].Time) i++;
        var (a, b) = (keys[i], keys[i + 1]);
        var dt = b.Time - a.Time;
        var s = (t - a.Time) / dt;
        return (2 * s * s * s - 3 * s * s + 1) * a.Value + (s * s * s - 2 * s * s + s) * dt * a.Out
            + (-2 * s * s * s + 3 * s * s) * b.Value + (s * s * s - s * s) * dt * b.In;
    }

    /// <summary>The curve the game builds from rebuilt keys (SizesModule.SetCurve): SizePlan's slopes as both tangents.</summary>
    private static (float Time, float Value, float In, float Out)[] AsGameCurve((float Time, float Value)[] keys)
    {
        var slopes = SizePlan.Slopes(keys);
        return keys.Select((k, i) => (k.Time, k.Value, slopes[i], slopes[i])).ToArray();
    }

    private static (float Time, float Value)[] Read((float Time, float Value, float In, float Out)[] game) =>
        SizeMath.Sample(game.Select(k => k.Time), t => Hermite(game, t));

    [Theory]
    [InlineData("Velociraptor", 0.5f)]
    [InlineData("Brachiosaurus", 0.25f)]
    public void Identity_sizes_keep_the_games_hermite_shape(string species, float m)
    {
        var game = species == "Velociraptor" ? VelociraptorGame : BrachiosaurusGame;
        var expected = Hermite(game, m);

        var rebuilt = SizeMath.Rebuild(Read(game), 1, null);

        Assert.InRange(SizeMath.Evaluate(rebuilt, m) / expected, 0.99, 1.01);
        Assert.InRange(Hermite(AsGameCurve(rebuilt), m) / expected, 0.99, 1.01);
    }

    [Theory]
    [InlineData("Velociraptor", 1.2)]
    [InlineData("Velociraptor", 0.6)]
    [InlineData("Brachiosaurus", 1.2)]
    [InlineData("Brachiosaurus", 0.6)]
    public void A_sized_species_grows_along_the_games_shape(string species, double adult)
    {
        var game = species == "Velociraptor" ? VelociraptorGame : BrachiosaurusGame;
        var curve = AsGameCurve(SizeMath.Rebuild(Read(game), adult, null));

        for (var i = 0; i <= 100; i++)
        {
            var m = i / 100f;
            var final = Hermite(curve, m) * (1 + (adult - 1) * SizeMath.Fade(m)); // S(m, adult, 1) as the game computes it
            Assert.InRange(final / (adult * Hermite(game, m)), 0.99, 1.01);
        }
    }

    [Fact]
    public void Sample_reads_the_curve_at_33_times_over_0_to_1_and_at_each_key()
    {
        var sampled = SizeMath.Sample(new[] { 0f, 0.4f, 1f, 2f }, t => t * t);

        Assert.Equal(35, sampled.Length); // 0, 1/32 … 1, plus the keys at 0.4 and 2
        Assert.Equal(sampled.OrderBy(k => k.Time).Select(k => k.Time), sampled.Select(k => k.Time));
        Assert.Contains((0.4f, 0.4f * 0.4f), sampled);
        Assert.Contains((2f, 4f), sampled);
        Assert.Empty(SizeMath.Sample(Array.Empty<float>(), t => t));
    }
}
