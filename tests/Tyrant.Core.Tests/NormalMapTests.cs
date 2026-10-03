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
    public void A_saturated_red_channel_marks_unitys_packed_format()
    {
        byte[] packed = [255, 120, 120, 200, 254, 90, 90, 40, 255, 200, 200, 128];
        byte[] ordinary = [128, 128, 255, 255, 120, 140, 250, 255, 135, 120, 255, 255];

        Assert.True(NormalMap.LooksPacked(packed));
        Assert.False(NormalMap.LooksPacked(ordinary));
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
