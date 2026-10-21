using Tyrant.Core.Dumping;
using Tyrant.Core.Mods;
using Tyrant.Core.Tests;
using Tyrant.Dumper.Serialization;
using Tyrant.Framework.Core;

namespace Tyrant.Rpc.Tests;

public class SpeciesDatabaseRpcTests
{
    [Fact]
    public async Task A_templates_stats_and_cost_can_be_read_before_creating_a_species()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1), """
            {"speciesID":"Allosaurus Anax", "cost":20000, "massAverageAdult":4010, "matureMaxAge":{"x":15,"y":40}, "skinsData":[{"skinName":"Default"}]}
            """));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });

        var found = await h.Call("mods.speciesTemplate", new { template = "Allosaurus Anax" });
        var missing = await h.Call("mods.speciesTemplate", new { template = "Nope" });

        Assert.True(found.GetProperty("found").GetBoolean());
        Assert.Equal(20000, found.GetProperty("cost").GetInt32());
        Assert.Equal(4010, found.GetProperty("stats").GetProperty("adultMassKg").GetDouble());
        Assert.Equal(40, found.GetProperty("stats").GetProperty("maxAgeYears").GetDouble());
        Assert.False(missing.GetProperty("found").GetBoolean());
        Assert.Empty(missing.GetProperty("stats").EnumerateObject());
    }

    [Fact]
    public async Task Nursery_and_paleopedia_pictures_are_set_and_cleared_separately()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        var skin = AssetFixtures.TexturedSkin(objects, "Default", 50);
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1), $$"""
            {"speciesID":"Allosaurus Anax", "cost":20000, "skinsData":[{{skin}}]}
            """));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
        await h.Call("mods.create", new { id = "mod-a" });
        string Rev(System.Text.Json.JsonElement d) => d.GetProperty("revision").GetString()!;
        var a = await h.Call("mods.get", new { id = "mod-a" });
        a = await h.Call("mods.addSpecies", new { id = "mod-a", revision = Rev(a), speciesId = "mirabilis", template = "Allosaurus Anax", name = "Mirabilis", genus = "Example", epithet = "mirabilis" });
        var png = Path.Combine(ws, "picture.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWWQAAAAASUVORK5CYII="));

        a = await h.Call("mods.setSpeciesIcon", new { id = "mod-a", revision = Rev(a), speciesId = "mirabilis", png });
        a = await h.Call("mods.setSpeciesPaleopediaPicture", new { id = "mod-a", revision = Rev(a), speciesId = "mirabilis", png });
        var animal = Assert.Single((await h.Call("mods.animalDatabase", new { id = "mod-a" })).GetProperty("animals").EnumerateArray());
        Assert.Equal("icons/mirabilis.png", animal.GetProperty("icon").GetString());
        Assert.Equal("icons/mirabilis-paleopedia.png", animal.GetProperty("paleopediaPicture").GetString());

        await h.Call("mods.setSpeciesPaleopediaPicture", new { id = "mod-a", revision = Rev(a), speciesId = "mirabilis", png = (string?)null });
        animal = Assert.Single((await h.Call("mods.animalDatabase", new { id = "mod-a" })).GetProperty("animals").EnumerateArray());
        Assert.Equal("icons/mirabilis.png", animal.GetProperty("icon").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, animal.GetProperty("paleopediaPicture").ValueKind);
    }

    [Fact]
    public async Task Species_texts_are_listed_per_language_and_saved_with_the_species()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        var skin = AssetFixtures.TexturedSkin(objects, "Default", 50);
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1), $$"""
            {"speciesID":"Allosaurus Anax", "cost":20000, "skinsData":[{{skin}}]}
            """));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
        await h.Call("mods.create", new { id = "mod-a" });
        string Rev(System.Text.Json.JsonElement d) => d.GetProperty("revision").GetString()!;
        var a = await h.Call("mods.get", new { id = "mod-a" });
        a = await h.Call("mods.addSpecies", new { id = "mod-a", revision = Rev(a), speciesId = "mirabilis", template = "Allosaurus Anax", name = "Mirabilis", genus = "Example", epithet = "mirabilis" });
        var animal = Assert.Single((await h.Call("mods.animalDatabase", new { id = "mod-a" })).GetProperty("animals").EnumerateArray());
        Assert.Equal("Mirabilis", animal.GetProperty("texts").GetProperty("en-US").GetProperty("name").GetString());
        Assert.Equal("", animal.GetProperty("texts").GetProperty("fr").GetProperty("description").GetString());

        await h.Call("mods.setSpecies", new
        {
            id = "mod-a", revision = Rev(a), speciesId = "mirabilis", name = "Mirabilis Rex", genus = "Example", epithet = "mirabilis",
            cost = (int?)null, stats = new Dictionary<string, double>(),
            texts = new Dictionary<string, Dictionary<string, string>>
            {
                ["en-US"] = new() { ["description"] = "Big.\n\nVery big.", ["characterNotes"] = "Grumpy." },
                ["fr"] = new() { ["description"] = "Grand.", ["name"] = "Mirabilis roi" },
            },
        });

        var texts = Assert.Single((await h.Call("mods.animalDatabase", new { id = "mod-a" })).GetProperty("animals").EnumerateArray()).GetProperty("texts");
        Assert.Equal("Mirabilis Rex", texts.GetProperty("en-US").GetProperty("name").GetString());
        Assert.Equal("Big.\n\nVery big.", texts.GetProperty("en-US").GetProperty("description").GetString());
        Assert.Equal("Grumpy.", texts.GetProperty("en-US").GetProperty("characterNotes").GetString());
        Assert.Equal("Grand.", texts.GetProperty("fr").GetProperty("description").GetString());
        Assert.Equal("Mirabilis roi", texts.GetProperty("fr").GetProperty("name").GetString());
        Assert.Equal("", texts.GetProperty("es").GetProperty("description").GetString());
    }

    [Fact]
    public async Task Editor_creates_sqlite_species_edits_stats_and_rejects_external_conflicts()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        var skin = AssetFixtures.TexturedSkin(objects, "Default", 50);
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1), $$"""
            {"speciesID":"Allosaurus Anax", "cost":20000, "massAverageAdult":4010, "matureMaxAge":{"x":15,"y":40}, "diet":"Carnivore",
              "skinsData":[{{skin}}]}
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
        // Species live in animals.db; mod.json holds only the starting skin that points at it.
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(ws, "mods", "animal-pack", "mod.json"))))
            Assert.False(json.RootElement.TryGetProperty("species", out _));
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

    [Fact]
    public async Task Starting_skin_swatch_custom_species_and_uses_species_from()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        var skins = AssetFixtures.TexturedSkin(objects, "Default", 50) + "," + AssetFixtures.TexturedSkin(objects, "Albino", 51);
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1), $$"""
            {"speciesID":"Allosaurus Anax", "cost":20000, "skinsData":[{{skins}}]}
            """));
        DumpWriter.Write(Path.Combine(ws, "data"), objects, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
        await h.Call("mods.create", new { id = "mod-a", name = "Mod A" });
        await h.Call("mods.create", new { id = "mod-b" });
        string Rev(System.Text.Json.JsonElement d) => d.GetProperty("revision").GetString()!;
        var a = await h.Call("mods.get", new { id = "mod-a" });
        a = await h.Call("mods.addSpecies", new
        {
            id = "mod-a", revision = Rev(a), speciesId = "mirabilis", template = "Allosaurus Anax", name = "Mirabilis", genus = "Example", epithet = "mirabilis", startingSkin = 1,
        });
        a = await h.Call("mods.setSpeciesStartingSkin", new { id = "mod-a", revision = Rev(a), speciesId = "mirabilis", startingSkin = 1, name = "Ghost" });
        var png = Path.Combine(ws, "swatch.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWWQAAAAASUVORK5CYII="));
        await h.Call("mods.setSpeciesSwatch", new { id = "mod-a", revision = Rev(a), speciesId = "mirabilis", png });

        var animal = Assert.Single((await h.Call("mods.animalDatabase", new { id = "mod-a" })).GetProperty("animals").EnumerateArray());
        Assert.Equal(1, animal.GetProperty("startingSkin").GetInt32());
        Assert.Equal("Ghost", animal.GetProperty("startingSkinName").GetString());
        Assert.Equal("icons/mirabilis.swatch.png", animal.GetProperty("swatch").GetString());

        var forB = (await h.Call("mods.customSpecies", new { id = "mod-b" })).GetProperty("species").EnumerateArray().ToList();
        var row = Assert.Single(forB);
        Assert.Equal(("tyrant.mod-a.mirabilis", "Mod A", false), (row.GetProperty("speciesId").GetString(), row.GetProperty("modName").GetString(), row.GetProperty("own").GetBoolean()));
        Assert.Equal("Ghost", Assert.Single(row.GetProperty("skins").EnumerateArray()).GetProperty("name").GetString());
        Assert.True(Assert.Single((await h.Call("mods.customSpecies", new { id = "mod-a" })).GetProperty("species").EnumerateArray()).GetProperty("own").GetBoolean());

        // The dump has no textures, so mods.addSkin cannot copy a base; write the skin (with its own texture) the way the editor would save it.
        var dirB = Path.Combine(ws, "mods", "mod-b");
        Directory.CreateDirectory(Path.Combine(dirB, "skins", "blue"));
        File.Copy(png, Path.Combine(dirB, "skins", "blue", "male_D.png"));
        File.WriteAllText(Path.Combine(dirB, "mod.json"),
            "{\"format\":1,\"id\":\"mod-b\",\"name\":\"mod-b\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"blue\",\"species\":\"tyrant.mod-a.mirabilis\",\"name\":\"Blue\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/blue/male_D.png\"}}]}");
        var b = await h.Call("mods.get", new { id = "mod-b" });
        var use = Assert.Single(b.GetProperty("usesSpeciesFrom").EnumerateArray());
        Assert.Equal(("mod-a", 1, true, false), (use.GetProperty("modId").GetString(), use.GetProperty("skins").GetInt32(), use.GetProperty("inWorkspace").GetBoolean(), use.GetProperty("installed").GetBoolean()));
        var list = await h.Call("mods.list", new { });
        var rowB = list.GetProperty("mods").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "mod-b");
        var rowUse = Assert.Single(rowB.GetProperty("usesSpeciesFrom").EnumerateArray());
        Assert.Equal(("mod-a", "Mod A", 1, true, false, false), (rowUse.GetProperty("modId").GetString(), rowUse.GetProperty("modName").GetString(),
            rowUse.GetProperty("skins").GetInt32(), rowUse.GetProperty("inWorkspace").GetBoolean(), rowUse.GetProperty("installed").GetBoolean(), rowUse.GetProperty("enabled").GetBoolean()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, list.GetProperty("mods").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "mod-a").GetProperty("usesSpeciesFrom").ValueKind);
    }
}
