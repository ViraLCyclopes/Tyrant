using Tyrant.Core.Dumping;
using Tyrant.Core.Mods;
using Tyrant.Core.Tests;
using Tyrant.Dumper.Serialization;
using Tyrant.Framework.Core;

namespace Tyrant.Rpc.Tests;

public class SpeciesDatabaseRpcTests
{
    [Fact]
    public async Task Editor_creates_sqlite_species_edits_stats_and_rejects_external_conflicts()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1), """
            {"speciesID":"Allosaurus Anax", "cost":20000, "massAverageAdult":4010, "matureMaxAge":{"x":15,"y":40}, "diet":"Carnivore",
              "skinsData":[{"skinName":"Default"}]}
            """));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
        await h.Call("mods.create", new { id = "animal-pack" });
        var detail = await h.Call("mods.get", new { id = "animal-pack" });
        var added = await h.Call("mods.addSpecies", new
        {
            id = "animal-pack", revision = detail.GetProperty("revision").GetString(), speciesId = "new-animal", template = "Allosaurus Anax",
            name = "New animal", genus = "Example", epithet = "animal", stats = new { adultMassKg = 4500 },
        });
        var path = Path.Combine(ws, "mods", "animal-pack", AnimalDatabaseFile.FileName);
        Assert.True(File.Exists(path));
        Assert.DoesNotContain("species", File.ReadAllText(Path.Combine(ws, "mods", "animal-pack", "mod.json")));
        var data = await h.Call("mods.animalDatabase", new { id = "animal-pack" });
        var animal = Assert.Single(data.GetProperty("animals").EnumerateArray());
        Assert.Equal(4500, animal.GetProperty("stats").GetProperty("adultMassKg").GetDouble());
        Assert.Equal(20000, animal.GetProperty("cost").GetInt32());
        Assert.Equal(40, animal.GetProperty("stats").GetProperty("maxAgeYears").GetDouble());
        Assert.Equal(4010, animal.GetProperty("inheritedStats").GetProperty("adultMassKg").GetDouble());
        Assert.Contains(animal.GetProperty("dataFields").EnumerateArray(), f => f.GetProperty("path").GetString() == "/diet");
        Assert.Equal("new-animal/description", animal.GetProperty("localization").GetProperty("description").GetString());
        Assert.True(File.Exists(Path.Combine(ws, "mods", "animal-pack", "localization", "en-US", "new-animal", "description.txt")));
        var invalid = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setSpecies", new
        {
            id = "animal-pack", revision = added.GetProperty("revision").GetString(), speciesId = "new-animal",
            name = "Animal", genus = "Example", epithet = "animal", cost = (int?)null, stats = new { matureAgeYears = 50 },
        }));
        Assert.Equal("MOD_INVALID", invalid.DataCode);
        var outside = AnimalDatabaseFile.Read(path);
        outside[0].Name = "SQL editor change";
        AnimalDatabaseFile.Save(path, outside);
        var stale = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setSpecies", new
        {
            id = "animal-pack", revision = added.GetProperty("revision").GetString(), speciesId = "new-animal",
            name = "Old form", genus = "Example", epithet = "animal", cost = (int?)null, stats = new { adultMassKg = 4000 },
        }));
        Assert.Equal("MOD_CHANGED", stale.DataCode);
        Assert.Equal("SQL editor change", Assert.Single(AnimalDatabaseFile.Read(path)).Name);
    }
}
