using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class AgeLooksTests
{
    [Theory]
    // baby, adult, integument (the game's growth of the skin's covering, 0 a hatchling, 1 grown), shown
    [InlineData(0f, 0.6f, 0f, 0f)] // left out, the baby strength is 0: the game's own (it lerps from 0)
    [InlineData(0f, 0.6f, 0.5f, 0.3f)]
    [InlineData(0f, 0.6f, 1f, 0.6f)]
    [InlineData(0.3f, 0f, 0f, 0.3f)] // a baby shimmer that goes away
    [InlineData(0.3f, 0f, 0.5f, 0.15f)]
    [InlineData(0.3f, 0f, 1f, 0f)]
    [InlineData(0.4f, 0.4f, 0.5f, 0.4f)] // the same at every age
    [InlineData(0.2f, 0.8f, 0.5f, 0.5f)]
    [InlineData(0.2f, 0.8f, 1.5f, 0.8f)] // the integument is kept within 0..1
    [InlineData(0.2f, 0.8f, -1f, 0.2f)]
    public void The_shown_strength_slides_from_the_baby_s_to_the_adult_s(float baby, float adult, float integument, float shown)
    {
        Assert.Equal(shown, AgeLooks.Strength(baby, adult, integument), 5);
    }

    [Fact]
    public void A_baby_strength_turns_iridescence_on_when_nothing_else_does()
    {
        var baby = new IridescenceSettings { BabyStrength = 0.3f };
        Assert.True(AgeLooks.Shimmers(baby, skinType: 0));
        Assert.True(AgeLooks.Shimmers(new IridescenceSettings { BabyStrength = 0.3f, Strength = new VariantNumbers { Normal = 0 } }, skinType: 0));
        Assert.True(AgeLooks.Shimmers(new IridescenceSettings { Strength = new VariantNumbers { Normal = 0, Albino = 0.5f } }, skinType: 1));
        Assert.False(AgeLooks.Shimmers(new IridescenceSettings { Strength = new VariantNumbers { Normal = 0, Albino = 0.5f } }, skinType: 0));
        Assert.False(AgeLooks.Shimmers(new IridescenceSettings { BabyStrength = 0 }, skinType: 0));
        Assert.False(AgeLooks.Shimmers(new IridescenceSettings { Scale = 1 }, skinType: 0)); // shaping alone leaves the game's choice
        Assert.False(AgeLooks.Shimmers(null, skinType: 0));
    }

    [Fact]
    public void Tyrant_writes_the_strength_when_a_layer_sets_either_strength()
    {
        Assert.True(AgeLooks.WritesStrength(new IridescenceSettings { BabyStrength = 0 }));
        Assert.True(AgeLooks.WritesStrength(new IridescenceSettings { Strength = new VariantNumbers() }));
        Assert.False(AgeLooks.WritesStrength(new IridescenceSettings { Bias = 1 }));
        Assert.False(AgeLooks.WritesStrength(null));
    }

    [Theory]
    // age, grow, the fuzz at a hatchling and at a grown animal; null: the game's own (Tyrant writes none)
    [InlineData(FurAge.Babies, false, 1f, 0f)]
    [InlineData(FurAge.Adults, false, 0f, 1f)]
    [InlineData(FurAge.Both, false, 1f, 1f)]
    [InlineData(FurAge.Babies, true, 1f, 0f)] // the age decides which stages, grow adds nothing
    [InlineData(FurAge.Adults, true, 0f, 1f)]
    [InlineData(null, true, 1f, 1f)] // grow alone: fur at every age, as before
    public void The_fuzz_ends_follow_the_fur_age(FurAge? age, bool grow, float baby, float adult)
    {
        Assert.Equal((baby, adult), AgeLooks.FuzzEnds(age, grow));
    }

    [Fact]
    public void Without_an_age_or_grow_the_fuzz_is_the_game_s_from_its_fur_maps()
    {
        Assert.Null(AgeLooks.FuzzEnds(null, grow: false));
        Assert.Equal((1f, 0f), AgeLooks.GameFuzzEnds(babyMap: true, adultMap: false)); // Acrocanthosaurus: a baby fur map only
        Assert.Equal((0f, 1f), AgeLooks.GameFuzzEnds(babyMap: false, adultMap: true));
        Assert.Equal((1f, 1f), AgeLooks.GameFuzzEnds(babyMap: true, adultMap: true));
        Assert.Equal((0f, 0f), AgeLooks.GameFuzzEnds(babyMap: false, adultMap: false));
    }

    [Theory]
    [InlineData(1f, 0f, 0f, 1f)]
    [InlineData(1f, 0f, 0.25f, 0.75f)]
    [InlineData(1f, 0f, 1f, 0f)]
    [InlineData(0f, 1f, 0.5f, 0.5f)]
    [InlineData(1f, 1f, 0.5f, 1f)]
    public void The_fuzz_lerps_between_its_ends_like_the_game(float baby, float adult, float integument, float fuzz)
    {
        Assert.Equal(fuzz, AgeLooks.Fuzz((baby, adult), integument), 5);
    }

    [Theory]
    // age, grow, does Tyrant supply the baby's / the adult's fur where the game has none
    [InlineData(FurAge.Babies, false, true, false)]
    [InlineData(FurAge.Adults, false, false, true)] // Acrocanthosaurus adults: a white adult mask
    [InlineData(FurAge.Both, false, true, true)]
    [InlineData(FurAge.Adults, true, false, true)] // with an age, grow adds no stage
    [InlineData(null, true, true, true)]
    [InlineData(null, false, false, false)]
    public void The_stages_that_need_grown_fur_follow_the_age_else_grow(FurAge? age, bool grow, bool baby, bool adult)
    {
        Assert.Equal((baby, adult), AgeLooks.GrownStages(age, grow));
    }

    [Fact]
    public void The_fur_variant_is_needed_for_grow_or_an_age()
    {
        Assert.True(AgeLooks.NeedsFurVariant(new FurMasks { Grow = true }));
        Assert.True(AgeLooks.NeedsFurVariant(new FurMasks { Age = FurAge.Babies }));
        Assert.True(AgeLooks.NeedsFurVariant(new FurMasks { Age = FurAge.Adults, Grow = false })); // the age wins over grow false
        Assert.False(AgeLooks.NeedsFurVariant(new FurMasks { Grow = false }));
        Assert.False(AgeLooks.NeedsFurVariant(new FurMasks { Mask = new MaskSource { File = "m.png" } }));
        Assert.False(AgeLooks.NeedsFurVariant(null));
    }
}
