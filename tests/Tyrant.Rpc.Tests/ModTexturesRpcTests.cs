using System.Text.Json;
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
        Assert.Equal("TARGET_NOT_FOUND", file.DataCode);

        var neither = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.setCompression", new { id = "red-spot", revision = Rev(detail), file = (string?)null, mode = (string?)null }));
        Assert.Equal("MOD_INVALID", neither.DataCode);
    }

    private static async Task<(RpcHarness H, string Ws)> OpenedWithLoader(FakeGame game, TimeSpan? progressInterval = null)
    {
        var h = new RpcHarness(TestStudio.Options(loaderZip: TestStudio.FakeMelonLoaderZip(), dumperDir: TestStudio.FakeGameModsDir()), progressInterval);
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        await h.Call("mods.create", new { id = "red-spot", name = "Red spot" });
        return (h, ws);
    }

    private static void AssertPack(Stream stream)
    {
        var pack = Tyrant.Framework.Core.TexturePack.Read(stream);
        Assert.Equal(["skins/blue/male_D.png", "skins/blue/male_pattern.png"], pack.Entries.Select(e => e.Source).Order().ToArray());
    }

    [Fact]
    public async Task Installing_cooks_the_textures_into_the_game_and_the_list_says_so()
    {
        using var game = new FakeGame();
        var (h, ws) = await OpenedWithLoader(game);
        await ModEditorMethodsTests.WithSkin(h, ws);

        await h.RunJob("mods.install", new { id = "red-spot" });
        var list = await h.Call("mods.list");

        var path = Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot", "textures.tpack");
        Assert.True(File.Exists(path));
        using (var stream = File.OpenRead(path)) AssertPack(stream);
        Assert.Equal("current", list.GetProperty("mods").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "red-spot").GetProperty("cooked").GetString());
    }

    [Fact]
    public async Task Exporting_puts_the_cooked_pack_in_the_zip()
    {
        using var game = new FakeGame();
        var (h, ws) = await ModEditorMethodsTests.Opened(game);
        await ModEditorMethodsTests.WithSkin(h, ws);

        var zip = (await h.RunJob("mods.export", new { id = "red-spot" })).GetProperty("path").GetString()!;

        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        using var entry = archive.GetEntry("UserData/Tyrant/Mods/red-spot/textures.tpack")!.Open();
        using var copy = new MemoryStream();
        entry.CopyTo(copy);
        copy.Position = 0;
        AssertPack(copy);
    }

    private static IEnumerable<JsonElement> Cooking(RpcHarness h) =>
        h.Notifications.Where(n => n.GetProperty("method").GetString() == "job.progress").Select(n => n.GetProperty("params"))
            .Where(p => p.GetProperty("message").GetString()!.StartsWith("Cooking textures"));

    [Fact]
    public async Task Installing_reports_cooking_progress_between_the_install_and_the_end()
    {
        using var game = new FakeGame();
        var (h, ws) = await OpenedWithLoader(game, TimeSpan.Zero);
        await ModEditorMethodsTests.WithSkin(h, ws);

        await h.RunJob("mods.install", new { id = "red-spot" });

        var cooking = Cooking(h).ToList();
        Assert.Contains(cooking, p => p.GetProperty("message").GetString()!.StartsWith("Cooking textures 1/2 (skins/blue/male_"));
        Assert.Contains(cooking, p => p.GetProperty("message").GetString()!.StartsWith("Cooking textures 2/2 (skins/blue/male_"));
        Assert.All(cooking, p => Assert.InRange(p.GetProperty("fraction").GetDouble(), 0.8, 0.98));
    }

    [Fact]
    public async Task Cancelling_the_job_while_cooking_stops_it_and_installs_nothing()
    {
        using var game = new FakeGame();
        var (h, ws) = await OpenedWithLoader(game, TimeSpan.Zero);
        await ModEditorMethodsTests.WithSkin(h, ws);
        h.OnNotification = n =>
        {
            if (n.GetProperty("method").GetString() != "job.progress") return;
            var p = n.GetProperty("params");
            if (p.GetProperty("message").GetString()!.StartsWith("Cooking textures 1/2 (")) h.Jobs.Cancel(p.GetProperty("jobId").GetString()!);
        };

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.RunJob("mods.install", new { id = "red-spot" }));

        Assert.Equal(Tyrant.Rpc.Protocol.RpcErrorCodes.Cancelled, ex.Code);
        Assert.False(File.Exists(Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot", "textures.tpack")));
        Assert.False(File.Exists(Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot", "mod.json")));
    }
}
