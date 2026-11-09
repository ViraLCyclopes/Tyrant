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

    [Fact]
    public void Baby_strength_is_set_and_null_drops_it()
    {
        var draft = new LookDraft(LookScope.Skin, null);
        draft.SetBabyStrength(0.3f);
        Assert.Equal(0.3f, draft.Current.Iridescence!.BabyStrength);
        Assert.Null(draft.Current.Iridescence.Strength); // the adult strength is left to the layers below
        Assert.True(draft.IsDirty);

        draft.SetBabyStrength(null);
        Assert.Null(draft.Current.Iridescence);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void Fur_age_is_kept_beside_grow_and_null_drops_it()
    {
        var draft = new LookDraft(LookScope.Species, null);
        draft.SetFurAge(FurAge.Adults);
        Assert.Equal(FurAge.Adults, draft.Current.Fur!.Age);
        draft.SetFurGrow(true);
        draft.SetFurGrow(null);
        Assert.Equal(FurAge.Adults, draft.Current.Fur!.Age); // removing grow keeps the age

        draft.SetFurAge(null);
        Assert.Null(draft.Current.Fur);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void Iridescence_off_also_zeroes_a_baby_strength()
    {
        var draft = new LookDraft(LookScope.Skin, new MaterialOverride { Iridescence = new IridescenceSettings { BabyStrength = 0.3f } });
        draft.SetIridescenceOn(false);
        Assert.Equal(0f, draft.Current.Iridescence!.BabyStrength);
        Assert.Equal(0f, draft.Current.Iridescence.Strength!.Normal);
    }

    // IsDirty is remembered between changes: every change must make it look again, also right after it was read.
    public static TheoryData<string> Edits => new()
    {
        "strength", "baby", "variation", "film", "ramp", "mask", "shaping", "setting", "off", "furMask", "infantMask", "grow", "age",
    };

    private static void Edit(LookDraft draft, string edit)
    {
        switch (edit)
        {
            case "strength": draft.SetStrength("normal", 0.9f); break;
            case "baby": draft.SetBabyStrength(0.5f); break;
            case "variation": draft.SetVariation(0.7f); break;
            case "film": draft.SetThinFilm(500); break;
            case "ramp": draft.SetRamp(new RampSource { Game = true }); break;
            case "mask": draft.SetMask(new MaskSource { Everywhere = true }); break;
            case "shaping": draft.SetShaping(1, 2, 3); break;
            case "setting": draft.SetSetting("_Length", MaterialValue.OfNumber(0.5f)); break;
            case "off": draft.SetIridescenceOn(false); break;
            case "furMask": draft.SetFurMask(new MaskSource { Game = true }, infant: false); break;
            case "infantMask": draft.SetFurMask(new MaskSource { Game = true }, infant: true); break;
            case "grow": draft.SetFurGrow(true); break;
            case "age": draft.SetFurAge(FurAge.Babies); break;
            default: throw new ArgumentException(edit);
        }
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public void Every_edit_after_a_read_counts_as_a_change(string edit)
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.4f));
        Assert.False(draft.IsDirty); // read (and remembered) before the edit

        Edit(draft, edit);
        Assert.True(draft.IsDirty);

        draft.Reset();
        Assert.False(draft.IsDirty);
        Edit(draft, edit);
        Assert.True(draft.IsDirty);
        draft.MarkSaved();
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void An_edit_back_to_the_saved_value_is_clean_again()
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.4f));
        draft.SetStrength("normal", 0.8f);
        Assert.True(draft.IsDirty);
        draft.SetStrength("normal", 0.4f);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void A_direct_change_counts_once_touched()
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.4f));
        Assert.False(draft.IsDirty);
        draft.Current.Iridescence!.Strength!.Normal = 0.7f;
        draft.Touch();
        Assert.True(draft.IsDirty);
    }

    [Fact]
    public void Switching_off_and_on_again_is_clean_after_each_read()
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.4f, 0.2f));
        var toggle = new IridescenceSwitch();
        toggle.Off(draft);
        Assert.True(draft.IsDirty);

        toggle.On(draft, null, gameHasRamp: true); // puts the remembered strengths back by hand
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void A_preset_after_a_read_counts_as_a_change()
    {
        var draft = new LookDraft(LookScope.Species, null);
        Assert.False(draft.IsDirty);
        var preset = SpeciesPreset.Of("Microraptor", "Microraptor", [new PresetSkin { Shimmers = true, Strength = new VariantNumbers { Normal = 0.6f } }], null, hasRamp: true, hasMask: true);
        LookEditing.ApplyPreset(draft, PresetKind.Iridescence, preset, "Microraptor");
        Assert.True(draft.IsDirty);
    }
}
