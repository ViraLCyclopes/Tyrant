using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MaskChannelTests
{
    private static readonly byte[] TwoPixels = { 10, 20, 30, 40, 250, 5, 128, 0 };

    [Theory]
    [InlineData('r', false, new byte[] { 10, 10, 10, 255, 250, 250, 250, 255 })]
    [InlineData('G', false, new byte[] { 20, 20, 20, 255, 5, 5, 5, 255 })]
    [InlineData('b', true, new byte[] { 225, 225, 225, 255, 127, 127, 127, 255 })]
    [InlineData('a', true, new byte[] { 215, 215, 215, 255, 255, 255, 255, 255 })]
    [InlineData('x', false, new byte[] { 10, 10, 10, 255, 250, 250, 250, 255 })] // not a channel: red
    public void In_place_extraction_writes_grey_of_the_channel_over_each_pixel(char channel, bool invert, byte[] expected)
    {
        var pixels = (byte[])TwoPixels.Clone();
        MaskChannel.ExtractInPlace(new ByteArrayBuffer(pixels), channel, invert);
        Assert.Equal(expected, pixels);
    }

    [Fact]
    public void Extract_leaves_its_input_alone_and_matches_in_place()
    {
        var input = (byte[])TwoPixels.Clone();
        var copy = MaskChannel.Extract(input, 'b', false);
        Assert.Equal(TwoPixels, input);
        var inPlace = (byte[])TwoPixels.Clone();
        MaskChannel.ExtractInPlace(new ByteArrayBuffer(inPlace), 'b', false);
        Assert.Equal(inPlace, copy);
    }

    [Fact]
    public void Bytes_after_the_last_whole_pixel_are_kept_in_place_and_zero_in_a_copy()
    {
        var pixels = new byte[] { 1, 2, 3, 4, 9, 9 };
        MaskChannel.ExtractInPlace(new ByteArrayBuffer(pixels), 'g', false);
        Assert.Equal(new byte[] { 2, 2, 2, 255, 9, 9 }, pixels);
        Assert.Equal(new byte[] { 2, 2, 2, 255, 0, 0 }, MaskChannel.Extract(new byte[] { 1, 2, 3, 4, 9, 9 }, 'g', false));
    }
}
