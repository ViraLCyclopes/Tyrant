using Tyrant.Core.Assets;
using Tyrant.Rpc.Assets;

namespace Tyrant.Rpc.Tests;

public class PreviewPartsTests
{
    private static ModelPart Part(string name) => new($@"D:\p\{name}.glb", name, 10, 5, true);

    [Fact]
    public void Keeps_the_most_detailed_lod_and_parts_without_one()
    {
        var shown = PreviewParts.Pick([Part("Acro_LOD02"), Part("Acro_LOD01"), Part("Acro_LOD00"), Part("Eyes")]);

        Assert.Equal(["Acro_LOD00", "Eyes"], shown.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Without_lods_every_part_is_shown()
    {
        Assert.Equal(2, PreviewParts.Pick([Part("Body"), Part("Eyes")]).Count);
    }
}
