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
    [InlineData(0.2f, 0.8f, 1.5f, 1.1f)] // like the game's math.lerp, the integument is not clamped
    [InlineData(0.2f, 0.8f, -1f, -0.4f)]
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

    /// <summary>The two fur slots of an animal: (infant, adult).</summary>
    private static (FurFill Infant, FurFill Adult) Slots(FurAge? age, bool grow = false, bool infantMap = false, bool adultMap = false,
        bool file = false, bool game = false) =>
        (AgeLooks.FurSlot(age, grow, infant: true, file, game, infantMap), AgeLooks.FurSlot(age, grow, infant: false, file, game, adultMap));

    [Fact]
    public void Acrocanthosaurus_adults_get_fur_and_lose_the_baby_fluff_through_the_masks()
    {
        // Acrocanthosaurus has a baby fur map only (its adult slot is the game's blank FurMaskEmpty).
        Assert.Equal((FurFill.Black, FurFill.White), Slots(FurAge.Adults, infantMap: true));
        Assert.Equal((FurFill.Leave, FurFill.Black), Slots(FurAge.Babies, infantMap: true)); // its own baby map stays
        Assert.Equal((FurFill.Leave, FurFill.White), Slots(FurAge.Both, infantMap: true));
    }

    [Fact]
    public void An_age_with_fur_keeps_its_own_mask_and_one_without_is_black_over_anything()
    {
        Assert.Equal((FurFill.Leave, FurFill.Leave), Slots(FurAge.Both, infantMap: true, adultMap: true)); // the game's maps stay
        Assert.Equal((FurFill.Black, FurFill.File), Slots(FurAge.Adults, file: true)); // a file serves the age with fur only
        Assert.Equal((FurFill.File, FurFill.Black), Slots(FurAge.Babies, file: true));
        Assert.Equal((FurFill.Leave, FurFill.Black), Slots(FurAge.Babies, game: true)); // "game": the game's own mask stays
        Assert.Equal((FurFill.Black, FurFill.Leave), Slots(FurAge.Adults, game: true, adultMap: false));
        Assert.Equal((FurFill.Black, FurFill.White), Slots(FurAge.Adults, grow: true)); // with an age, grow adds nothing
    }

    [Fact]
    public void Grow_alone_whitens_each_age_without_a_mask_and_nothing_set_leaves_the_game_s()
    {
        Assert.Equal((FurFill.White, FurFill.White), Slots(null, grow: true));
        Assert.Equal((FurFill.Leave, FurFill.White), Slots(null, grow: true, infantMap: true));
        Assert.Equal((FurFill.File, FurFill.File), Slots(null, grow: true, file: true));
        Assert.Equal((FurFill.Leave, FurFill.Leave), Slots(null, grow: true, game: true));
        Assert.Equal((FurFill.Leave, FurFill.Leave), Slots(null)); // vanilla stays vanilla
        Assert.Equal((FurFill.File, FurFill.File), Slots(null, file: true)); // a mask file alone, as before
    }

    [Fact]
    public void Tyrant_writes_the_fur_sheen_whenever_it_grows_fur_or_an_age_is_set()
    {
        Assert.True(AgeLooks.WritesFurSheen(new FurMasks { Grow = true }));
        Assert.True(AgeLooks.WritesFurSheen(new FurMasks { Age = FurAge.Babies }));
        Assert.False(AgeLooks.WritesFurSheen(new FurMasks { Grow = false }));
        Assert.False(AgeLooks.WritesFurSheen(null));
    }

    [Fact]
    public void A_fur_variant_on_a_furless_template_switches_to_texture_masking()
    {
        Assert.True(FurMasking.NeedsTextureMasking(tessellationFur: true, textureMasking: false)); // _FURMASKING_NONE: no mask read at all
        Assert.False(FurMasking.NeedsTextureMasking(tessellationFur: true, textureMasking: true)); // a furry species: already
        Assert.False(FurMasking.NeedsTextureMasking(tessellationFur: false, textureMasking: false)); // no fur variant: nothing drawn
        Assert.Equal((4f, 4f), FurMasking.DispScale(1, 1)); // the furless template's (1,1): a furry default
        Assert.Null(FurMasking.DispScale(3, 3)); // a scale of its own stays
    }

    [Fact]
    public void The_starting_fur_length_is_short_like_vanilla_fur()
    {
        Assert.Equal(0.15f, LookEditing.StartFurLength);
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
