using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Tests;
using static Tyrant.Rpc.Tests.AssetFixtures;

namespace Tyrant.Rpc.Tests;

public class AssetsMethodsTests
{
    private static List<string> Names(JsonElement result) =>
        result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToList();

    private static int CountOf(JsonElement list, string name) =>
        list.EnumerateArray().Single(c => c.GetProperty("name").GetString() == name).GetProperty("count").GetInt32();

    [Fact]
    public async Task Summary_counts_assets_bundles_groups_and_types()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        var summary = await h.Call("assets.summary");

        Assert.Equal(5, summary.GetProperty("assets").GetInt32());
        Assert.Equal(4, summary.GetProperty("bundles").GetInt32());
        Assert.Equal(3, CountOf(summary.GetProperty("groups"), "animals/stego_assets_assets"));
        Assert.Equal(3, CountOf(summary.GetProperty("types"), "Texture2D"));
        Assert.False(summary.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task Summary_flags_an_index_from_an_older_build()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game, fingerprint: new GameFingerprint("an-older-build", "0"));

        Assert.True((await h.Call("assets.summary")).GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task Bundles_lists_the_bundles_of_one_group()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        var bundles = (await h.Call("assets.bundles", new { group = "animals/stego_assets_assets" })).GetProperty("bundles");

        Assert.Equal(["animals/stego_assets_assets/prefabs.bundle", "animals/stego_assets_assets/textures.bundle"],
            bundles.EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToArray());
        Assert.Equal(2, CountOf(bundles, "animals/stego_assets_assets/textures.bundle"));
    }

    [Fact]
    public async Task List_filters_by_words_type_group_and_bundle()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        Assert.Equal(["T_Stego_D"], Names(await h.Call("assets.list", new { filter = "stego_d" })));
        Assert.Equal(["T_Stego_D"], Names(await h.Call("assets.list", new { filter = "0123456789ABCDEF" })));
        Assert.Equal(["Menu"], Names(await h.Call("assets.list", new { filter = "menuscript" })));
        Assert.Equal(["T_Stego_D", "T_Stego_N"], Names(await h.Call("assets.list", new { type = "Texture2D", group = "animals/stego_assets_assets" })));
        Assert.Equal(["Stegosaurus"], Names(await h.Call("assets.list", new { bundle = "animals/stego_assets_assets/prefabs.bundle" })));
        var row = (await h.Call("assets.list", new { filter = "stego_d" })).GetProperty("rows")[0];
        Assert.Equal(StegoD.Ref, row.GetProperty("ref").GetString());
        Assert.Equal(StegoD.ContainerPath, row.GetProperty("containerPath").GetString());
    }

    [Fact]
    public async Task List_pages_and_caps_the_page_size()
    {
        using var game = new FakeGame();
        var many = Enumerable.Range(1, 1500).Select(i => new AssetRecord("big.bundle", i, "Texture2D", $"T_{i:0000}", null, null, null));
        var (h, _, _) = await Opened(game, many);

        var first = await h.Call("assets.list", new { pageSize = 5000 });
        var second = await h.Call("assets.list", new { page = 1, pageSize = 1000 });

        Assert.Equal(1000, first.GetProperty("rows").GetArrayLength());
        Assert.Equal(1000, first.GetProperty("pageSize").GetInt32());
        Assert.Equal(1500, first.GetProperty("total").GetInt32());
        Assert.Equal(500, second.GetProperty("rows").GetArrayLength());
        Assert.Equal("T_1001", Names(second)[0]);
    }

    [Fact]
    public async Task Without_an_index_asks_to_refresh()
    {
        using var game = new FakeGame();
        var h = new RpcHarness(TestStudio.Options());
        await h.Call("workspace.create", new { dir = TestStudio.TempDir(), gamePath = game.Root });

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("assets.summary"));

        Assert.Equal("ASSET_INDEX_MISSING", ex.DataCode);
        Assert.Equal("REFRESH_WORKSPACE", ex.Fix);
    }

    [Fact]
    public async Task Get_resolves_references_and_returns_the_fields()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await Opened(game);
        reader.Inspection = new AssetInspection(4096, """{"m_Name":"T_Stego_D","m_Width":2048}""",
            [new AssetReference("m_Normal", 0, 2), new AssetReference("m_Script", 1, 77), new AssetReference("ghost", 0, 999)], ["CAB-abc"]);

        var details = await h.Call("assets.get", new { @ref = StegoD.Ref });

        Assert.Equal(4096, details.GetProperty("byteSize").GetInt64());
        Assert.Equal(2048, details.GetProperty("fields").GetProperty("m_Width").GetInt32());
        var refs = details.GetProperty("references");
        Assert.Equal(StegoN.Ref, refs[0].GetProperty("ref").GetString());
        Assert.Equal("T_Stego_N", refs[0].GetProperty("name").GetString());
        Assert.Equal("CAB-abc #77", refs[1].GetProperty("external").GetString());
        Assert.Equal(JsonValueKind.Null, refs[1].GetProperty("ref").ValueKind);
        Assert.Contains("999", refs[2].GetProperty("external").GetString());
    }

    [Fact]
    public async Task Get_of_an_unknown_ref_is_not_found()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("assets.get", new { @ref = "nope.bundle#1" }));

        Assert.Equal("ASSET_NOT_FOUND", ex.DataCode);
    }

    [Fact]
    public async Task A_rebuilt_index_replaces_the_cached_one()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await Opened(game);
        Assert.Equal(5, (await h.Call("assets.list")).GetProperty("total").GetInt32());

        WriteIndex(ws, [StegoD, RexD], null);
        File.SetLastWriteTimeUtc(Path.Combine(ws, "cache", AssetIndex.FileName), DateTime.UtcNow.AddSeconds(5));

        Assert.Equal(2, (await h.Call("assets.list")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Species_list_finds_species_prefabs_with_their_textures()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        var species = Assert.Single((await h.Call("species.list")).GetProperty("species").EnumerateArray());

        Assert.Equal("stegosaurus", species.GetProperty("key").GetString());
        Assert.Equal("Stegosaurus", species.GetProperty("displayName").GetString());
        Assert.Equal(StegoPrefab.Ref, species.GetProperty("prefabRef").GetString());
        Assert.Equal(2, species.GetProperty("textures").GetInt32());
    }
}
