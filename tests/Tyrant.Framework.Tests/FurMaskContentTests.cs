using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class FurMaskContentTests
{
    // DXT1 blocks: two RGB565 end colours, then 2-bit indices. Fur reads only red (the top 5 bits).
    private static byte[] Dxt1(params ushort[] colours) =>
        colours.Chunk(2).SelectMany(c => new byte[] { (byte)c[0], (byte)(c[0] >> 8), (byte)c[1], (byte)(c[1] >> 8), 0x55, 0x55, 0x55, 0x55 }).ToArray();

    [Fact]
    public void A_dxt1_mask_whose_end_colours_have_no_red_has_no_fur()
    {
        // The starting skins' baby mask: 0x07FF is green + blue, red 0.
        Assert.True(FurMaskContent.HasNoFur(UnityTextureFormats.Dxt1, 8, 4, Dxt1(0x07FF, 0x07FF, 0x0000, 0x001F)));
        Assert.False(FurMaskContent.HasNoFur(UnityTextureFormats.Dxt1, 8, 4, Dxt1(0x07FF, 0x07FF, 0x0800, 0x0000)));
    }

    [Fact]
    public void A_dxt5_mask_is_judged_by_its_colour_half()
    {
        // The game's blank FurMaskEmpty, as the starting skins cooked it: alpha 0/255, colours black.
        var blank = new byte[] { 0x00, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.True(FurMaskContent.HasNoFur(UnityTextureFormats.Dxt5, 4, 4, blank));
        var furred = (byte[])blank.Clone();
        furred[9] = 0xF8; // end colour 0: full red
        Assert.False(FurMaskContent.HasNoFur(UnityTextureFormats.Dxt5, 4, 4, furred));
    }

    [Fact]
    public void Only_the_largest_level_is_read()
    {
        // 4 × 4: one block, then a mip level with red that must not count.
        var data = Dxt1(0x0000, 0x0000).Concat(Dxt1(0xF800, 0xF800)).ToArray();
        Assert.True(FurMaskContent.HasNoFur(UnityTextureFormats.Dxt1, 4, 4, data));
    }

    [Fact]
    public void An_rgba32_mask_is_read_pixel_by_pixel()
    {
        var pixels = new byte[4 * 4 * 4];
        for (var i = 1; i < pixels.Length; i += 4) pixels[i] = 200; // green only
        Assert.True(FurMaskContent.HasNoFur(FurMaskContent.Rgba32, 4, 4, pixels));
        pixels[4 * 7] = 1;
        Assert.False(FurMaskContent.HasNoFur(FurMaskContent.Rgba32, 4, 4, pixels));
    }

    [Fact]
    public void Formats_it_cannot_read_and_short_data_count_as_fur()
    {
        Assert.False(FurMaskContent.HasNoFur(UnityTextureFormats.Bc7, 4, 4, new byte[16]));
        Assert.False(FurMaskContent.HasNoFur(UnityTextureFormats.Dxt1, 8, 8, new byte[8]));
    }

    [Theory]
    [InlineData("ultimasaurus/ultimasaurus-start:fur", true)]
    [InlineData("ultimasaurus/ultimasaurus-start:infantFur", true)]
    [InlineData("ultimasaurus/ultimasaurus-start:diffuse", false)]
    [InlineData("T_rex_fur", false)]
    public void Only_skin_fur_slots_are_checked(string textureName, bool fur) =>
        Assert.Equal(fur, FurMaskContent.IsSkinFurSlot(textureName));
}
