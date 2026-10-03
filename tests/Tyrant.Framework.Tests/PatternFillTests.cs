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

    private static StoredPattern Stored(Rgb a, float alphaA, Rgb b, float alphaB, float strength, float softness) =>
        new(a, alphaA, b, alphaB, C("#000000"), 0f, C("#000000"), 0f, strength, softness);

    [Fact]
    public void Stored_values_inside_the_palette_and_ranges_are_kept_on_load()
    {
        var set = new SkinColorSet { A = [C("#000000"), C("#ffffff")], B = [C("#20c040")], Strength = new FloatRange(0.6f, 0.8f), Softness = new FloatRange(0.1f, 0.4f) };
        var grey = C("#808080");

        var v = PatternFill.Keep(Stored(grey, 1f, C("#20c040"), 1f, 0.7f, 0.2f), set, new Random(3));

        Assert.Equal(grey, v.A); // on the black-to-white ramp: unchanged
        Assert.Equal((0.7f, 0.2f), (v.Strength, v.Softness));
    }

    [Fact]
    public void A_half_mixed_offspring_gets_its_missing_values_drawn_not_black_or_zero()
    {
        var set = new SkinColorSet { A = [C("#3060ff")], B = [C("#20c040")] }; // breeding mixed fields with a vanilla parent: B, strength, softness are 0

        var v = PatternFill.Keep(Stored(C("#3060ff"), 1f, C("#000000"), 0f, 0f, 0f), set, new Random(3));

        Assert.Equal(C("#20c040"), v.B);
        Assert.InRange(v.Strength, PatternFill.DefaultStrength.Min, PatternFill.DefaultStrength.Max);
        Assert.InRange(v.Softness, PatternFill.DefaultSoftness.Min, PatternFill.DefaultSoftness.Max);
        Assert.Equal(PatternFill.DefaultSecondary, v.Secondary);
    }

    [Fact]
    public void A_changed_palette_and_ranges_pull_stored_values_in()
    {
        var set = new SkinColorSet { A = [C("#ff0000")], Strength = new FloatRange(0.6f, 0.8f), Softness = new FloatRange(0.1f, 0.4f) }; // the modder changed A to red

        var v = PatternFill.Keep(Stored(C("#3060ff"), 1f, C("#3060ff"), 1f, 0.95f, 1f), set, new Random(3)); // e.g. the Nursery forced softness to 1

        Assert.Equal(C("#ff0000"), v.A);
        Assert.Equal((0.8f, 0.4f), (v.Strength, v.Softness));
    }
}
