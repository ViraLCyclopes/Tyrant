using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class ModTexturesRpcTests
{
    private static string Rev(System.Text.Json.JsonElement detail) => ModEditorMethodsTests.Rev(detail);

    [Fact]
    public async Task Compression_is_set_for_the_mod_and_one_texture_and_the_estimates_follow()
    {
        using var game = new FakeGame();
        var (h, ws) = await ModEditorMethodsTests.Opened(game);
        var detail = await ModEditorMethodsTests.WithSkin(h, ws);

        var all = await h.Call("mods.setCompression", new { id = "red-spot", revision = Rev(detail), file = (string?)null, mode = "bc7" });
        var one = await h.Call("mods.setCompression", new { id = "red-spot", revision = Rev(all), file = "skins/blue/male_D.png", mode = "auto" });
        var facts = await h.Call("mods.textures", new { id = "red-spot" });

        Assert.Equal("bc7", facts.GetProperty("compression").GetString());
        var rows = facts.GetProperty("textures").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var diffuse = rows.Single(r => r.GetProperty("file").GetString() == "skins/blue/male_D.png");
        Assert.Equal(("colour", "auto", true, "DXT5", 4, 4),
            (diffuse.GetProperty("kind").GetString(), diffuse.GetProperty("mode").GetString(), diffuse.GetProperty("override").GetBoolean(),
             diffuse.GetProperty("format").GetString(), diffuse.GetProperty("width").GetInt32(), diffuse.GetProperty("height").GetInt32()));
        var pattern = rows.Single(r => r.GetProperty("file").GetString() == "skins/blue/male_pattern.png");
        Assert.Equal(("mask", "bc7", false, "BC7"), (pattern.GetProperty("kind").GetString(), pattern.GetProperty("mode").GetString(),
            pattern.GetProperty("override").GetBoolean(), pattern.GetProperty("format").GetString()));
        Assert.Equal(rows.Sum(r => r.GetProperty("bytes").GetInt64()), facts.GetProperty("totalBytes").GetInt64());

        await h.Call("mods.setCompression", new { id = "red-spot", revision = Rev(one), file = "skins/blue/male_D.png", mode = (string?)null });
        var cleared = (await h.Call("mods.textures", new { id = "red-spot" })).GetProperty("textures").EnumerateArray()
            .Single(r => r.GetProperty("file").GetString() == "skins/blue/male_D.png");
        Assert.Equal(("bc7", false), (cleared.GetProperty("mode").GetString(), cleared.GetProperty("override").GetBoolean()));
    }

    [Fact]
    public async Task An_unknown_mode_or_file_is_refused()
    {
        using var game = new FakeGame();
        var (h, ws) = await ModEditorMethodsTests.Opened(game);
        var detail = await ModEditorMethodsTests.WithSkin(h, ws);

        var mode = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setCompression", new { id = "red-spot", revision = Rev(detail), file = (string?)null, mode = "dxt1" }));
        Assert.Equal("MOD_INVALID", mode.DataCode);
        Assert.Contains("auto or bc7", mode.Message);

        var file = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setCompression", new { id = "red-spot", revision = Rev(detail), file = "skins/blue/thumbnail.png", mode = "bc7" }));
        Assert.Contains("skins/blue/thumbnail.png", file.Message);

        await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setCompression", new { id = "red-spot", revision = Rev(detail), file = (string?)null, mode = (string?)null }));
    }
}
