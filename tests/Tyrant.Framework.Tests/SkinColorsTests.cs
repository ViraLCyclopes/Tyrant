using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SkinColorsTests
{
    private static ModManifest WithColors(string colors) => ModManifest.Parse($$"""
        { "format": 1, "id": "mod-id", "skins": [ { "id": "red-spot", "species": "S", "male": { "diffuse": "a.png" }, "colors": {{colors}} } ] }
        """);

    [Fact]
    public void Reads_tint_pattern_and_mutations_with_single_values_as_ranges()
    {
        var c = WithColors("""
            {
              "tint": { "hue": [-0.05, 0.05], "saturation": 0, "value": [0, 0] },
              "pattern": { "a": ["#3060ff", "#2040c0"], "b": "#20c040", "strength": [0.6, 0.8], "softness": 0.2, "secondary": "#ffcc00", "eye": ["#ff2000"] },
              "albino": { "a": "#ffffff", "tint": { "value": [0.1, 0.2] } }
            }
            """).Skins[0].Colors!;

        Assert.Equal((-0.05f, 0.05f), (c.Tint!.Hue!.Value.Min, c.Tint.Hue.Value.Max));
        Assert.Equal((0f, 0f), (c.Tint.Saturation!.Value.Min, c.Tint.Saturation.Value.Max));
        Assert.Equal(new[] { "#3060ff", "#2040c0" }, c.Pattern!.A!.Select(x => x.ToString()));
        Assert.Equal("#20c040", Assert.Single(c.Pattern.B!).ToString());
        Assert.Equal((0.2f, 0.2f), (c.Pattern.Softness!.Value.Min, c.Pattern.Softness.Value.Max));
        Assert.Equal((0.1f, 0.2f), (c.Albino!.Tint!.Value!.Value.Min, c.Albino.Tint.Value.Value.Max));
        Assert.Null(c.Melanistic);
    }

    [Theory]
    [InlineData("""{ "glow": {} }""", "unknown key \"colors.glow\"")]
    [InlineData("""{ "pattern": { "a": "#12345" } }""", "colors.pattern.a")]
    [InlineData("""{ "pattern": { "a": ["#000000","#000000","#000000","#000000","#000000","#000000","#000000","#000000","#000000"] } }""", "at most 8")]
    [InlineData("""{ "pattern": { "a": "#000000", "strength": [0.8, 0.2] } }""", "colors.pattern.strength")]
    [InlineData("""{ "pattern": { "a": "#000000", "strength": 1.5 } }""", "0 to 1")]
    [InlineData("""{ "pattern": { "a": "#000000", "softness": 0 } }""", "greater than 0")]
    [InlineData("""{ "tint": { "hue": 2 } }""", "-1 to 1")]
    [InlineData("""{ "pattern": { "strength": 0.5 } }""", "\"a\" or \"b\"")]
    [InlineData("""{ "pattern": { "a": "#000000", "tint": {} } }""", "colors.tint")]
    [InlineData("""{ "albino": { "shine": 1 } }""", "unknown key \"colors.albino.shine\"")]
    public void Colour_problems_are_explained(string colors, string expected)
    {
        var ex = Assert.Throws<ManifestException>(() => WithColors(colors));

        Assert.Contains(expected, ex.Message);
        Assert.Contains("red-spot", ex.Message);
    }

    [Fact]
    public void Written_colours_read_back_the_same_and_skins_without_colours_write_none()
    {
        var m = WithColors("""{ "tint": { "value": 0 }, "pattern": { "a": "#3060ff", "strength": [0.6, 0.8] }, "leucistic": { "b": ["#ffffff", "#eeeeee"] } }""");

        var json = m.ToJson();

        Assert.Equal(json, ModManifest.Parse(json).ToJson());
        Assert.DoesNotContain("colors", ModManifest.Parse("""{ "format": 1, "id": "mod-id", "skins": [ { "id": "plain", "species": "S", "male": { "diffuse": "a.png" } } ] }""").ToJson());
    }

    [Fact]
    public void A_ramp_is_evenly_spaced_and_a_single_colour_is_fixed()
    {
        Rgb.TryParse("#000000", out var black);
        Rgb.TryParse("#ffffff", out var white);
        Rgb.TryParse("#ff0000", out var red);

        Assert.Equal(0.5f, ColorRamp.Sample([black, white], 0.5).R, 3);
        Assert.Equal(1f, ColorRamp.Sample([black, white, red], 0.75).R, 3); // between white and red: red stays 1
        Assert.Equal(0.5f, ColorRamp.Sample([black, white, red], 0.75).G, 3);
        Assert.Equal(red, ColorRamp.Sample([red], 0.3));
        Assert.Equal(0.7f, new FloatRange(0.6f, 0.8f).Sample(0.5), 3);
    }
}
