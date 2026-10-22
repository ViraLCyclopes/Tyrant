using Tyrant.Core.Assets;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class TemplatePicturesRpcTests
{
    private static async Task<(RpcHarness Harness, string Workspace, FakeAssetReader Reader)> Opened(FakeGame game, bool dump = true)
    {
        var (h, ws, reader) = await AssetFixtures.Opened(game);
        if (dump) PictureDumps.Write(Path.Combine(ws, "data"));
        PictureDumps.Index().Save(Path.Combine(ws, "cache", AssetIndex.FileName));
        PictureDumps.Reader(reader);
        return (h, ws, reader);
    }

    [Fact]
    public async Task A_templates_paleopedia_picture_is_saved_where_the_user_chose()
    {
        using var game = new FakeGame();
        var (h, ws, reader) = await Opened(game);
        var png = Path.Combine(Path.GetDirectoryName(ws)!, "pictures", "Parasaurolophus Cyrtocristatus paleopedia picture.png");

        var saved = await h.Call("species.saveTemplatePicture", new { species = "Parasaurolophus Cyrtocristatus", kind = "paleopedia", path = png });

        Assert.Equal("Parasaurolophus Cyrtocristatus", saved.GetProperty("species").GetString());
        Assert.Equal("paleopedia", saved.GetProperty("kind").GetString());
        Assert.Equal("Parasarulophus Cyrtocristatus", saved.GetProperty("texture").GetString());
        Assert.Equal(2048, saved.GetProperty("width").GetInt32());
        Assert.Equal(1152, saved.GetProperty("height").GetInt32());
        Assert.Equal(png, saved.GetProperty("path").GetString());
        Assert.True(File.Exists(png));
        Assert.Equal([PictureDumps.ParaPicture.Ref], reader.TexturesWritten);
    }

    [Fact]
    public async Task A_templates_nursery_picture_is_saved()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await Opened(game);
        var png = Path.Combine(Path.GetDirectoryName(ws)!, "pictures", "allo.png");

        var saved = await h.Call("species.saveTemplatePicture", new { species = "Allosaurus Anax", kind = "nursery", path = png });

        Assert.Equal("nursery", saved.GetProperty("kind").GetString());
        Assert.Equal(512, saved.GetProperty("width").GetInt32());
        Assert.Equal(256, saved.GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task Without_the_dump_the_error_says_to_run_it()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await Opened(game, dump: false);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() =>
            h.Call("species.saveTemplatePicture", new { species = "Allosaurus Anax", kind = "nursery", path = Path.Combine(Path.GetDirectoryName(ws)!, "a.png") }));

        Assert.Equal("DATA_MISSING", ex.DataCode);
    }
}
