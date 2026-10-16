using System.Text.Json;
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

        var refused = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setPortraits", new
        {
            id = "red-spot", revision = Rev(set), skin = "blue", set = new Dictionary<string, object?> { ["adult"] = new { png = art } },
        }));
        Assert.Equal("MOD_INVALID", refused.DataCode);
        Assert.Contains("'adult' is not a portrait", refused.Message);
    }

    [Fact]
    public async Task A_species_has_portraits_too()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await AssetFixtures.Opened(game);
        var objects = new DumpResult();
        var skin = AssetFixtures.TexturedSkin(objects, "Default", 50);
        objects.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", "Allosaurus", 1), $$"""
            {"speciesID":"Allosaurus Anax", "cost":20000, "massAverageAdult":4010, "matureMaxAge":{"x":15,"y":40}, "skinsData":[{{skin}}]}
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
        var own = Assert.Single(shown.EnumerateArray(), r => r.GetProperty("slot").GetString() == "adultFemale" && r.GetProperty("own").GetBoolean());
        Assert.True(File.Exists(own.GetProperty("file").GetString()));
        Assert.Equal(4, own.GetProperty("width").GetInt32());
        Assert.Equal(4, own.GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task Bad_picture_data_is_refused_naming_the_slot()
    {
        using var game = new FakeGame();
        var (h, ws) = await ModEditorMethodsTests.Opened(game);
        var detail = await ModEditorMethodsTests.WithSkin(h, ws);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setPortraits", new
        {
            id = "red-spot", revision = Rev(detail), skin = "blue",
            set = new Dictionary<string, object?> { ["infant"] = new { data = "not base64!" } },
        }));

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.Contains("'infant'", ex.Message);
    }

    [Fact]
    public async Task Asking_for_portraits_needs_a_skin_or_a_species()
    {
        using var game = new FakeGame();
        var (h, ws) = await ModEditorMethodsTests.Opened(game);
        await ModEditorMethodsTests.WithSkin(h, ws);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.portraits", new { id = "red-spot" }));

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.Contains("Give a skin or a species.", ex.Message);
    }

    [Fact]
    public async Task A_game_portrait_is_written_to_the_preview_cache()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await ModEditorMethodsTests.WithSkinAndPrefab(game);
        // The base skin ("Alt 1") has adultMale portrait guid "abc" in the dump: put that texture in the index.
        var portrait = new Tyrant.Core.Assets.AssetRecord("carch.bundle", 20, "Texture2D", "P_adultMale", "Assets/P_adultMale.png", "abc", null);
        AssetFixtures.WriteIndex(ws, [.. SkinDumps.Textures, SkinDumps.Prefab, portrait],
            Tyrant.Core.Install.GameFingerprint.Compute(new Tyrant.Core.Install.GameInstall(game.Root, null)));

        var shown = (await h.Call("mods.portraits", new { id = "red-spot", skin = "blue" })).GetProperty("portraits");

        var row = Assert.Single(shown.EnumerateArray(), r => r.GetProperty("slot").GetString() == "adultMale");
        Assert.False(row.GetProperty("own").GetBoolean());
        var file = row.GetProperty("file").GetString()!;
        Assert.True(File.Exists(file));
        Assert.StartsWith(Path.Combine(ws, "cache", "previews", "portraits") + Path.DirectorySeparatorChar, file);
        var infant = Assert.Single(shown.EnumerateArray(), r => r.GetProperty("slot").GetString() == "infant");
        Assert.Equal(JsonValueKind.Null, infant.GetProperty("file").ValueKind); // an empty guid: nothing shown
    }

    [Fact]
    public async Task A_game_portrait_that_cannot_be_decoded_shows_nothing_but_does_not_fail_the_call()
    {
        using var game = new FakeGame();
        var (h, ws, reader) = await ModEditorMethodsTests.WithSkinAndPrefab(game);
        var portrait = new Tyrant.Core.Assets.AssetRecord("carch.bundle", 20, "Texture2D", "P_adultMale", "Assets/P_adultMale.png", "abc", null);
        AssetFixtures.WriteIndex(ws, [.. SkinDumps.Textures, SkinDumps.Prefab, portrait],
            Tyrant.Core.Install.GameFingerprint.Compute(new Tyrant.Core.Install.GameInstall(game.Root, null)));
        reader.FailFor.Add(portrait.Ref);

        var shown = (await h.Call("mods.portraits", new { id = "red-spot", skin = "blue" })).GetProperty("portraits");

        var row = Assert.Single(shown.EnumerateArray(), r => r.GetProperty("slot").GetString() == "adultMale");
        Assert.Equal(JsonValueKind.Null, row.GetProperty("file").ValueKind);
        Assert.Equal(12, shown.GetArrayLength());
    }

    [Fact]
    public async Task Species_model_gives_the_starting_skins_maps_per_sex()
    {
        using var game = new FakeGame();
        var (h, ws) = await ModEditorMethodsTests.WithCustomSpeciesSkin(game, "red-spot");

        var male = await h.Call("mods.speciesModel", new { id = "red-spot", speciesId = "anax" });
        var female = await h.Call("mods.speciesModel", new { id = "red-spot", speciesId = "anax", sex = "female" });
        var infant = await h.Call("mods.speciesModel", new { id = "red-spot", speciesId = "anax", sex = "infant" });

        Assert.Equal(SkinDumps.Prefab.Ref, male.GetProperty("prefabRef").GetString());
        Assert.EndsWith(SkinDumps.MaleDiffuse + ".png", male.GetProperty("maps").GetProperty("diffuse").GetString());
        Assert.True(File.Exists(male.GetProperty("maps").GetProperty("normal").GetString()));
        Assert.False(male.GetProperty("maps").TryGetProperty("extra", out _));
        Assert.EndsWith(SkinDumps.FemaleDiffuse + ".png", female.GetProperty("maps").GetProperty("diffuse").GetString());
        Assert.EndsWith(SkinDumps.FemaleExtra + ".png", female.GetProperty("maps").GetProperty("extra").GetString());
        Assert.EndsWith(SkinDumps.MaleDiffuse + ".png", infant.GetProperty("maps").GetProperty("diffuse").GetString()); // no infant map: the adult one
        var previews = Path.Combine(ws, "cache", "previews") + Path.DirectorySeparatorChar;
        Assert.StartsWith(previews, male.GetProperty("maps").GetProperty("diffuse").GetString());
    }

    [Fact]
    public async Task Species_model_of_an_unknown_species_is_not_found()
    {
        using var game = new FakeGame();
        var (h, _) = await ModEditorMethodsTests.WithCustomSpeciesSkin(game, "red-spot");

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.speciesModel", new { id = "red-spot", speciesId = "nope" }));

        Assert.Equal("TARGET_NOT_FOUND", ex.DataCode);
    }
}
