using System.Text.Json;
using Tyrant.Core.Models;
using Tyrant.Core.Tests;
using static Tyrant.Rpc.Tests.ModEditorMethodsTests;

namespace Tyrant.Rpc.Tests;

public class ModMaterialsRpcTests
{
    private const string Animal = "Carch_Mat";

    private static MaterialModel CarchMaterial() => new(Animal, [new TextureSlot("_AdultDiffuse", null, 1)])
    {
        Floats = new Dictionary<string, float> { ["_Smoothness"] = 0.3f },
    };

    [Fact]
    public async Task Set_materials_round_trips_through_the_detail()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [CarchMaterial()];
        var json = "{\"" + Animal + "\":{\"iridescence\":{\"strength\":0.5}}}";

        var detail = await h.Call("mods.get", new { id = "red-spot" });
        detail = await h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(detail), skin = "blue", materials = json });
        Assert.Contains("\"strength\"", detail.GetProperty("skins")[0].GetProperty("materialsJson").GetString());

        detail = await h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(detail), species = "Carcharodontosaurus", materials = json });
        var species = detail.GetProperty("speciesMaterials");
        Assert.Equal("Carcharodontosaurus", species[0].GetProperty("species").GetString());
        Assert.Equal(0, detail.GetProperty("models").GetArrayLength());

        var both = await Assert.ThrowsAnyAsync<Exception>(() =>
            h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(detail), skin = "blue", species = "Carcharodontosaurus", materials = json }));
        Assert.Contains("Give a skin, a species or a vanilla skin, only one.", both.Message);
    }

    [Fact]
    public async Task Material_info_lists_every_setting_with_its_inherited_value()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [CarchMaterial()];

        var info = await h.Call("mods.materialInfo", new { id = "red-spot", skin = "blue" });

        Assert.Equal(Animal, info.GetProperty("name").GetString());
        var rows = info.GetProperty("properties");
        Assert.Equal(21, rows.GetArrayLength());
        var smooth = rows.EnumerateArray().Single(r => r.GetProperty("key").GetString() == "_Smoothness");
        Assert.Equal(0.3, double.Parse(smooth.GetProperty("inherited").GetString()!, System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal("game", smooth.GetProperty("source").GetString());
        Assert.False(info.GetProperty("gameHasRamp").GetBoolean());
        // The skin is based on Alt 1, whose male asset shimmers at 0.25 (its female one does not): the skin's baseline.
        Assert.Equal(0.25, info.GetProperty("gameIridescence").GetDouble(), 3);
        // A species page cannot tell: only some of its skins shimmer.
        var species = await h.Call("mods.materialInfo", new { id = "red-spot", species = "Carcharodontosaurus" });
        Assert.Equal(JsonValueKind.Null, species.GetProperty("gameIridescence").ValueKind);
    }

    [Fact]
    public async Task Vanilla_skin_materials_round_trip_through_the_detail()
    {
        using var game = new FakeGame();
        var (h, ws, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [CarchMaterial()];
        var json = "{\"" + Animal + "\":{\"settings\":{\"_Smoothness\":0.6},\"iridescence\":{\"strength\":0.5}}}";
        const string vanilla = "Carcharodontosaurus/Alt 1";

        var picker = await h.Call("mods.vanillaSkins", new { id = "red-spot", species = "Carcharodontosaurus" });
        Assert.Contains("Alt 1", picker.GetProperty("names").EnumerateArray().Select(n => n.GetString()!));

        var detail = await h.Call("mods.get", new { id = "red-spot" });
        detail = await h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(detail), vanillaSkin = vanilla, materials = json });
        var row = detail.GetProperty("vanillaSkinMaterials")[0];
        Assert.Equal("Carcharodontosaurus", row.GetProperty("species").GetString());
        Assert.Equal("Alt 1", row.GetProperty("skin").GetString());
        Assert.Contains("\"strength\"", row.GetProperty("materialsJson").GetString());
        Assert.Contains("materials/vanilla-skins/Carcharodontosaurus-Alt 1/materials.json", detail.GetProperty("materialFiles").EnumerateObject().Select(f => f.Name));

        var png = Png(Path.Combine(ws, "art"), "fur.png");
        detail = await h.Call("mods.setMaterialFile", new { id = "red-spot", revision = Rev(detail), vanillaSkin = vanilla, material = Animal, part = "furMask", png });
        Assert.Contains("fur-mask.png", detail.GetProperty("vanillaSkinMaterials")[0].GetProperty("materialsJson").GetString());
        Assert.True(File.Exists(Path.Combine(ws, "mods", "red-spot", "materials", "vanilla-skins", "Carcharodontosaurus-Alt 1", "fur-mask.png")));

        var info = await h.Call("mods.materialInfo", new { id = "red-spot", vanillaSkin = vanilla });
        Assert.Equal(Animal, info.GetProperty("name").GetString());
        Assert.Equal(0.25, info.GetProperty("gameIridescence").GetDouble(), 3);
        var smooth = info.GetProperty("properties").EnumerateArray().Single(r => r.GetProperty("key").GetString() == "_Smoothness");
        Assert.Equal("game", smooth.GetProperty("source").GetString()); // a vanilla skin sits on its species layer, which is empty here
        var preview = await h.Call("mods.materialPreview", new { id = "red-spot", vanillaSkin = vanilla, materials = json });
        Assert.Equal(0.5, preview.GetProperty("strength").GetDouble(), 3);

        detail = await h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(detail), vanillaSkin = vanilla });
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("vanillaSkinMaterials").ValueKind);

        var two = await Assert.ThrowsAnyAsync<Exception>(() =>
            h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(detail), species = "Carcharodontosaurus", vanillaSkin = vanilla, materials = json }));
        Assert.Contains("only one", two.Message);
    }

    [Fact]
    public async Task Material_info_lists_droop_direction_with_three_components()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [CarchMaterial()];

        var info = await h.Call("mods.materialInfo", new { id = "red-spot", skin = "blue" });

        var rows = info.GetProperty("properties").EnumerateArray().ToList();
        int Components(string key) => rows.Single(r => r.GetProperty("key").GetString() == key).GetProperty("components").GetInt32();
        Assert.Equal("vector", rows.Single(r => r.GetProperty("key").GetString() == "_DroopDirection").GetProperty("kind").GetString());
        Assert.Equal(3, Components("_DroopDirection"));
        Assert.Equal(2, Components("_DetailScale"));
        Assert.Equal(0, Components("_Smoothness"));
    }

    [Fact]
    public async Task Species_presets_list_the_shimmering_and_furred_species_and_give_a_preset_block()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [new MaterialModel(Animal, [new TextureSlot("_AdultDiffuse", null, 1), new TextureSlot(Tyrant.Core.Mods.AnimalMaterial.RampSlot, null, 2)])
        {
            Floats = new Dictionary<string, float> { ["_IridescenceScale"] = -0.25f, ["_IridescencePower"] = -0.3f, ["_IridescenceBias"] = 1.3f },
        }];

        var lists = await h.Call("mods.speciesPresets", new { });
        var shimmering = lists.GetProperty("iridescence");
        Assert.Equal(1, shimmering.GetArrayLength()); // Carcharodontosaurus' Alt 1 male asset shimmers in the test dump
        Assert.Equal("Carcharodontosaurus", shimmering[0].GetProperty("species").GetString());
        Assert.Equal("Carcharodontosaurus", shimmering[0].GetProperty("name").GetString());
        Assert.Equal(0, lists.GetProperty("fur").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, lists.GetProperty("problem").ValueKind);

        var preset = await h.Call("mods.speciesPreset", new { species = "Carcharodontosaurus", kind = "iridescence", target = "Titanoboa" });
        using var block = JsonDocument.Parse(preset.GetProperty("materialJson").GetString()!);
        var i = block.RootElement.GetProperty("iridescence");
        Assert.Equal("Carcharodontosaurus", i.GetProperty("ramp").GetProperty("species").GetString());
        Assert.Equal("everywhere", i.GetProperty("mask").GetString()); // its material has no mask texture
        Assert.Equal(-0.25, i.GetProperty("scale").GetDouble(), 3);
        Assert.Equal(0.25, i.GetProperty("strength").GetProperty("normal").GetDouble(), 3);
        Assert.Equal(0, i.GetProperty("strength").GetProperty("albino").GetDouble(), 3); // its albino data's own strength

        var badKind = await Assert.ThrowsAnyAsync<Exception>(() => h.Call("mods.speciesPreset", new { species = "Carcharodontosaurus", kind = "scales" }));
        Assert.Contains("\"iridescence\" or \"fur\"", badKind.Message);
        var unknown = await Assert.ThrowsAnyAsync<Exception>(() => h.Call("mods.speciesPreset", new { species = "Nope", kind = "fur" }));
        Assert.Contains("\"Nope\" is not a species in the game data", unknown.Message);
    }

    [Fact]
    public async Task Material_info_carries_the_fur_block_below()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [CarchMaterial()];
        var before = await h.Call("mods.get", new { id = "red-spot" });
        await h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(before), species = "Carcharodontosaurus",
            materials = "{\"" + Animal + "\":{\"fur\":{\"grow\":true,\"mask\":\"game\"}}}" });

        var info = await h.Call("mods.materialInfo", new { id = "red-spot", skin = "blue" });
        using var fur = JsonDocument.Parse(info.GetProperty("inheritedFurJson").GetString()!);
        Assert.True(fur.RootElement.GetProperty("grow").GetBoolean());
        Assert.Equal("game", fur.RootElement.GetProperty("mask").GetString());
        var species = await h.Call("mods.materialInfo", new { id = "red-spot", species = "Carcharodontosaurus" });
        Assert.Equal(JsonValueKind.Null, species.GetProperty("inheritedFurJson").ValueKind); // nothing lies below a species
    }

    [Fact]
    public async Task Baby_strength_and_fur_age_round_trip_and_show_below()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [CarchMaterial()];
        var json = "{\"" + Animal + "\":{\"iridescence\":{\"strength\":0,\"babyStrength\":0.3},\"fur\":{\"age\":\"adults\"}}}";

        var detail = await h.Call("mods.get", new { id = "red-spot" });
        detail = await h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(detail), skin = "blue", materials = json });
        using (var saved = JsonDocument.Parse(detail.GetProperty("skins")[0].GetProperty("materialsJson").GetString()!))
        {
            var block = saved.RootElement.GetProperty(Animal);
            Assert.Equal(0.3, block.GetProperty("iridescence").GetProperty("babyStrength").GetDouble(), 3);
            Assert.Equal("adults", block.GetProperty("fur").GetProperty("age").GetString());
        }

        detail = await h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(detail), species = "Carcharodontosaurus", materials = json });
        var info = await h.Call("mods.materialInfo", new { id = "red-spot", skin = "blue" });
        using var iridescence = JsonDocument.Parse(info.GetProperty("inheritedIridescenceJson").GetString()!);
        Assert.Equal(0.3, iridescence.RootElement.GetProperty("babyStrength").GetDouble(), 3);
        using var fur = JsonDocument.Parse(info.GetProperty("inheritedFurJson").GetString()!);
        Assert.Equal("adults", fur.RootElement.GetProperty("age").GetString());

        var bad = await Assert.ThrowsAnyAsync<Exception>(() => h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(detail), skin = "blue",
            materials = "{\"" + Animal + "\":{\"iridescence\":{\"babyStrength\":1.5}}}" }));
        Assert.Contains("babyStrength\" must be from 0 to 1", bad.Message);
    }

    [Fact]
    public async Task Restore_snapshot_brings_back_material_files()
    {
        using var game = new FakeGame();
        var (h, ws, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [CarchMaterial()];
        var json = "{\"" + Animal + "\":{\"iridescence\":{\"strength\":0.5}}}";
        var before = await h.Call("mods.get", new { id = "red-spot" });
        var withSkin = await h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(before), skin = "blue", materials = json });
        var detail = await h.Call("mods.setMaterials", new { id = "red-spot", revision = Rev(withSkin), vanillaSkin = "Carcharodontosaurus/Alt 1", materials = json });
        var skinFile = Path.Combine(ws, "mods", "red-spot", "materials", "skins", "blue", "materials.json");
        var vanillaFile = Path.Combine(ws, "mods", "red-spot", "materials", "vanilla-skins", "Carcharodontosaurus-Alt 1", "materials.json");

        // Without a materials map the files stay as they are.
        var kept = await h.Call("mods.restoreSnapshot", new
        {
            id = "red-spot", revision = Rev(detail), manifest = before.GetProperty("manifestJson").GetString(), prefabs = new Dictionary<string, string>(),
        });
        Assert.True(File.Exists(skinFile));
        Assert.True(File.Exists(vanillaFile));
        Assert.Equal(JsonValueKind.Array, kept.GetProperty("vanillaSkinMaterials").ValueKind);

        // With one, they are put back exactly: the vanilla skin's file goes, the skin's stays.
        var files = withSkin.GetProperty("materialFiles").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetString()!);
        var back = await h.Call("mods.restoreSnapshot", new
        {
            id = "red-spot", revision = Rev(kept), manifest = withSkin.GetProperty("manifestJson").GetString(), prefabs = new Dictionary<string, string>(), materials = files,
        });
        Assert.True(File.Exists(skinFile));
        Assert.False(File.Exists(vanillaFile));
        Assert.Contains("\"strength\"", back.GetProperty("skins")[0].GetProperty("materialsJson").GetString());
        Assert.Equal(JsonValueKind.Null, back.GetProperty("vanillaSkinMaterials").ValueKind);
    }

    [Fact]
    public async Task Material_preview_without_a_mod_strength_has_the_vanilla_skins_own_per_sex()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [CarchMaterial()];

        var male = await h.Call("mods.materialPreview", new { id = "red-spot", skin = "blue", sex = "male" });
        var female = await h.Call("mods.materialPreview", new { id = "red-spot", skin = "blue", sex = "female" });

        Assert.Equal(0.25, male.GetProperty("strength").GetDouble(), 3);
        Assert.Equal(0, female.GetProperty("strength").GetDouble(), 3);
    }

    [Fact]
    public async Task Thin_film_gives_32_stops()
    {
        using var game = new FakeGame();
        var (h, _) = await Opened(game);

        var film = await h.Call("mods.thinFilm", new { thickness = 264 });

        var stops = film.GetProperty("stops").EnumerateArray().Select(s => s.GetString()!).ToArray();
        Assert.Equal(32, stops.Length);
        Assert.All(stops, s => Assert.Matches("^#[0-9a-f]{6}$", s));
        var last = stops[^1];
        var r = Convert.ToInt32(last[1..3], 16);
        var g = Convert.ToInt32(last[3..5], 16);
        var b = Convert.ToInt32(last[5..7], 16);
        Assert.True(g > r && g > b, last);
    }

    [Fact]
    public async Task Material_preview_writes_a_ramp_png()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await WithSkinAndPrefab(game);
        reader.PrefabMaterials = [CarchMaterial()];
        var draft = "{\"" + Animal + "\":{\"iridescence\":{\"strength\":0.8,\"ramp\":{\"stops\":[\"#ff0000\",\"#0000ff\"]}}}}";

        var preview = await h.Call("mods.materialPreview", new { id = "red-spot", skin = "blue", materials = draft });

        Assert.True(preview.GetProperty("on").GetBoolean());
        Assert.Equal(0.8, preview.GetProperty("strength").GetDouble(), 3);
        Assert.True(File.Exists(preview.GetProperty("ramp").GetString()));
        Assert.Equal(4, preview.GetProperty("maskSt").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("smoothness").ValueKind); // the game's 0.3 alone leaves the extra map's smoothness alone
    }
}
