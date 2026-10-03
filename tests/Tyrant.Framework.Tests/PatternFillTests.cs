using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PatternFillTests
{
    private static Rgb C(string hex)
    {
        Rgb.TryParse(hex, out var c);
        return c;
    }

    [Fact]
    public void Values_come_from_the_set_and_fixed_colours_stay_fixed()
    {
        var set = new SkinColorSet { A = [C("#3060ff")], B = [C("#20c040")], Secondary = [C("#ffcc00")], Eye = [C("#ff2000")], Strength = new FloatRange(0.7f, 0.7f), Softness = new FloatRange(0.2f, 0.2f) };

        var v = PatternFill.Create(set, new Random(1));

        Assert.Equal((C("#3060ff"), C("#20c040"), C("#ffcc00"), C("#ff2000")), (v.A, v.B, v.Secondary, v.Eye));
        Assert.Equal((0.7f, 0.2f), (v.Strength, v.Softness));
    }

    [Fact]
    public void A_missing_a_or_b_uses_the_other()
    {
        var onlyA = PatternFill.Create(new SkinColorSet { A = [C("#3060ff")] }, new Random(1));
        var onlyB = PatternFill.Create(new SkinColorSet { B = [C("#20c040")] }, new Random(1));

        Assert.Equal(onlyA.A, onlyA.B);
        Assert.Equal(onlyB.B, onlyB.A);
    }

    [Fact]
    public void Unset_secondary_eye_strength_and_softness_use_defaults_within_their_ranges()
    {
        var v = PatternFill.Create(new SkinColorSet { A = [C("#3060ff")] }, new Random(7));

        Assert.Equal((PatternFill.DefaultSecondary, PatternFill.DefaultEye), (v.Secondary, v.Eye));
        Assert.InRange(v.Strength, PatternFill.DefaultStrength.Min, PatternFill.DefaultStrength.Max);
        Assert.InRange(v.Softness, PatternFill.DefaultSoftness.Min, PatternFill.DefaultSoftness.Max);
    }

    [Fact]
    public void Gradients_give_each_animal_a_point_on_them()
    {
        var set = new SkinColorSet { A = [C("#000000"), C("#ffffff")] };

        var reds = Enumerable.Range(0, 50).Select(i => PatternFill.Create(set, new Random(i)).A.R).ToList();

        Assert.True(reds.Min() < 0.3f && reds.Max() > 0.7f);
    }

    [Fact]
    public void Stored_colours_are_kept_and_unset_ones_are_filled()
    {
        Assert.True(PatternFill.NeedsColours(0f)); // a vanilla normal animal: the game never set pattern colours
        Assert.False(PatternFill.NeedsColours(1f)); // set before (saved): keep them on load
    }
}
