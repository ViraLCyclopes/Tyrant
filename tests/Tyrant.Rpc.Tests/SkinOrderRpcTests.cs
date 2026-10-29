using System.Text.Json;
using Tyrant.Core.Mods;
using Tyrant.Core.Tests;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Rpc.Tests.ModEditorMethodsTests;

namespace Tyrant.Rpc.Tests;

public class SkinOrderRpcTests
{
    private const string Key = "tyrant.red-spot.anax";

    private static async Task<(RpcHarness H, string Ws, FakeGame Game)> WithSpecies(FakeGame game, params string[] extraSkins)
    {
        var (h, ws, _) = await WithSkinAndPrefab(game);
        await h.Call("mods.addSpecies", new
        {
            id = "red-spot", revision = Rev(await h.Call("mods.get", new { id = "red-spot" })), speciesId = "anax", template = "Carcharodontosaurus",
            name = "Anax", genus = "Example", epithet = "anax", startingSkin = 1,
        });
        var mod = ModProject.Open(Workspace.Open(ws), "red-spot");
        foreach (var id in extraSkins)
            mod.Manifest.Skins.Add(new SkinEntry { Id = id, Species = Key, Name = id, Base = "0", Male = new() { ["diffuse"] = "skins/blue/male_D.png" } });
        mod.Save();
        return (h, ws, game);
    }

    private static string[] Order(JsonElement detail) =>
        detail.GetProperty("skinOrders").GetProperty(Key).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static string StartingId(JsonElement detail) =>
        detail.GetProperty("skins").EnumerateArray().First(s => s.GetProperty("starting").GetBoolean()).GetProperty("id").GetString()!;

    [Fact]
    public async Task Detail_lists_the_shown_order_for_species_with_a_starting_skin()
    {
        using var game = new FakeGame();
        var (h, _, _) = await WithSpecies(game, "brown");

        var detail = await h.Call("mods.get", new { id = "red-spot" });

        var order = Order(detail);
        Assert.Equal(StartingId(detail), order[0]);
        Assert.Contains("brown", order);
        Assert.False(detail.GetProperty("skinOrders").TryGetProperty("Carcharodontosaurus", out _)); // not one of this mod's species
    }

    [Fact]
    public async Task Detail_has_no_order_entry_for_a_species_without_a_starting_skin()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await WithSkinAndPrefab(game);
        var mod = ModProject.Open(Workspace.Open(ws), "red-spot");
        mod.Manifest.Species.Add(new SpeciesEntry { Id = "anax", Base = "Carcharodontosaurus", Name = "Anax", Genus = "Example", Epithet = "anax" });
        mod.Save();

        var detail = await h.Call("mods.get", new { id = "red-spot" });

        Assert.False(detail.TryGetProperty("skinOrders", out var orders) && orders.ValueKind == JsonValueKind.Object && orders.TryGetProperty(Key, out _));
    }

    [Fact]
    public async Task Set_skin_order_saves_and_returns_the_new_detail()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await WithSpecies(game, "brown", "green");
        var start = StartingId(await h.Call("mods.get", new { id = "red-spot" }));
        var wanted = new[] { "green", start, "brown" };

        var detail = await h.Call("mods.setSkinOrder", new { id = "red-spot", revision = Rev(await h.Call("mods.get", new { id = "red-spot" })), species = Key, order = wanted });

        Assert.Equal(wanted, Order(detail));
        Assert.Equal(wanted, ModProject.Open(Workspace.Open(ws), "red-spot").Manifest.SkinOrders[Key]);
    }

    [Fact]
    public async Task Make_default_swaps_using_the_games_skin_numbers()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await WithSpecies(game, "brown", "green");
        var start = StartingId(await h.Call("mods.get", new { id = "red-spot" }));
        var install = new Tyrant.Core.Install.GameInstall(game.Root, null);
        var path = SkinSlots.PathOf(install);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"format\": 1, \"species\": { \"" + Key + "\": { \"red-spot/green\": 1, \"red-spot/brown\": 2 } } }");

        var before = Order(await h.Call("mods.get", new { id = "red-spot" }));
        Assert.Equal([start, "green", "brown"], before);

        var detail = await h.Call("mods.makeStartingSkin", new { id = "red-spot", revision = Rev(await h.Call("mods.get", new { id = "red-spot" })), skin = "brown" });

        Assert.Equal(["brown", "green", start], Order(detail));
    }
}
