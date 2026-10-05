using Tyrant.Core.Blender;
using Tyrant.Core.Data;

namespace Tyrant.Core.Tests;

public class BlenderGrowthTests
{
    private static DataStore Store()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SkinDumps.Write(dir);
        return DataStore.OpenDirectory(dir);
    }

    [Fact]
    public void Reads_curves_relative_flag_and_bones_in_gltf_space()
    {
        var growth = BlenderGrowthReader.Read(Store(), "Carcharodontosaurus")!;

        Assert.Equal(101, growth.Blend.Length);
        Assert.Equal(0.5f, growth.Blend[50], 3); // SkinDumps' blend curve is the identity line
        Assert.True(growth.Relative);
        Assert.Equal(2, growth.Bones.Count);
        var arm = growth.Bones[0];
        Assert.Equal("Arm.L", arm.Name);
        Assert.False(arm.Translation);
        Assert.True(arm.Scale);
        Assert.Equal([-0.1f, 0.3f, 0f, 1.14f, 1.14f, 1.14f], arm.Baby); // Unity x mirrored
        var hip = growth.Bones[1];
        Assert.True(hip.Translation); // "All" = translation and scale
        Assert.True(hip.Scale);
    }

    [Fact]
    public void No_dump_or_unknown_species_is_null()
    {
        Assert.Null(BlenderGrowthReader.Read(null, "Carcharodontosaurus"));
        Assert.Null(BlenderGrowthReader.Read(Store(), "Frog")); // no growth fields
    }
}
