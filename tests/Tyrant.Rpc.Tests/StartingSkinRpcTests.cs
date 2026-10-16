using System.Text.Json;
using Tyrant.Core.Tests;
using static Tyrant.Rpc.Tests.ModEditorMethodsTests;

namespace Tyrant.Rpc.Tests;

public class StartingSkinRpcTests
{
    private static Task<JsonElement> Add(RpcHarness h, JsonElement detail, object? extra = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["id"] = "red-spot", ["revision"] = Rev(detail), ["speciesId"] = "anax", ["template"] = "Carcharodontosaurus",
            ["name"] = "Anax", ["genus"] = "Example", ["epithet"] = "anax", ["startingSkin"] = 1,
        };
        foreach (var p in extra?.GetType().GetProperties() ?? []) fields[p.Name] = p.GetValue(extra);
        return h.Call("mods.addSpecies", fields);
    }

    [Fact]
    public async Task A_new_species_gets_its_starting_skin()
    {
        using var game = new FakeGame();
        var (h, _, _) = await WithSkinAndPrefab(game);

        var detail = await Add(h, await h.Call("mods.get", new { id = "red-spot" }));

        var start = Assert.Single(detail.GetProperty("skins").EnumerateArray(), s => s.GetProperty("starting").GetBoolean());
        Assert.Equal(("anax-start", "tyrant.red-spot.anax", "Alt 1"), (start.GetProperty("id").GetString(), start.GetProperty("species").GetString(), start.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, start.GetProperty("copiedFromSpecies").ValueKind);
        var animal = Assert.Single((await h.Call("mods.animalDatabase", new { id = "red-spot" })).GetProperty("animals").EnumerateArray());
        Assert.Equal("anax-start", animal.GetProperty("startingSkinId").GetString());
    }

    [Fact]
    public async Task Copy_and_own_pngs_together_are_refused()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await WithSkinAndPrefab(game);

        var detail = await h.Call("mods.get", new { id = "red-spot" });
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Add(h, detail,
            new { startFrom = "Carcharodontosaurus", startPng = Path.Combine(ws, "mods", "red-spot", "skins", "blue", "male_D.png") }));

        Assert.Contains("Copy a skin or use your own PNGs, not both.", ex.Message);
    }

    [Fact]
    public async Task A_species_without_one_can_get_its_starting_skin_once()
    {
        using var game = new FakeGame();
        var (h, _, _) = await WithSkinAndPrefab(game);
        var detail = await Add(h, await h.Call("mods.get", new { id = "red-spot" }));
        detail = await h.Call("mods.removeSkin", new { id = "red-spot", revision = Rev(detail), skin = "anax-start", deleteFiles = true });

        detail = await h.Call("mods.addStartingSkin", new { id = "red-spot", revision = Rev(detail), speciesId = "anax", startSkin = 1, startName = "Mine" });

        var start = Assert.Single(detail.GetProperty("skins").EnumerateArray(), s => s.GetProperty("starting").GetBoolean());
        Assert.Equal("Mine", start.GetProperty("name").GetString());
        await Assert.ThrowsAnyAsync<Exception>(() => h.Call("mods.addStartingSkin", new { id = "red-spot", revision = Rev(detail), speciesId = "anax", startSkin = 1 }));
    }
}
