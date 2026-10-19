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
        Assert.Contains("Give a skin or a species, not both.", both.Message);
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
        Assert.Equal(19, rows.GetArrayLength());
        var smooth = rows.EnumerateArray().Single(r => r.GetProperty("key").GetString() == "_Smoothness");
        Assert.Equal(0.3, double.Parse(smooth.GetProperty("inherited").GetString()!, System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal("game", smooth.GetProperty("source").GetString());
        Assert.False(info.GetProperty("gameHasIridescence").GetBoolean());
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
