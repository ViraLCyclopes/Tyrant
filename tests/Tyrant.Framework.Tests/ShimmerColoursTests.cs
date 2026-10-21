using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class ShimmerColoursTests
{
    private static Rgb Hex(string text) => Rgb.TryParse(text, out var c) ? c : throw new ArgumentException(text);

    private static List<Rgb> Hexes(params string[] texts) => texts.Select(Hex).ToList();

    private static string[] Text(IEnumerable<Rgb> stops) => stops.Select(c => c.ToString()).ToArray();

    // ---- HSV ----

    [Theory]
    [InlineData("#ff0000", 0f, 1f, 1f)]
    [InlineData("#00ff00", 1 / 3f, 1f, 1f)]
    [InlineData("#0000ff", 2 / 3f, 1f, 1f)]
    [InlineData("#ffffff", 0f, 0f, 1f)]
    [InlineData("#000000", 0f, 0f, 0f)]
    public void Known_colours_have_their_hue_saturation_and_value(string hex, float h, float s, float v)
    {
        var hsv = ShimmerColours.ToHsv(Hex(hex));
        Assert.Equal(h, hsv.H, 4);
        Assert.Equal(s, hsv.S, 4);
        Assert.Equal(v, hsv.V, 4);
        Assert.Equal(hex, ShimmerColours.ToRgb(new Hsv(h, s, v)).ToString());
    }

    [Fact]
    public void Every_byte_colour_survives_the_round_trip()
    {
        for (var r = 0; r < 256; r += 17)
        for (var g = 0; g < 256; g += 15)
        for (var b = 0; b < 256; b += 51)
        {
            var colour = new Rgb(r / 255f, g / 255f, b / 255f);
            Assert.Equal(colour.ToString(), ShimmerColours.ToRgb(ShimmerColours.ToHsv(colour)).ToString());
        }
        foreach (var hex in new[] { "#39149c", "#2e6be1", "#808080", "#010203" })
            Assert.Equal(hex, ShimmerColours.ToRgb(ShimmerColours.ToHsv(Hex(hex))).ToString());
    }

    [Fact]
    public void Hue_wraps_at_0_and_1()
    {
        Assert.Equal("#ff0000", ShimmerColours.ToRgb(new Hsv(1f, 1f, 1f)).ToString());
        Assert.Equal(ShimmerColours.ToRgb(new Hsv(0.25f, 1f, 1f)).ToString(), ShimmerColours.ToRgb(new Hsv(1.25f, 1f, 1f)).ToString());
        Assert.Equal(ShimmerColours.ToRgb(new Hsv(0.9f, 1f, 1f)).ToString(), ShimmerColours.ToRgb(new Hsv(-0.1f, 1f, 1f)).ToString());
        var magentaRed = ShimmerColours.ToHsv(Hex("#ff0004")); // just under a full turn
        Assert.InRange(magentaRed.H, 0.99f, 1f);
        Assert.True(magentaRed.H < 1f);
    }

    [Fact]
    public void A_grey_keeps_the_hue_it_had_and_black_keeps_its_saturation_too()
    {
        var before = new Hsv(0.6f, 0.4f, 0.5f);
        var grey = ShimmerColours.ToHsv(ShimmerColours.ToRgb(new Hsv(0.6f, 0f, 0.7f)), before);
        Assert.Equal(0.6f, grey.H, 4);
        Assert.Equal(0f, grey.S, 4);
        Assert.Equal(0.7f, grey.V, 2);

        var black = ShimmerColours.ToHsv(new Rgb(0, 0, 0), before);
        Assert.Equal(0.6f, black.H, 4);
        Assert.Equal(0.4f, black.S, 4); // a square dragged to the bottom keeps where it was across
        Assert.Equal(0f, black.V, 4);

        var red = ShimmerColours.ToHsv(Hex("#ff0000"), before); // a colour with a hue has its own
        Assert.Equal(0f, red.H, 4);
        Assert.Equal(1f, red.S, 4);

        Assert.Equal(0f, ShimmerColours.ToHsv(Hex("#808080")).H); // nothing to keep: hue 0
    }

    [Fact]
    public void Out_of_range_saturation_and_value_are_kept_in_0_to_1()
    {
        Assert.Equal("#ffffff", ShimmerColours.ToRgb(new Hsv(0.3f, -1f, 2f)).ToString());
        Assert.Equal("#000000", ShimmerColours.ToRgb(new Hsv(0.3f, 2f, -1f)).ToString());
    }

    // ---- hex ----

    [Theory]
    [InlineData("#39149c", "#39149c")]
    [InlineData("39149C", "#39149c")]
    [InlineData("  #2E6BE1 ", "#2e6be1")]
    public void A_typed_colour_parses_with_or_without_the_hash(string typed, string expected)
    {
        Assert.True(ShimmerColours.TryParseHex(typed, out var colour));
        Assert.Equal(expected, ShimmerColours.Hex(colour));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#12345g")]
    [InlineData("##12345")]
    [InlineData("#-12345")]
    [InlineData("blue")]
    public void Bad_text_is_not_a_colour(string? typed) => Assert.False(ShimmerColours.TryParseHex(typed, out _));

    [Fact]
    public void Quantize_rounds_to_what_the_file_keeps()
    {
        var c = ShimmerColours.Quantize(new Rgb(0.5f, 0.1234f, 1.2f));
        Assert.Equal("#801fff", c.ToString());
        Assert.Equal(128 / 255f, c.R);
        Assert.Equal(1f, c.B);
    }

    // ---- stop lists ----

    [Fact]
    public void Add_puts_a_copy_after_the_selected_colour()
    {
        var stops = Hexes("#000000", "#ffffff");
        var added = ShimmerColours.AddAfter(stops, 0);
        Assert.Equal(new[] { "#000000", "#000000", "#ffffff" }, Text(added));
        Assert.Equal(new[] { "#000000", "#ffffff" }, Text(stops)); // a new list; the old is untouched
        Assert.Equal(new[] { "#000000", "#ffffff", "#ffffff" }, Text(ShimmerColours.AddAfter(stops, 1)));
    }

    [Fact]
    public void Add_stops_at_8_and_remove_at_1()
    {
        var seven = Hexes("#000001", "#000002", "#000003", "#000004", "#000005", "#000006", "#000007");
        Assert.True(ShimmerColours.CanAdd(seven));
        var eight = ShimmerColours.AddAfter(seven, 6);
        Assert.Equal(8, eight.Count);
        Assert.False(ShimmerColours.CanAdd(eight));
        Assert.Equal(Text(eight), Text(ShimmerColours.AddAfter(eight, 2))); // unchanged at the limit

        var one = Hexes("#123456");
        Assert.False(ShimmerColours.CanRemove(one));
        Assert.Equal(new[] { "#123456" }, Text(ShimmerColours.Remove(one, 0)));
        Assert.True(ShimmerColours.CanRemove(Hexes("#000000", "#ffffff")));
    }

    [Fact]
    public void Remove_takes_the_selected_colour_out()
    {
        var stops = Hexes("#000001", "#000002", "#000003");
        Assert.Equal(new[] { "#000001", "#000003" }, Text(ShimmerColours.Remove(stops, 1)));
        Assert.Equal(3, stops.Count);
    }

    [Fact]
    public void Set_replaces_one_colour()
    {
        var stops = Hexes("#000001", "#000002", "#000003");
        Assert.Equal(new[] { "#000001", "#ff0000", "#000003" }, Text(ShimmerColours.SetAt(stops, 1, Hex("#ff0000"))));
        Assert.Equal("#000002", stops[1].ToString());
        Assert.Equal(Text(stops), Text(ShimmerColours.SetAt(stops, 5, Hex("#ff0000")))); // out of range: unchanged
    }

    [Fact]
    public void A_look_with_more_than_8_keeps_the_rest_through_every_edit()
    {
        var ten = Enumerable.Range(1, 10).Select(i => new Rgb(0, 0, i / 255f)).ToList();
        Assert.False(ShimmerColours.CanAdd(ten));
        Assert.Equal(10, ShimmerColours.AddAfter(ten, 3).Count);

        var set = ShimmerColours.SetAt(ten, 7, Hex("#ff0000"));
        Assert.Equal(10, set.Count);
        Assert.Equal("#ff0000", set[7].ToString());
        Assert.Equal(Text(ten.Skip(8)), Text(set.Skip(8)));

        var removed = ShimmerColours.Remove(ten, 2);
        Assert.Equal(9, removed.Count);
        Assert.Equal(Text(ten.Skip(3)), Text(removed.Skip(2)));
    }

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(2, 3, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(-1, 3, 0)]
    [InlineData(9, 12, 7)] // only the first 8 are shown
    [InlineData(4, 0, 0)]
    public void The_selection_stays_on_a_shown_colour(int selected, int count, int expected) =>
        Assert.Equal(expected, ShimmerColours.Selection(selected, count));

    // ---- resampling and starting colours ----

    [Fact]
    public void Resampling_keeps_both_ends_and_spaces_the_rest_evenly()
    {
        var ramp = Enumerable.Range(0, 32).Select(i => new Rgb(i / 31f, 0, 0)).ToList();
        var eight = ShimmerColours.Resample(ramp, 8);
        Assert.Equal(8, eight.Count);
        Assert.Equal(ramp[0].ToString(), eight[0].ToString());
        Assert.Equal(ramp[31].ToString(), eight[7].ToString());
        for (var i = 0; i < 8; i++) Assert.Equal(i / 7f, eight[i].R, 3);

        var short_ = Hexes("#000000", "#ffffff");
        Assert.Equal(Text(short_), Text(ShimmerColours.Resample(short_, 8))); // already short enough: the same colours
        Assert.Equal(new[] { "#000000" }, Text(ShimmerColours.Resample(ramp, 1)));
    }

    [Fact]
    public void Picking_Colours_starts_from_the_stops_in_effect_a_thin_film_or_Microraptors_blues()
    {
        var own = Hexes("#112233", "#445566", "#778899");
        var kept = ShimmerColours.Starting(new RampSource { Stops = own });
        Assert.Equal(Text(own), Text(kept));
        Assert.NotSame(own, kept);

        var film = ShimmerColours.Starting(new RampSource { ThinFilm = new ThinFilmSettings { Thickness = 420 } });
        Assert.Equal(8, film.Count);
        var ramp = ThinFilm.Ramp(420);
        Assert.Equal(ramp[0].ToString(), film[0].ToString());
        Assert.Equal(ramp[31].ToString(), film[7].ToString());

        var blues = new[] { "#39149c", "#2e6be1" };
        Assert.Equal(blues, Text(ShimmerColours.Starting(null)));
        Assert.Equal(blues, Text(ShimmerColours.Starting(new RampSource { Game = true })));
        Assert.Equal(blues, Text(ShimmerColours.Starting(new RampSource { File = "r.png" })));
        Assert.Equal(blues, Text(ShimmerColours.Starting(new RampSource { Species = "Microraptor" })));
    }

    [Fact]
    public void Edited_stops_save_as_the_same_stops_json()
    {
        var stops = ShimmerColours.SetAt(ShimmerColours.Starting(null), 1, ShimmerColours.Quantize(ShimmerColours.ToRgb(new Hsv(0.4f, 0.8f, 0.9f))));
        var look = new MaterialOverride { Iridescence = new IridescenceSettings { Ramp = new RampSource { Stops = stops } } };
        var json = Json.Write(MaterialOverrides.ToJson(new Dictionary<string, MaterialOverride> { ["m"] = look }));
        Assert.Contains("\"stops\"", json);
        var back = MaterialOverrides.Parse(Json.Parse(json), "Test", true)!["m"].Iridescence!.Ramp!.Stops!;
        Assert.Equal(Text(stops), Text(back));
    }
}
