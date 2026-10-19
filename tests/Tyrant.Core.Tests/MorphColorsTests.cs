using System.Text.Json;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class MorphColorsTests
{
    private static IReadOnlyList<SpeciesSkins> Species()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SkinDumps.Write(dir);
        return SpeciesSkinsReader.Read(Tyrant.Core.Data.DataStore.OpenDirectory(dir));
    }

    private static Rgb C(string hex) => Rgb.TryParse(hex, out var c) ? c : throw new ArgumentException(hex);

    private static List<Rgb>? Ramp(string json) => MorphColors.Ramp(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void A_vanilla_skins_own_iridescence_is_read_per_sex_from_its_default_variation()
    {
        var alt1 = SkinMaps.Vanilla(Species(), "Carcharodontosaurus", "Alt 1")!;
        var plain = SkinMaps.Vanilla(Species(), "Carcharodontosaurus", "Base")!;

        Assert.Equal(0.25f, alt1.MaleIridescence);
        Assert.Equal(0f, alt1.FemaleIridescence); // iridescence off: none, whatever its strength says
        Assert.Equal(0.25f, alt1.Iridescence("male"));
        Assert.Equal(0.25f, alt1.Iridescence("infant")); // infants wear the male asset
        Assert.Equal(0f, alt1.Iridescence("female"));
        Assert.Equal(0.25f, alt1.Iridescence(null)); // either sex: the stronger
        Assert.Null(plain.MaleIridescence); // the dump does not say
        Assert.Null(plain.Iridescence(null));
    }

    [Fact]
    public void A_vanilla_skins_mutation_colours_are_read_from_its_variation_asset()
    {
        var alt1 = SkinMaps.Vanilla(Species(), "Carcharodontosaurus", "Alt 1")!;

        var albino = alt1.MaleMorphs["albino"];
        Assert.Equal([C("#ff0000"), C("#0000ff")], albino.A);
        Assert.Equal([C("#00ff00"), C("#00ff00")], albino.B);
        Assert.Equal([C("#ff0000")], albino.Eye);
        Assert.Equal([C("#808080"), C("#808080")], albino.Secondary);
        Assert.Equal((0.3f, 0.4f), (albino.Strength!.Value.Min, albino.Strength.Value.Max));
        Assert.Equal((0f, 0.4f), (albino.Softness!.Value.Min, albino.Softness.Value.Max));
        Assert.Equal((-0.1f, 0.1f), (albino.Tint!.Hue!.Value.Min, albino.Tint.Hue.Value.Max));
        Assert.Equal((-0.3f, 0f), (albino.Tint.Saturation!.Value.Min, albino.Tint.Saturation.Value.Max));
        Assert.Equal((-0.2f, 0.2f), (albino.Tint.Value!.Value.Min, albino.Tint.Value.Value.Max));
        Assert.False(alt1.MaleMorphs.ContainsKey("leucistic"));
        Assert.Equal([C("#ffffff")], alt1.FemaleMorphs["leucistic"].A); // each sex has its own asset
        Assert.Empty(SkinMaps.Vanilla(Species(), "Carcharodontosaurus", "Base")!.MaleMorphs);
    }

    [Fact]
    public void Morph_picks_the_sexs_asset_and_the_male_one_for_infants()
    {
        var alt1 = SkinMaps.Vanilla(Species(), "Carcharodontosaurus", "Alt 1");

        Assert.NotNull(MorphColors.Of(alt1, "male", "albino"));
        Assert.NotNull(MorphColors.Of(alt1, "infant", "albino"));
        Assert.Null(MorphColors.Of(alt1, "female", "albino"));
        Assert.NotNull(MorphColors.Of(alt1, "female", "leucistic"));
        Assert.Null(MorphColors.Of(alt1, "male", "normal"));
        Assert.Null(MorphColors.Of(null, "male", "albino"));
    }

    [Fact]
    public void Evenly_spaced_gradient_keys_are_kept_as_they_are()
    {
        Assert.Equal([C("#ff0000"), C("#00ff00"), C("#0000ff")],
            Ramp("""{"mode":"Blend","colorKeys":[{"time":0,"r":1,"g":0,"b":0},{"time":0.5,"r":0,"g":1,"b":0},{"time":1,"r":0,"g":0,"b":1}]}"""));
    }

    [Fact]
    public void Unevenly_spaced_keys_are_sampled_into_evenly_spaced_stops()
    {
        var stops = Ramp("""{"mode":"Blend","colorKeys":[{"time":0,"r":0,"g":0,"b":0},{"time":0.2,"r":1,"g":1,"b":1},{"time":1,"r":1,"g":1,"b":1}]}""")!;

        Assert.Equal(SkinColors.MaxStops, stops.Count);
        Assert.Equal(C("#000000"), stops[0]);
        Assert.Equal(C("#ffffff"), stops[^1]);
        Assert.Equal(C("#b6b6b6"), stops[1]); // time 1/7 is 5/7 of the way to the white key at 0.2
        Assert.Equal(C("#ffffff"), stops[2]);
    }

    [Fact]
    public void A_fixed_gradient_takes_the_key_at_or_after_each_time()
    {
        var stops = Ramp("""{"mode":"Fixed","colorKeys":[{"time":0.5,"r":1,"g":0,"b":0},{"time":1,"r":0,"g":0,"b":1}]}""")!;

        Assert.Equal(SkinColors.MaxStops, stops.Count);
        Assert.Equal(C("#ff0000"), stops[0]);
        Assert.Equal(C("#ff0000"), stops[3]); // 3/7 is before 0.5
        Assert.Equal(C("#0000ff"), stops[4]);
    }

    [Fact]
    public void Keys_out_of_order_are_sorted_and_a_gradient_without_keys_has_no_colour()
    {
        Assert.Equal([C("#ff0000"), C("#0000ff")],
            Ramp("""{"mode":"Blend","colorKeys":[{"time":1,"r":0,"g":0,"b":1},{"time":0,"r":1,"g":0,"b":0}]}"""));
        Assert.Null(Ramp("""{"mode":"Blend","colorKeys":[]}"""));
        Assert.Null(Ramp("""{"mode":"Blend"}"""));
    }

    [Fact]
    public void Fill_keeps_the_mods_fields_and_takes_the_rest_from_the_base_skin()
    {
        var own = new SkinColorSet { A = [C("#123456")], Strength = new FloatRange(0.9f, 0.9f), Tint = new SkinTint { Hue = new FloatRange(0.5f, 0.5f) } };
        var based = new SkinColorSet
        {
            A = [C("#ffffff")], B = [C("#000000")], Eye = [C("#ff0000")], Secondary = [C("#00ff00")],
            Strength = new FloatRange(0.3f, 0.4f), Softness = new FloatRange(0.1f, 0.2f),
            Tint = new SkinTint { Hue = new FloatRange(-0.1f, 0.1f), Value = new FloatRange(-0.2f, 0.2f) },
        };

        var filled = MorphColors.Fill(own, based)!;

        Assert.Equal([C("#123456")], filled.A);
        Assert.Equal([C("#000000")], filled.B);
        Assert.Equal([C("#ff0000")], filled.Eye);
        Assert.Equal([C("#00ff00")], filled.Secondary);
        Assert.Equal(0.9f, filled.Strength!.Value.Min);
        Assert.Equal(0.1f, filled.Softness!.Value.Min);
        Assert.Equal(0.5f, filled.Tint!.Hue!.Value.Min);
        Assert.Equal(-0.2f, filled.Tint.Value!.Value.Min);
        Assert.Null(filled.Tint.Saturation);
        Assert.Same(own, MorphColors.Fill(own, null));
        Assert.Same(based, MorphColors.Fill(null, based));
        Assert.Null(MorphColors.Fill(null, null));
    }

    [Fact]
    public void The_preview_colours_a_mutation_with_the_base_skins_colours_where_the_mod_sets_none()
    {
        var based = new SkinColorSet { A = [C("#ffffff")], Tint = new SkinTint { Hue = new FloatRange(0.2f, 0.2f) } };
        var colors = new SkinColors { Albino = new SkinColorSet { B = [C("#000000")] } };

        var (set, tint) = ColorPreview.For(colors, "albino", based);
        Assert.Equal([C("#ffffff")], set!.A);
        Assert.Equal([C("#000000")], set.B);
        Assert.Equal(0.2f, tint!.Hue!.Value.Min);

        var (normal, _) = ColorPreview.For(null, "normal", based); // normal animals never take mutation colours
        Assert.Null(normal);
    }
}
