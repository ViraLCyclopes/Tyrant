using Tyrant.Core.Dumping;
using Tyrant.Core.Tests;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Rpc.Tests;

public class ModPortraitsRpcTests
{
    private static string Rev(System.Text.Json.JsonElement detail) => ModEditorMethodsTests.Rev(detail);

    [Fact]
    public async Task Portraits_are_set_listed_and_refused_for_unknown_slots()
    {
        using var game = new FakeGame();
        var (h, ws) = await ModEditorMethodsTests.Opened(game);
        var detail = await ModEditorMethodsTests.WithSkin(h, ws);
        var art = ModEditorMethodsTests.Png(Path.Combine(ws, "art"), "p.png");
        var data = Convert.ToBase64String(File.ReadAllBytes(art));

        var set = await h.Call("mods.setPortraits", new
        {
            id = "red-spot", revision = Rev(detail), skin = "blue",
            set = new Dictionary<string, object?> { ["adultMale"] = new { png = art }, ["infant"] = new { data, rendered = true } },
        });

        var portraits = set.GetProperty("skins")[0].GetProperty("portraits");
        Assert.Equal("skins/blue/portraits/adultMale.png", portraits.GetProperty("adultMale").GetString());
        Assert.Equal("skins/blue/portraits/infant.render.png", portraits.GetProperty("infant").GetString());

        var shown = (await h.Call("mods.portraits", new { id = "red-spot", skin = "blue" })).GetProperty("portraits");
        Assert.Equal(12, shown.GetArrayLength());
        Assert.True(shown[0].GetProperty("own").GetBoolean());
        Assert.True(File.Exists(shown[0].GetProperty("file").GetString()));
        Assert.Equal(4, shown[0].GetProperty("width").GetInt32());
        Assert.True(shown[8].GetProperty("rendered").GetBoolean());

        await Assert.ThrowsAnyAsync<Exception>(() => h.Call("mods.setPortraits", new
        {
            id = "red-spot", revision = Rev(set), skin = "blue", set = new Dictionary<string, object?> { ["adult"] = new { png = art } },
        }));
    }

    [Fact]
    public async Task A_species_has_portraits_too()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1), """
            {"speciesID":"Allosaurus Anax", "cost":20000, "massAverageAdult":4010, "matureMaxAge":{"x":15,"y":40}, "skinsData":[{"skinName":"Default"}]}
            """));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
        await h.Call("mods.create", new { id = "mod-a" });
        var detail = await h.Call("mods.get", new { id = "mod-a" });
        var added = await h.Call("mods.addSpecies", new
        {
            id = "mod-a", revision = Rev(detail), speciesId = "mirabilis", template = "Allosaurus Anax",
            name = "Mirabilis", genus = "Example", epithet = "mirabilis",
        });
        var art = ModEditorMethodsTests.Png(Path.Combine(ws, "art"), "f.png");

        await h.Call("mods.setSpeciesPortraits", new
        {
            id = "mod-a", revision = Rev(added), speciesId = "mirabilis",
            set = new Dictionary<string, object?> { ["adultFemale"] = new { png = art } },
        });

        var animal = Assert.Single((await h.Call("mods.animalDatabase", new { id = "mod-a" })).GetProperty("animals").EnumerateArray());
        Assert.Equal("portraits/mirabilis/adultFemale.png", animal.GetProperty("portraits").GetProperty("adultFemale").GetString());
        var shown = (await h.Call("mods.portraits", new { id = "mod-a", speciesId = "mirabilis" })).GetProperty("portraits");
        Assert.Equal(12, shown.GetArrayLength());
        Assert.Contains(shown.EnumerateArray(), r => r.GetProperty("slot").GetString() == "adultFemale" && r.GetProperty("own").GetBoolean());
    }
}
