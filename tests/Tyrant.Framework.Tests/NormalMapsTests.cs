using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class NormalMapsTests
{
    private static byte[] Pixels(params (byte R, byte G, byte B, byte A)[] px) => px.SelectMany(p => new[] { p.R, p.G, p.B, p.A }).ToArray();

    [Fact]
    public void Recognises_standard_packed_and_other_images()
    {
        var standard = Pixels((128, 128, 255, 255), (140, 120, 250, 255), (110, 135, 245, 255));
        var packed = Pixels((255, 120, 120, 200), (254, 90, 90, 40), (255, 200, 200, 128));
        var diffuse = Pixels((200, 40, 30, 255), (90, 60, 20, 255), (10, 200, 30, 255));

        Assert.Equal(NormalMapKind.Standard, NormalMaps.Classify(standard));
        Assert.Equal(NormalMapKind.Packed, NormalMaps.Classify(packed));
        Assert.Equal(NormalMapKind.Unknown, NormalMaps.Classify(diffuse));
    }

    [Fact]
    public void A_packed_map_with_a_flat_left_edge_is_judged_from_all_its_columns()
    {
        // 64 x 4096: about 4096 samples one width apart all fall in column 0, as on a 4096 x 4096 map (Gallimimus' adult normal).
        var map = NormalMapsFixture.PackedWithFlatLeftColumn(64, 4096);

        Assert.Equal(NormalMapKind.Packed, NormalMaps.Classify(map));
    }

    [Theory]
    [InlineData(4096 * 4096)]
    [InlineData(2048 * 2048)]
    [InlineData(64 * 4096)]
    public void The_sample_step_is_odd_so_samples_cross_every_power_of_two_width(int pixels)
    {
        Assert.Equal(1, NormalMaps.SampleStep(pixels) % 2);
    }

    [Fact]
    public void Small_images_are_read_pixel_by_pixel()
    {
        Assert.Equal(1, NormalMaps.SampleStep(3));
        Assert.Equal(1, NormalMaps.SampleStep(8191));
    }

    [Fact]
    public void Packing_moves_x_to_alpha_keeps_y_and_fills_red()
    {
        var pixels = Pixels((200, 90, 240, 255));

        NormalMaps.PackForUnity(pixels);

        Assert.Equal(new byte[] { 255, 90, 90, 200 }, pixels); // R = 1, G = Y, B = Y, A = X — Unity's packed (DXT5nm) layout
    }

    [Fact]
    public void Every_skin_slot_the_game_sets_is_known_with_its_kind()
    {
        Assert.Equal(10, TextureSlots.All.Count);
        Assert.Equal(SlotKind.Color, TextureSlots.All.Single(s => s.Property == "_AdultDiffuse").Kind);
        Assert.Equal(SlotKind.Normal, TextureSlots.All.Single(s => s.Property == "_InfantNormal").Kind);
        Assert.Equal(SlotKind.Data, TextureSlots.All.Single(s => s.Property == "_AdultPatternMask").Kind);
    }
}
