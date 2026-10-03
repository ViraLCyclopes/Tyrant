using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class ModsMethodsTests
{
    private static readonly AssetRecord Texture = new("StandaloneWindows64/carch_assets_assets/t.bundle", 7, "Texture2D",
        "T_carcharodontosaurus_alt1_male_D", "Assets/Art/T_carcharodontosaurus_alt1_male_D.png", "e3583acd2b3b5b14c875f42d110d97ce", null);

    private static async Task<(RpcHarness H, string Ws)> Opened(FakeGame game, bool withLoaderZip = false)
    {
        var options = TestStudio.Options(loaderZip: withLoaderZip ? TestStudio.FakeMelonLoaderZip() : null, dumperDir: TestStudio.FakeGameModsDir());
        var h = new RpcHarness(options);
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        AssetFixtures.WriteIndex(ws, [Texture], GameFingerprint.Compute(new GameInstall(game.Root, null)));
        return (h, ws);
    }

    private static string Png(string ws)
    {
        var path = Path.Combine(ws, "spots.png");
        using var stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }

    private static JsonElement Row(JsonElement list, string id) => list.GetProperty("mods").EnumerateArray().Single(m => m.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Create_and_replace_show_up_in_the_list()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);

        await h.Call("mods.create", new { id = "red-spot", name = "Red spot" });
        var list = await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });

        var row = Row(list, "red-spot");
        Assert.Equal(("Red spot", 1, "notInstalled"), (row.GetProperty("name").GetString(), row.GetProperty("replacements").GetInt32(), row.GetProperty("state").GetString()));
        Assert.False(list.GetProperty("frameworkInstalled").GetBoolean());
    }

    [Fact]
    public async Task Check_reports_errors_and_warnings()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });
        File.Delete(Path.Combine(ws, "mods", "red-spot", "textures", "T_carcharodontosaurus_alt1_male_D.png"));

        var report = await h.Call("mods.check", new { id = "red-spot" });

        Assert.Contains("missing", report.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task Installing_a_mod_installs_the_framework_first()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game, withLoaderZip: true);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });

        var result = await h.RunJob("mods.install", new { id = "red-spot" });
        var list = await h.Call("mods.list");

        Assert.Contains("Installed 'red-spot'", result.GetProperty("message").GetString());
        Assert.True(File.Exists(Path.Combine(game.Root, "Mods", "Tyrant.Framework.dll")));
        Assert.True(File.Exists(Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot", "mod.json")));
        Assert.True(list.GetProperty("frameworkInstalled").GetBoolean());
        Assert.Equal(("installed", true), (Row(list, "red-spot").GetProperty("state").GetString(), Row(list, "red-spot").GetProperty("enabled").GetBoolean()));
    }

    [Fact]
    public async Task A_mod_with_errors_is_not_installed()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game, withLoaderZip: true);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });
        File.Delete(Path.Combine(ws, "mods", "red-spot", "textures", "T_carcharodontosaurus_alt1_male_D.png"));

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.RunJob("mods.install", new { id = "red-spot" }));

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.False(Directory.Exists(Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot")));
    }

    [Fact]
    public async Task Turning_off_and_removing_update_the_list()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game, withLoaderZip: true);
        await h.Call("mods.create", new { id = "red-spot" });
        await h.Call("mods.replace", new { id = "red-spot", texture = Texture.Name, png = Png(ws) });
        await h.RunJob("mods.install", new { id = "red-spot" });

        var off = await h.Call("mods.enable", new { id = "red-spot", enabled = false });
        var removed = await h.Call("mods.remove", new { id = "red-spot" });

        Assert.False(Row(off, "red-spot").GetProperty("enabled").GetBoolean());
        Assert.Equal("notInstalled", Row(removed, "red-spot").GetProperty("state").GetString());
    }
}
