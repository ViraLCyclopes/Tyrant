using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PixelsTests
{
    [Fact]
    public void Opaque_means_every_alpha_is_255()
    {
        Assert.True(Pixels.IsOpaque([1, 2, 3, 255, 4, 5, 6, 255]));
        Assert.False(Pixels.IsOpaque([1, 2, 3, 255, 4, 5, 6, 254]));
    }

    [Fact]
    public void The_centre_square_is_the_largest_one_in_the_middle()
    {
        Pixels.CenterSquare(300, 100, out var x, out var y, out var size);

        Assert.Equal((100, 0, 100), (x, y, size));
    }

    [Fact]
    public void A_thumbnail_averages_the_centre_square()
    {
        // 4x2 image: the centre square is columns 1-2; each column is one colour.
        byte[] Px(byte v) => [v, v, v, 255];
        var rgba = new[] { Px(0), Px(100), Px(200), Px(0), Px(0), Px(100), Px(200), Px(0) }.SelectMany(p => p).ToArray();

        var thumb = Pixels.Thumbnail(rgba, 4, 2, 1);

        Assert.Equal(new byte[] { 150, 150, 150, 255 }, thumb); // (100 + 200 + 100 + 200) / 4
    }

    [Fact]
    public void Thumbnail_reads_other_byte_layouts_in_place_and_gives_rgba()
    {
        byte[] argb = [40, 10, 20, 30, 80, 50, 60, 70, 40, 10, 20, 30, 80, 50, 60, 70]; // 2x2, alpha first
        byte[] rgb = [10, 20, 30, 50, 60, 70, 10, 20, 30, 50, 60, 70]; // 2x2, no alpha

        Assert.Equal(new byte[] { 30, 40, 50, 60 }, Pixels.Thumbnail(new ArrayBytes(argb), 2, 2, 1, 4, 1, 2, 3, 0));
        Assert.Equal(new byte[] { 30, 40, 50, 255 }, Pixels.Thumbnail(new ArrayBytes(rgb), 2, 2, 1, 3, 0, 1, 2, -1));
    }
}
