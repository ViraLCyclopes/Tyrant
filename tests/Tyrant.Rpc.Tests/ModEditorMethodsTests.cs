using System.Text.Json;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class ModEditorMethodsTests
{
    private static async Task<(RpcHarness H, string Ws)> Opened(FakeGame game)
    {
        var h = new RpcHarness(TestStudio.Options(dumperDir: TestStudio.FakeGameModsDir()));
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        await h.Call("mods.create", new { id = "red-spot", name = "Red spot" });
        return (h, ws);
    }

    private static string Png(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        using var stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(Enumerable.Repeat((byte)200, 4 * 4 * 4).ToArray(), 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }

    /// <summary>A mod with one skin whose male diffuse and pattern are the mod's own files.</summary>
    private static async Task<JsonElement> WithSkin(RpcHarness h, string ws)
    {
        var dir = Path.Combine(ws, "mods", "red-spot");
        Png(Path.Combine(dir, "skins", "blue"), "male_D.png");
        Png(Path.Combine(dir, "skins", "blue"), "male_pattern.png");
        var json = "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"blue\",\"species\":\"Carcharodontosaurus\",\"name\":\"Blue\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/blue/male_D.png\",\"pattern\":\"skins/blue/male_pattern.png\"}}]}";
        File.WriteAllText(Path.Combine(dir, "mod.json"), json);
        return await h.Call("mods.get", new { id = "red-spot" });
    }

    private static string Rev(JsonElement detail) => detail.GetProperty("revision").GetString()!;

    [Fact]
    public async Task Get_returns_the_mod_its_skins_and_a_revision()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);

        var detail = await WithSkin(h, ws);

        Assert.Equal("Red spot", detail.GetProperty("name").GetString());
        Assert.Equal(64, Rev(detail).Length);
        var skin = detail.GetProperty("skins")[0];
        Assert.Equal("red-spot/blue", skin.GetProperty("key").GetString());
        Assert.Equal("skins/blue/male_D.png", skin.GetProperty("male").GetProperty("diffuse").GetString());
        Assert.Contains("\"red-spot\"", detail.GetProperty("manifestJson").GetString());
    }

    [Fact]
    public async Task Each_edit_returns_the_new_mod_and_revision()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        var detail = await WithSkin(h, ws);

        var renamed = await h.Call("mods.renameSkin", new { id = "red-spot", revision = Rev(detail), skin = "blue", name = "Ocean" });
        var coloured = await h.Call("mods.setColors", new { id = "red-spot", revision = Rev(renamed), skin = "blue", colors = "{\"pattern\":{\"a\":\"#3060ff\"}}" });
        var details = await h.Call("mods.setDetails", new { id = "red-spot", revision = Rev(coloured), name = "Red spots", version = "1.1.0", author = "me", description = (string?)null });

        Assert.Equal("Ocean", details.GetProperty("skins")[0].GetProperty("name").GetString());
        Assert.Contains("#3060ff", details.GetProperty("skins")[0].GetProperty("colorsJson").GetString());
        Assert.Equal(("Red spots", "1.1.0"), (details.GetProperty("name").GetString(), details.GetProperty("version").GetString()));
        Assert.NotEqual(Rev(detail), Rev(details));
    }

    [Fact]
    public async Task A_write_with_an_old_revision_is_refused_with_MOD_CHANGED()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        var detail = await WithSkin(h, ws);
        File.AppendAllText(Path.Combine(ws, "mods", "red-spot", "mod.json"), "\n"); // edited elsewhere

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.renameSkin", new { id = "red-spot", revision = Rev(detail), skin = "blue", name = "X" }));

        Assert.Equal("MOD_CHANGED", ex.DataCode);
        Assert.Equal("Blue", (await h.Call("mods.get", new { id = "red-spot" })).GetProperty("skins")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Save_manifest_puts_an_earlier_version_back()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        var before = await WithSkin(h, ws);
        var after = await h.Call("mods.renameSkin", new { id = "red-spot", revision = Rev(before), skin = "blue", name = "Ocean" });

        var undone = await h.Call("mods.saveManifest", new { id = "red-spot", revision = Rev(after), manifest = before.GetProperty("manifestJson").GetString() });

        Assert.Equal("Blue", undone.GetProperty("skins")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Skin_files_thumbnail_removal_and_replacements()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        var detail = await WithSkin(h, ws);
        var art = Png(Path.Combine(ws, "art"), "extra.png");

        var withExtra = await h.Call("mods.setSkinFile", new { id = "red-spot", revision = Rev(detail), skin = "blue", sex = "male", slot = "extra", png = art });
        var withThumb = await h.Call("mods.setThumbnail", new { id = "red-spot", revision = Rev(withExtra), skin = "blue", png = art });
        var cleared = await h.Call("mods.setSkinFile", new { id = "red-spot", revision = Rev(withThumb), skin = "blue", sex = "male", slot = "extra", png = (string?)null });
        var removed = await h.Call("mods.removeSkin", new { id = "red-spot", revision = Rev(cleared), skin = "blue", deleteFiles = true });

        Assert.Equal("skins/blue/male_extra.png", withExtra.GetProperty("skins")[0].GetProperty("male").GetProperty("extra").GetString());
        Assert.Equal("skins/blue/thumbnail.png", withThumb.GetProperty("skins")[0].GetProperty("thumbnail").GetString());
        Assert.False(cleared.GetProperty("skins")[0].GetProperty("male").TryGetProperty("extra", out _));
        Assert.Equal(0, removed.GetProperty("skins").GetArrayLength());
        Assert.False(File.Exists(Path.Combine(ws, "mods", "red-spot", "skins", "blue", "male_D.png")));
    }

    [Fact]
    public async Task Removing_a_replacement_returns_the_mod_without_it()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        File.WriteAllText(Path.Combine(ws, "mods", "red-spot", "mod.json"),
            "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[{\"texture\":\"T_A_D\",\"file\":\"textures/a.png\"}]}");
        var detail = await h.Call("mods.get", new { id = "red-spot" });

        var removed = await h.Call("mods.removeReplacement", new { id = "red-spot", revision = Rev(detail), texture = "T_A_D" });

        Assert.Equal(1, detail.GetProperty("replace").GetArrayLength());
        Assert.Equal(0, removed.GetProperty("replace").GetArrayLength());
    }

    [Fact]
    public async Task Colour_previews_and_thumbnails_are_files_in_the_preview_cache()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        await WithSkin(h, ws);

        var preview = await h.Call("mods.colorPreview", new { id = "red-spot", skin = "blue", colors = "{\"pattern\":{\"a\":\"#0000ff\"}}", count = 3, size = 16 });
        var thumb = await h.Call("mods.thumbnail", new { id = "red-spot", file = "skins/blue/male_D.png", size = 8 });

        var files = preview.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).ToList();
        Assert.Equal(3, files.Count);
        var previews = Path.Combine(Path.GetFullPath(ws), "cache", "previews");
        Assert.All(files, f => Assert.StartsWith(previews, f));
        Assert.StartsWith(previews, thumb.GetProperty("file").GetString());
        Assert.Equal(JsonValueKind.Null, (await h.Call("mods.thumbnail", new { id = "red-spot", file = "skins/nope.png" })).GetProperty("file").ValueKind);
    }
}
