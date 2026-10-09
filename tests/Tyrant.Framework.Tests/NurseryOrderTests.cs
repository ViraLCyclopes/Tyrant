using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class NurseryOrderTests
{
    [Fact]
    public void Custom_species_sort_by_their_scientific_name_among_the_games_species()
    {
        var names = new Dictionary<string, string>
        {
            ["tyrant.big-pack.giga"] = "Giganotosaurus carolinii",
            ["tyrant.spino.mirabilis"] = "Spinosaurus mirabilis",
        };
        var ids = new List<string> { "tyrant.spino.mirabilis", "Spinosaurus", "tyrant.big-pack.giga", "Allosaurus Anax", "Gallimimus", "Carcharodontosaurus", "Tyrannosaurus" };

        ids.Sort((a, b) => NurseryOrder.Compare(a, b, names));

        Assert.Equal(["Allosaurus Anax", "Carcharodontosaurus", "Gallimimus", "tyrant.big-pack.giga", "Spinosaurus", "tyrant.spino.mirabilis", "Tyrannosaurus"], ids);
    }

    [Fact]
    public void Without_custom_species_the_games_order_is_unchanged()
    {
        var ids = new List<string> { "Spinosaurus", "Allosaurus Anax", "Carcharodontosaurus" };

        ids.Sort((a, b) => NurseryOrder.Compare(a, b, new Dictionary<string, string>()));

        Assert.Equal(["Allosaurus Anax", "Carcharodontosaurus", "Spinosaurus"], ids);
    }
}
