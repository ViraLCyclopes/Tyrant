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

    [Theory]
    [InlineData("Femur.L", -1561929513)] // Tyrannosaurus' GrowthData: its bones are stored by this hash, not by name
    [InlineData("Head", -158744160)]
    [InlineData("Hip", 339800441)]
    public void The_bone_hash_is_the_games_stable_hash(string name, int hash)
    {
        Assert.Equal(hash, BlenderGrowthReader.StableHash(name));
    }

    [Fact]
    public void Bones_stored_by_hash_are_found_by_the_skeletons_names()
    {
        // As 45 of the game's animals store them (Tyrannosaurus, Carcharodontosaurus…): no name, the name's hash in the address.
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SkinDumps.Write(dir);
        foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("\"transformName\":\"Hip\"", StringComparison.Ordinal))
                File.WriteAllText(file, text.Replace("\"transformName\":\"Hip\"",
                    "\"transformName\":\"\",\"transformAddress\":{\"finalDestinationHash\":339800441}", StringComparison.Ordinal));
        }
        var store = DataStore.OpenDirectory(dir);

        Assert.DoesNotContain(BlenderGrowthReader.Read(store, "Carcharodontosaurus")!.Bones, b => b.Name == "Hip"); // no skeleton given
        var growth = BlenderGrowthReader.Read(store, "Carcharodontosaurus", ["Animal", "Hip", "Tail"])!;

        var hip = Assert.Single(growth.Bones, b => b.Name == "Hip");
        Assert.True(hip.Translation);
    }
}
