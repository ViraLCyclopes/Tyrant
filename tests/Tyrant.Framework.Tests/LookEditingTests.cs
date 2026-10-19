using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class LookEditingTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static MaterialOverride Strength(float normal, float? albino = null) => new()
    {
        Iridescence = new IridescenceSettings { Strength = new VariantNumbers { Normal = normal, Albino = albino } },
    };

    [Fact]
    public void A_species_never_offers_a_mask_from_a_skin_slot()
    {
        Assert.DoesNotContain(MaskKind.Channel, LookEditing.MaskKinds(LookScope.Species));
        Assert.Contains(MaskKind.Channel, LookEditing.MaskKinds(LookScope.Skin));
        Assert.Contains(MaskKind.Channel, LookEditing.MaskKinds(LookScope.Animal));
        foreach (var scope in new[] { LookScope.Animal, LookScope.Skin, LookScope.Species })
        {
            Assert.Contains(MaskKind.Everywhere, LookEditing.MaskKinds(scope));
            Assert.Contains(MaskKind.Game, LookEditing.MaskKinds(scope));
            Assert.Contains(MaskKind.File, LookEditing.MaskKinds(scope));
        }
    }

    [Fact]
    public void Ramp_and_mask_kinds_follow_the_block()
    {
        Assert.Equal(RampKind.Game, LookEditing.RampKindOf(null));
        Assert.Equal(RampKind.ThinFilm, LookEditing.RampKindOf(new RampSource { ThinFilm = new ThinFilmSettings { Thickness = 300 } }));
        Assert.Equal(RampKind.File, LookEditing.RampKindOf(new RampSource { File = "r.png" }));
        Assert.Equal(RampKind.Stops, LookEditing.RampKindOf(new RampSource { Stops = new List<Rgb>() }));
        Assert.Equal(MaskKind.Game, LookEditing.MaskKindOf(null));
        Assert.Equal(MaskKind.Everywhere, LookEditing.MaskKindOf(new MaskSource { Everywhere = true }));
        Assert.Equal(MaskKind.File, LookEditing.MaskKindOf(new MaskSource { File = "m.png" }));
        Assert.Equal(MaskKind.Channel, LookEditing.MaskKindOf(new MaskSource { From = "extra", Channel = 'g' }));
    }

    [Theory]
    [InlineData("0.5", 0.5f)]
    [InlineData("0,25", 0.25f)]
    [InlineData(" 1 ", 1f)]
    [InlineData("7", 2f)]
    [InlineData("-3", -1f)]
    public void Typed_values_parse_in_either_decimal_style_and_are_clamped(string text, float expected)
    {
        Assert.True(LookEditing.TryParse(text, -1, 2, out var value));
        Assert.Equal(expected, value, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(null)]
    [InlineData("NaN")]
    public void Text_that_is_no_number_is_refused(string? text)
    {
        Assert.False(LookEditing.TryParse(text, 0, 1, out _));
    }

    [Fact]
    public void Values_show_with_at_most_three_decimals()
    {
        Assert.Equal("0.123", LookEditing.Format(0.123456f));
        Assert.Equal("2", LookEditing.Format(2f));
        Assert.Equal("-0.25", LookEditing.Format(-0.25f));
        Assert.Equal("420", LookEditing.Format(420f));
    }

    [Fact]
    public void Turning_iridescence_off_and_on_keeps_the_per_variant_strengths()
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.35f, 0.2f));
        var toggle = new IridescenceSwitch();

        toggle.Off(draft);
        Assert.Equal(0f, draft.Current.Iridescence!.Strength!.Normal);
        Assert.Equal(0f, draft.Current.Iridescence.Strength.Albino);

        toggle.On(draft, draft.Current, gameHasRamp: false);
        Assert.Equal(0.35f, draft.Current.Iridescence!.Strength!.Normal);
        Assert.Equal(0.2f, draft.Current.Iridescence.Strength.Albino);
        Assert.Null(draft.Current.Iridescence.Ramp); // a restore is not a fresh start: nothing is seeded
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void Off_then_on_over_a_strength_from_below_hands_it_back_to_that_layer()
    {
        var draft = new LookDraft(LookScope.Animal, null);
        var toggle = new IridescenceSwitch();

        toggle.Off(draft);
        Assert.Equal(0f, draft.Current.Iridescence!.Strength!.Normal);

        toggle.On(draft, null, gameHasRamp: true);
        Assert.Null(draft.Current.Iridescence);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void A_fresh_on_with_no_ramp_and_no_shaping_anywhere_starts_a_visible_thin_film()
    {
        var draft = new LookDraft(LookScope.Species, null);
        new IridescenceSwitch().On(draft, null, gameHasRamp: false);

        var i = draft.Current.Iridescence!;
        Assert.Equal(1f, i.Strength!.Normal);
        Assert.Equal(LookEditing.DefaultThickness, i.Ramp!.ThinFilm!.Thickness);
        Assert.Equal(-0.25f, i.Scale);
        Assert.Equal(-0.3f, i.Power);
        Assert.Equal(1.3f, i.Bias);
    }

    [Fact]
    public void A_fresh_on_seeds_nothing_when_the_game_or_a_layer_already_has_a_ramp_or_shaping()
    {
        var gameRamp = new LookDraft(LookScope.Skin, null);
        new IridescenceSwitch().On(gameRamp, null, gameHasRamp: true);
        Assert.Null(gameRamp.Current.Iridescence!.Ramp);
        Assert.Null(gameRamp.Current.Iridescence.Scale);

        var shaped = new LookDraft(LookScope.Skin, null);
        var below = new MaterialOverride { Iridescence = new IridescenceSettings { Bias = 0.5f } };
        new IridescenceSwitch().On(shaped, below, gameHasRamp: false);
        Assert.Null(shaped.Current.Iridescence!.Ramp);
        Assert.Null(shaped.Current.Iridescence.Bias);

        var ramped = new LookDraft(LookScope.Skin, null);
        var file = new MaterialOverride { Iridescence = new IridescenceSettings { Ramp = new RampSource { File = "r.png" } } };
        new IridescenceSwitch().On(ramped, file, gameHasRamp: false);
        Assert.Null(ramped.Current.Iridescence!.Ramp);
        Assert.Equal(1f, ramped.Current.Iridescence.Strength!.Normal);
    }

    [Fact]
    public void A_forgotten_switch_starts_fresh()
    {
        var draft = new LookDraft(LookScope.Skin, Strength(0.35f));
        var toggle = new IridescenceSwitch();
        toggle.Off(draft);
        toggle.Forget();
        toggle.On(draft, draft.Current, gameHasRamp: true);
        Assert.Equal(1f, draft.Current.Iridescence!.Strength!.Normal);
    }

    [Fact]
    public void A_strength_edit_over_per_variant_values_from_below_keeps_the_other_variants()
    {
        var draft = new LookDraft(LookScope.Skin, null);
        var below = new VariantNumbers { Normal = 0.35f, Albino = 0.2f };

        LookEditing.SetStrength(draft, below, "albino", 0.6f);

        var s = draft.Current.Iridescence!.Strength!;
        Assert.Equal(0.35f, s.Normal);
        Assert.Equal(0.6f, s.Albino);
        Assert.Equal(0.2f, below.Albino); // the lower layer's numbers are not changed

        var single = new LookDraft(LookScope.Skin, null);
        LookEditing.SetStrength(single, null, "melanistic", 0.4f);
        Assert.Equal(0.4f, single.Current.Iridescence!.Strength!.Normal);
        Assert.Null(single.Current.Iridescence.Strength.Melanistic);

        Assert.Equal(0.2f, LookEditing.StrengthFor(below, "albino"));
        Assert.Equal(0.35f, LookEditing.StrengthFor(below, "leucistic"));
    }

    [Fact]
    public void Files_under_a_base_folder_are_stored_relative_with_forward_slashes()
    {
        var root = TempDir();
        var inside = Path.Combine(root, "Masks", "x.png");
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "y.png");
        var block = new MaterialOverride
        {
            Iridescence = new IridescenceSettings { Mask = new MaskSource { File = inside }, Ramp = new RampSource { File = outside } },
            Fur = new FurMasks { Mask = new MaskSource { File = inside } },
        };

        var stored = LookFiles.Relative(block, root);

        Assert.Equal("Masks/x.png", stored.Iridescence!.Mask!.File);
        Assert.Equal(Path.GetFullPath(outside), Path.GetFullPath(stored.Iridescence.Ramp!.File!));
        Assert.Equal("Masks/x.png", stored.Fur!.Mask!.File);
        Assert.Equal(inside, block.Iridescence!.Mask!.File); // the input is not changed
    }

    [Fact]
    public void Adopting_copies_files_from_outside_the_mod_into_the_owner_folder()
    {
        var mod = TempDir();
        var masks = TempDir();
        var outside = Path.Combine(masks, "spots.png");
        File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });
        var own = Path.Combine(mod, "materials", "skins", "s", "iridescence-mask.png");
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllBytes(own, new byte[] { 9 });
        var clash = Path.Combine(mod, "materials", "skins", "s", "spots.png");
        File.WriteAllBytes(clash, new byte[] { 7, 7 }); // same name, other content: kept, the copy gets another name
        var block = new MaterialOverride
        {
            Iridescence = new IridescenceSettings { Mask = new MaskSource { File = outside }, Ramp = new RampSource { File = own } },
        };

        var adopted = LookFiles.Adopt(block, mod, "materials/skins/s");

        Assert.Equal("materials/skins/s/iridescence-mask.png", adopted.Iridescence!.Ramp!.File);
        Assert.Equal("materials/skins/s/spots-2.png", adopted.Iridescence.Mask!.File);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(mod, "materials", "skins", "s", "spots-2.png")));
        Assert.Equal(new byte[] { 7, 7 }, File.ReadAllBytes(clash));

        var again = LookFiles.Adopt(block, mod, "materials/skins/s"); // the same content already there is reused
        Assert.Equal("materials/skins/s/spots-2.png", again.Iridescence!.Mask!.File);
    }

    [Fact]
    public void Adopting_reports_an_outside_file_that_is_missing()
    {
        var mod = TempDir();
        var gone = Path.Combine(TempDir(), "gone.png");
        var block = new MaterialOverride { Iridescence = new IridescenceSettings { Mask = new MaskSource { File = gone }, Scale = 0.5f } };
        var missing = new List<string>();

        var adopted = LookFiles.Adopt(block, mod, "materials/skins/s", missing);

        Assert.Null(adopted.Iridescence!.Mask);
        Assert.Equal(0.5f, adopted.Iridescence.Scale);
        Assert.Equal(new[] { gone }, missing);
    }

    [Fact]
    public void A_staged_export_reaches_its_fixed_name_only_on_commit()
    {
        var target = TempDir();
        var final = Path.Combine(target, "skin", LookEditing.IridescenceMaskFile);
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        File.WriteAllBytes(final, new byte[] { 1 }); // the saved look's mask
        var staged = new StagedFiles(Path.Combine(TempDir(), ".pending"));

        var first = staged.Stage(final);
        File.WriteAllBytes(first, new byte[] { 2 });
        var second = staged.Stage(final); // exported again: the first staged file goes
        File.WriteAllBytes(second, new byte[] { 3 });
        Assert.False(File.Exists(first));
        Assert.True(staged.IsStaged(second));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(final));

        var block = new MaterialOverride { Iridescence = new IridescenceSettings { Mask = new MaskSource { File = second } } };
        var saved = staged.Final(block);
        Assert.Equal(final, saved.Iridescence!.Mask!.File);
        Assert.Equal(second, block.Iridescence!.Mask!.File); // the input is not changed
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(final)); // naming the final file moves nothing

        staged.Commit(block);
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(final));
        Assert.False(File.Exists(second));
        Assert.False(staged.IsStaged(second));
    }

    [Fact]
    public void Discarding_deletes_every_staged_file_and_commit_drops_the_unused_ones()
    {
        var target = TempDir();
        var staged = new StagedFiles(Path.Combine(TempDir(), ".pending"));
        var fur = staged.Stage(Path.Combine(target, LookEditing.FurMaskFile));
        File.WriteAllBytes(fur, new byte[] { 1 });
        staged.Discard();
        Assert.False(File.Exists(fur));
        Assert.False(File.Exists(Path.Combine(target, LookEditing.FurMaskFile)));

        var unused = staged.Stage(Path.Combine(target, LookEditing.IridescenceMaskFile));
        File.WriteAllBytes(unused, new byte[] { 1 });
        staged.Commit(new MaterialOverride()); // the draft no longer uses it (picked another mask)
        Assert.False(File.Exists(unused));
        Assert.False(File.Exists(Path.Combine(target, LookEditing.IridescenceMaskFile)));
    }

    [Fact]
    public void Pngs_are_listed_from_every_folder_once_and_sorted()
    {
        var a = TempDir();
        var b = TempDir();
        File.WriteAllBytes(Path.Combine(a, "b.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(a, "notes.txt"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(b, "a.PNG"), new byte[] { 1 });

        var pngs = LookFiles.Pngs(new[] { a, b, a, Path.Combine(a, "missing") });

        Assert.Equal(new[] { Path.Combine(b, "a.PNG"), Path.Combine(a, "b.png") }, pngs);
    }

    private static void AssertSaveable(LookDraft draft, bool skinLevel)
    {
        var map = new Dictionary<string, MaterialOverride> { ["Body"] = draft.Current };
        Assert.Null(MaterialOverrides.Problem(map, "Look", skinLevel));
        Assert.NotNull(MaterialOverrides.Parse(Json.Parse(Json.Write(MaterialOverrides.ToJson(map))), "Look", skinLevel));
    }

    [Fact]
    public void Moving_one_droop_component_keeps_the_game_s_others_and_the_draft_still_parses()
    {
        // The Velociraptor adult's own droop (0, -10.3, 8.11) is outside the old -1..1: moving X must not make the look unsaveable.
        var draft = new LookDraft(LookScope.Skin, null);

        LookEditing.SetComponent(draft, "_DroopDirection", new[] { 0f, -10.3f, 8.11f }, 0, 0.5f);

        Assert.Equal(new[] { 0.5f, -10.3f, 8.11f }, draft.Current.Settings["_DroopDirection"].Vector.Take(3));
        AssertSaveable(draft, skinLevel: true);
    }

    [Fact]
    public void A_game_value_beyond_any_range_is_brought_into_it_when_a_component_moves()
    {
        var draft = new LookDraft(LookScope.Animal, null);

        LookEditing.SetComponent(draft, "_DroopDirection", new[] { 0f, -35f, 40f }, 0, 0.25f);

        Assert.Equal(new[] { 0.25f, -20f, 20f }, draft.Current.Settings["_DroopDirection"].Vector.Take(3));
        AssertSaveable(draft, skinLevel: true);
    }

    [Fact]
    public void A_block_a_load_would_refuse_is_named_before_it_is_written()
    {
        var bad = new MaterialOverride();
        bad.Settings["_DroopDirection"] = MaterialValue.OfVector(0, 0, -21);
        var mask = new MaterialOverride { Iridescence = new IridescenceSettings { Mask = new MaskSource { From = "extra" } } };

        var problem = MaterialOverrides.Problem(new Dictionary<string, MaterialOverride> { ["Body"] = bad }, "This look", true);

        Assert.Equal("This look: \"materials.Body.settings._DroopDirection\" must be from -20 to 20 (got -21).", problem);
        Assert.Null(MaterialOverrides.Problem(new Dictionary<string, MaterialOverride> { ["Body"] = mask }, "Skin", true));
        Assert.Contains("works only on a skin", MaterialOverrides.Problem(new Dictionary<string, MaterialOverride> { ["Body"] = mask }, "Species", false));
    }
}
