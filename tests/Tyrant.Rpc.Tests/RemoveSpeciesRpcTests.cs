using System.Text.Json;
using Tyrant.Core.Tests;
using static Tyrant.Rpc.Tests.ModEditorMethodsTests;

namespace Tyrant.Rpc.Tests;

public class RemoveSpeciesRpcTests
{
    [Fact]
    public async Task The_preview_names_the_species_skins_and_removing_takes_them()
    {
        using var game = new FakeGame();
        var (h, _, _) = await WithSkinAndPrefab(game);
        var detail = await h.Call("mods.addSpecies", new
        {
            id = "red-spot", revision = Rev(await h.Call("mods.get", new { id = "red-spot" })), speciesId = "anax", template = "Carcharodontosaurus",
            name = "Anax", genus = "Example", epithet = "anax", startingSkin = 1,
        });

        var plan = await h.Call("mods.speciesRemoval", new { id = "red-spot", speciesId = "anax" });
        Assert.Equal(("tyrant.red-spot.anax", "Anax"), (plan.GetProperty("key").GetString(), plan.GetProperty("name").GetString()));
        Assert.Equal(["Alt 1"], plan.GetProperty("skins").EnumerateArray().Select(s => s.GetString()));
        Assert.Empty(plan.GetProperty("usedBy").EnumerateArray());

        detail = await h.Call("mods.removeSpecies", new { id = "red-spot", revision = Rev(detail), speciesId = "anax", deleteFiles = true });

        Assert.DoesNotContain(detail.GetProperty("skins").EnumerateArray(), s => s.GetProperty("species").GetString() == "tyrant.red-spot.anax");
        Assert.Empty((await h.Call("mods.animalDatabase", new { id = "red-spot" })).GetProperty("animals").EnumerateArray());
    }
}
