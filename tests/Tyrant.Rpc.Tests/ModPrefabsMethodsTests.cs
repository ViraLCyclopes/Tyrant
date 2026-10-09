using System.Text.Json;
using Tyrant.Core.Models;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class ModPrefabsMethodsTests
{
    private static string Rev(JsonElement detail) => detail.GetProperty("revision").GetString()!;

    private static void Png(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg=="));
    }

    /// <summary>A mod with one skin on Carcharodontosaurus, with the data dump and an index holding the species prefab, and a one-field AnimalPhysics prefab.</summary>
    private static async Task<(RpcHarness H, string Ws, FakeAssetReader Reader)> WithSkinAndPrefab(FakeGame game)
    {
        var reader = new FakeAssetReader();
        var h = new RpcHarness(TestStudio.Options(dumperDir: TestStudio.FakeGameModsDir(), reader: reader));
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        await h.Call("mods.create", new { id = "red-spot", name = "Red spot" });
        SkinDumps.Write(Path.Combine(ws, "data"));
        AssetFixtures.WriteIndex(ws, [.. SkinDumps.Textures, SkinDumps.Prefab],
            Tyrant.Core.Install.GameFingerprint.Compute(new Tyrant.Core.Install.GameInstall(game.Root, null)));
        var dir = Path.Combine(ws, "mods", "red-spot");
        Png(Path.Combine(dir, "skins", "blue"), "male_D.png");
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"blue\",\"species\":\"Carcharodontosaurus\",\"name\":\"Blue\",\"base\":\"1\",\"male\":{\"diffuse\":\"skins/blue/male_D.png\"}}]}");
        reader.PrefabComponentsToReturn = new("Carch", [new PrefabObject("", [new PrefabComponent("AnimalPhysics", null, null, 0, """{"inputParams":{"waterlineHeight":2}}""")])]);
        return (h, ws, reader);
    }

    [Fact]
    public async Task Fields_list_game_values_and_overrides_and_an_edit_round_trips()
    {
        using var game = new FakeGame();
        var (h, _, _) = await WithSkinAndPrefab(game);
        var detail = await h.Call("mods.get", new { id = "red-spot" });

        var set = await h.Call("mods.setPrefabField", new { id = "red-spot", revision = Rev(detail), target = "Carcharodontosaurus", @object = "", component = "AnimalPhysics", field = "inputParams.waterlineHeight", value = 3.5 });
        var fields = await h.Call("mods.prefabFields", new { id = "red-spot", target = "Carcharodontosaurus" });
        var f = fields.GetProperty("fields")[0];

        Assert.Equal("2", f.GetProperty("gameValue").GetString());
        Assert.Equal("3.5", f.GetProperty("value").GetString());
        Assert.Equal("Carcharodontosaurus", set.GetProperty("prefabTargets")[0].GetProperty("target").GetString());
        Assert.True(set.GetProperty("prefabFiles").TryGetProperty("carcharodontosaurus.json", out _));

        var reset = await h.Call("mods.resetPrefabField", new { id = "red-spot", revision = Rev(set), target = "Carcharodontosaurus", @object = "", component = "AnimalPhysics", field = "inputParams.waterlineHeight" });
        Assert.Equal(0, reset.GetProperty("prefabTargets").GetArrayLength());
    }

    [Fact]
    public async Task A_bad_value_is_refused_and_restore_snapshot_puts_files_back()
    {
        using var game = new FakeGame();
        var (h, _, _) = await WithSkinAndPrefab(game);
        var before = await h.Call("mods.get", new { id = "red-spot" });

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setPrefabField",
            new { id = "red-spot", revision = Rev(before), target = "Carcharodontosaurus", @object = "", component = "AnimalPhysics", field = "inputParams.waterlineHeight", value = "deep" }));
        Assert.Equal("MOD_INVALID", ex.DataCode);

        var afterSet = await h.Call("mods.setPrefabField", new { id = "red-spot", revision = Rev(before), target = "Carcharodontosaurus", @object = "", component = "AnimalPhysics", field = "inputParams.waterlineHeight", value = 3.5 });
        Assert.Equal(1, afterSet.GetProperty("prefabTargets").GetArrayLength());

        var restored = await h.Call("mods.restoreSnapshot", new { id = "red-spot", revision = Rev(afterSet), manifest = before.GetProperty("manifestJson").GetString(), prefabs = new Dictionary<string, string>() });
        Assert.Equal(0, restored.GetProperty("prefabTargets").GetArrayLength());
    }

    [Fact]
    public async Task A_vector_override_is_shown_compact_like_the_game_value()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await WithSkinAndPrefab(game);
        reader.PrefabComponentsToReturn = new("Carch", [new PrefabObject("", [new PrefabComponent("AnimalPhysics", null, null, 0, """{"offset":{"x":0,"y":1,"z":0}}""")])]);
        var detail = await h.Call("mods.get", new { id = "red-spot" });

        await h.Call("mods.setPrefabField", new { id = "red-spot", revision = Rev(detail), target = "Carcharodontosaurus", @object = "", component = "AnimalPhysics", field = "offset", value = new { x = 1, y = 2, z = 3 } });
        var f = (await h.Call("mods.prefabFields", new { id = "red-spot", target = "Carcharodontosaurus" })).GetProperty("fields")[0];

        Assert.Equal("{\"x\":1,\"y\":2,\"z\":3}", f.GetProperty("value").GetString());
        Assert.Equal("{\"x\":0,\"y\":1,\"z\":0}", f.GetProperty("gameValue").GetString());
    }
}
