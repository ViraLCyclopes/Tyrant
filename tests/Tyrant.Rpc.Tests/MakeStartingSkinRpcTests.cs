using Tyrant.Core.Mods;
using Tyrant.Core.Tests;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;
using static Tyrant.Rpc.Tests.ModEditorMethodsTests;

namespace Tyrant.Rpc.Tests;

public class MakeStartingSkinRpcTests
{
    [Fact]
    public async Task Make_starting_skin_swaps_the_species_starting_skin()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await WithSkinAndPrefab(game);
        await h.Call("mods.addSpecies", new
        {
            id = "red-spot", revision = Rev(await h.Call("mods.get", new { id = "red-spot" })), speciesId = "anax", template = "Carcharodontosaurus",
            name = "Anax", genus = "Example", epithet = "anax", startingSkin = 1,
        });
        var mod = ModProject.Open(Workspace.Open(ws), "red-spot");
        mod.Manifest.Skins.Add(new SkinEntry { Id = "brown", Species = "tyrant.red-spot.anax", Name = "Brown", Base = "0", Male = new() { ["diffuse"] = "skins/blue/male_D.png" } });
        mod.Save();

        var detail = await h.Call("mods.makeStartingSkin", new { id = "red-spot", revision = Rev(await h.Call("mods.get", new { id = "red-spot" })), skin = "brown" });

        var starting = detail.GetProperty("skins").EnumerateArray().Where(s => s.GetProperty("starting").GetBoolean()).Select(s => s.GetProperty("id").GetString());
        Assert.Equal(["brown"], starting);
    }
}
