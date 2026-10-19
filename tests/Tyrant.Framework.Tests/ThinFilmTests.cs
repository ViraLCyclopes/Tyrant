using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class ThinFilmTests
{
    [Fact] public void No_film_gives_no_colour() => Assert.All(ThinFilm.Ramp(0), c => Assert.Equal("#000000", c.ToString()));

    [Fact]
    public void A_176nm_film_looks_purple_face_on()
    {
        var c = ThinFilm.Ramp(176)[^1];
        Assert.True(c.G < c.R && c.G < c.B);
    }

    [Fact]
    public void A_264nm_film_looks_green_face_on()
    {
        var c = ThinFilm.Ramp(264)[^1];
        Assert.True(c.G > c.R && c.G > c.B);
    }

    [Fact]
    public void Colours_shift_with_the_viewing_angle()
    {
        var r = ThinFilm.Ramp(420);
        Assert.NotEqual(r[0], r[^1]);
        Assert.Equal(32, r.Count);
    }

    [Fact]
    public void Ramp_pixels_repeat_one_row_and_stops_come_from_stops_or_thin_film()
    {
        var pixels = IridescenceRamp.Pixels([new Rgb(0, 0, 0), new Rgb(1, 1, 1)], 32);
        Assert.Equal(32 * 32 * 4, pixels.Length);
        Assert.Equal(pixels.AsSpan(0, 32 * 4).ToArray(), pixels.AsSpan(31 * 32 * 4, 32 * 4).ToArray());
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixels.AsSpan(0, 4).ToArray());
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels.AsSpan(31 * 4, 4).ToArray());

        var film = IridescenceRamp.Stops(new RampSource { ThinFilm = new ThinFilmSettings { Thickness = 264 } });
        Assert.Equal(ThinFilm.Ramp(264), film);
        var stops = new List<Rgb> { new Rgb(1, 0, 0) };
        Assert.Same(stops, IridescenceRamp.Stops(new RampSource { Stops = stops }));
        Assert.Null(IridescenceRamp.Stops(new RampSource { File = "r.png" }));
    }

    [Fact]
    public void A_mask_takes_one_channel_and_can_invert()
    {
        var rgba = new byte[] { 10, 20, 30, 40 };
        Assert.Equal(new byte[] { 20, 20, 20, 255 }, MaskChannel.Extract(rgba, 'g', false));
        Assert.Equal(new byte[] { 215, 215, 215, 255 }, MaskChannel.Extract(rgba, 'a', true));
    }
}
