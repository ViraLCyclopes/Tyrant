using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class LookDraftTests
{
    private static MaterialOverride Strength(float normal, float? albino = null) => new()
    {
        Iridescence = new IridescenceSettings { Strength = new VariantNumbers { Normal = normal, Albino = albino } },
    };

    [Fact]
    public void Albino_strength_edit_keeps_the_other_variants()
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.35f, 0.2f));
        draft.SetStrength("albino", 0.5f);

        var s = draft.Current.Iridescence!.Strength!;
        Assert.Equal(0.35f, s.Normal);
        Assert.Equal(0.5f, s.Albino);
        Assert.Null(s.Melanistic);
    }

    [Fact]
    public void A_single_strength_stays_single()
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.4f));
        draft.SetStrength("albino", 0.6f);

        var s = draft.Current.Iridescence!.Strength!;
        Assert.Equal(0.6f, s.Normal);
        Assert.Null(s.Albino);
    }

    [Fact]
    public void A_missing_variant_is_added()
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.35f, 0.2f));
        draft.SetStrength("leucistic", 0.9f);

        Assert.Equal(0.9f, draft.Current.Iridescence!.Strength!.Leucistic);
        Assert.Equal(0.2f, draft.Current.Iridescence.Strength.Albino);
    }

    [Fact]
    public void Draft_is_dirty_until_saved_and_reset_restores()
    {
        var draft = new LookDraft(LookScope.Species, Strength(0.4f));
        Assert.False(draft.IsDirty);

        draft.SetStrength("normal", 0.8f);
        draft.SetSetting("_Smoothness", MaterialValue.OfNumber(0.3f));
        Assert.True(draft.IsDirty);

        draft.Reset();
        Assert.False(draft.IsDirty);
        Assert.Equal(0.4f, draft.Current.Iridescence!.Strength!.Normal);
        Assert.Empty(draft.Current.Settings);

        draft.SetThinFilm(420);
        draft.MarkSaved();
        Assert.False(draft.IsDirty);
        draft.Reset();
        Assert.Equal(420, draft.Current.Iridescence!.Ramp!.ThinFilm!.Thickness);
    }

    [Fact]
    public void Variation_is_not_for_one_animal()
    {
        Assert.Throws<InvalidOperationException>(() => new LookDraft(LookScope.Animal, null).SetVariation(0.2f));
        var draft = new LookDraft(LookScope.Skin, null);
        draft.SetVariation(0.2f);
        Assert.Equal(0.2f, draft.Current.Iridescence!.Variation);
    }

    [Fact]
    public void Clearing_everything_leaves_an_empty_block()
    {
        var draft = new LookDraft(LookScope.Skin, null);
        draft.SetMask(new MaskSource { Everywhere = true });
        draft.SetShaping(0.1f, null, null);
        draft.SetFurMask(new MaskSource { File = "fur.png" }, false);
        Assert.True(draft.IsDirty);

        draft.SetMask(null);
        draft.SetShaping(null, null, null);
        draft.SetFurMask(null, false);
        Assert.True(draft.Current.IsEmpty);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void Fur_grow_is_kept_beside_the_masks_and_null_drops_it()
    {
        var draft = new LookDraft(LookScope.Animal, null);
        draft.SetFurGrow(true);
        Assert.True(draft.Current.Fur!.Grow);
        Assert.True(draft.IsDirty);

        draft.SetFurMask(new MaskSource { File = "fur.png" }, false);
        draft.SetFurMask(null, false);
        Assert.True(draft.Current.Fur!.Grow); // removing the mask keeps grow

        draft.SetFurGrow(false);
        Assert.False(draft.Current.Fur!.Grow);
        draft.SetFurGrow(null);
        Assert.Null(draft.Current.Fur);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void Iridescence_off_writes_zero_strength_and_on_restores_it()
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.4f, 0.2f));
        draft.SetIridescenceOn(false);
        Assert.Equal(0, draft.Current.Iridescence!.Strength!.Normal);
        Assert.Equal(0f, draft.Current.Iridescence.Strength.Albino);

        draft.SetIridescenceOn(true);
        Assert.Equal(1, draft.Current.Iridescence.Strength.Normal);
    }
}
