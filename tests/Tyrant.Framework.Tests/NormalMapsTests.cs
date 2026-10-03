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
