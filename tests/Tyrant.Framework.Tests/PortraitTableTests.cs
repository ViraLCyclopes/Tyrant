using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PortraitTableTests
{
    [Fact]
    public void A_skin_uses_its_own_then_its_base_skins_portraits()
    {
        var table = new PortraitTable();
        var dir = Path.Combine(Path.GetTempPath(), "mod-a");
        object species = new(), added = new(), other = new();
        table.Register(species, dir, new Dictionary<string, string> { ["infant"] = "portraits/x/infant.png", ["adultMale"] = "portraits/x/adultMale.png" });
        table.Register(added, dir, new Dictionary<string, string> { ["adultMale"] = "skins/s/portraits/adultMale.png" }, inheritFrom: species);

        Assert.Equal(Path.GetFullPath(Path.Combine(dir, "skins/s/portraits/adultMale.png")), table.PathFor(added, "adultMale"));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir, "portraits/x/infant.png")), table.PathFor(added, "infant"));
        Assert.Null(table.PathFor(added, "adultFemale"));
        Assert.Null(table.PathFor(other, "adultMale"));
    }

    [Fact]
    public void Paths_outside_the_mod_and_unknown_slots_are_ignored()
    {
        var table = new PortraitTable();
        object skin = new();
        table.Register(skin, Path.Combine(Path.GetTempPath(), "mod-a"), new Dictionary<string, string> { ["adultMale"] = "../evil.png", ["adult"] = "a.png" });

        Assert.Null(table.PathFor(skin, "adultMale"));
        Assert.False(table.Any);
    }
}
