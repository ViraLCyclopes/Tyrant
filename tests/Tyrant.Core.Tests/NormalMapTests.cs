using Tyrant.Core.Assets;

namespace Tyrant.Core.Tests;

public class NormalMapTests
{
    [Theory]
    [InlineData("T_Acrocanthosaurus_N", true)]
    [InlineData("T_acrocanthosaurus_infant_N", true)]
    [InlineData("Rock_Normal", true)]
    [InlineData("Cliff_nrm", true)]
    [InlineData("T_Stego_D", false)]
    [InlineData("T_N_D", false)]
    [InlineData("Detail_Skin", false)]
    public void Normal_maps_are_recognised_by_name(string name, bool expected)
    {
        Assert.Equal(expected, NormalMap.IsCandidate(name));
    }

    [Fact]
    public void The_packed_format_is_recognised_from_its_pixels()
    {
        // Red filled, green = blue (Y), alpha varying around the middle (X): Unity's packed normal map.
        byte[] packed = [255, 120, 120, 200, 254, 90, 90, 40, 255, 200, 200, 128];
        byte[] standardNormal = [128, 128, 255, 255, 120, 140, 250, 255, 135, 120, 255, 255];
        byte[] redOpaqueSkin = [250, 30, 30, 255, 255, 12, 12, 255, 245, 40, 40, 255];
        byte[] redSkinWithMask = [252, 30, 90, 200, 255, 12, 70, 40, 248, 40, 110, 128];

        Assert.True(NormalMap.LooksPacked(packed));
        Assert.False(NormalMap.LooksPacked(standardNormal));
        Assert.False(NormalMap.LooksPacked(redOpaqueSkin)); // solid alpha holds no X
        Assert.False(NormalMap.LooksPacked(redSkinWithMask)); // green and blue differ
    }

    [Fact]
    public void Unpack_rebuilds_x_y_z_from_alpha_and_green()
    {
        byte[] pixels = [255, 128, 128, 128, 255, 128, 0, 255];

        NormalMap.Unpack(pixels);

        Assert.Equal(new byte[] { 128, 128, 255, 255 }, pixels[..4]);
        Assert.Equal(255, pixels[4]); // X from alpha
        Assert.Equal(128, pixels[5]); // Y stays
        Assert.InRange(pixels[6], (byte)127, (byte)128); // flat to the side: z ~ 0
        Assert.Equal(255, pixels[7]); // opaque
    }
}
