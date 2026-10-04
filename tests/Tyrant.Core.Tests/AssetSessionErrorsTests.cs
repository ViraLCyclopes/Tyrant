using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Tests;

public class AssetSessionErrorsTests
{
    [Fact]
    public void A_vanished_bundle_offers_to_index_the_assets_again()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));

        var ex = Assert.Throws<TyrantException>(() => session.Open(new AssetRecord("gone/x.bundle", 1, "Texture2D", "T", null, null, null)));

        Assert.Equal(FixAction.ReindexAssets, ex.Fix);
        Assert.Equal("REINDEX_ASSETS", ex.Fix.ToWire());
    }

    [Fact]
    public void A_vanished_built_in_file_offers_to_index_the_assets_again()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));

        var ex = Assert.Throws<TyrantException>(() => session.Open(new AssetRecord("@data/sharedassets9.assets", 1, "Texture2D", "T", null, null, null)));

        Assert.Equal(FixAction.ReindexAssets, ex.Fix);
    }
}
