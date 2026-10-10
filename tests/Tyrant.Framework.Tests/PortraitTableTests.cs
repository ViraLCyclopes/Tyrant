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

    [Fact]
    public void An_unusable_path_skips_only_its_slot_and_is_reported()
    {
        var table = new PortraitTable();
        var dir = Path.Combine(Path.GetTempPath(), "mod-a");
        object skin = new();
        var skipped = table.Register(skin, dir, new Dictionary<string, string> { ["adultMale"] = "a\0b.png", ["infant"] = "i.png", ["adultFemale"] = "../x.png" });

        Assert.Equal(new[] { "adultFemale", "adultMale" }, skipped.OrderBy(x => x).ToArray());
        Assert.Null(table.PathFor(skin, "adultMale"));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir, "i.png")), table.PathFor(skin, "infant"));
    }

    [Fact]
    public void An_unregistered_base_skin_gives_nothing_to_inherit()
    {
        var table = new PortraitTable();
        var dir = Path.Combine(Path.GetTempPath(), "mod-a");
        object skin = new(), unknown = new();
        var skipped = table.Register(skin, dir, new Dictionary<string, string> { ["infant"] = "i.png" }, inheritFrom: unknown);

        Assert.Empty(skipped);
        Assert.NotNull(table.PathFor(skin, "infant"));
        Assert.Null(table.PathFor(skin, "adultMale"));
    }

    [Fact]
    public void Registering_again_with_nothing_removes_the_skin()
    {
        var table = new PortraitTable();
        var dir = Path.Combine(Path.GetTempPath(), "mod-a");
        object skin = new();
        table.Register(skin, dir, new Dictionary<string, string> { ["infant"] = "i.png" });
        Assert.True(table.Any);
        table.Register(skin, dir, null);

        Assert.False(table.Any);
        Assert.Null(table.PathFor(skin, "infant"));
    }
}
