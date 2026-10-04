using Tyrant.Core.Models;

namespace Tyrant.Core.Tests;

public class OcclusionRoughnessTests
{
    [Fact]
    public void Extra_green_becomes_occlusion_and_extra_red_becomes_roughness()
    {
        // extra map: R = smoothness, G = ambient occlusion (the decoded animal shader)
        byte[] extra = [255, 128, 7, 255, /* */ 0, 255, 9, 255];

        var orm = OcclusionRoughness.FromExtra(extra);

        // glTF: occlusion in R, roughness in G, metallic in B (0: animals are never metal)
        Assert.Equal(new byte[] { 128, 0, 0, 255, /* */ 255, 255, 0, 255 }, orm);
    }

    [Fact]
    public void A_png_is_written_next_to_the_extra_map()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var extra = Path.Combine(dir, "T_Acro_E.png");
        using (var s = File.Create(extra))
            new StbImageWriteSharp.ImageWriter().WritePng(new byte[] { 255, 64, 0, 255 }, 1, 1, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, s);

        var orm = OcclusionRoughness.Write(extra);

        Assert.Equal(Path.Combine(dir, "T_Acro_E_ORM.png"), orm);
        var image = StbImageSharp.ImageResult.FromMemory(File.ReadAllBytes(orm!), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Assert.Equal(new byte[] { 64, 0, 0, 255 }, image.Data);
    }

    [Fact]
    public void An_unreadable_extra_map_gives_no_png()
    {
        var path = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N") + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a png");

        Assert.Null(OcclusionRoughness.Write(path));
    }
}
