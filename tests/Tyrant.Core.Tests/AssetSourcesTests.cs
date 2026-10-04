using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Tests;

public class AssetSourcesTests
{
    [Fact]
    public void Bundles_and_the_built_in_files_are_sources()
    {
        using var game = new FakeGame();
        var data = Path.Combine(game.Root, GameInstall.DataDirName);
        Directory.CreateDirectory(data);
        foreach (var name in new[] { "sharedassets0.assets", "sharedassets12.assets", "resources.assets", "level0", "level10", "globalgamemanagers", "globalgamemanagers.assets", "sharedassets0.assets.resS", "level0.resS" })
            File.WriteAllBytes(Path.Combine(data, name), [0]);
        var aa = Path.Combine(data, "StreamingAssets", "aa", "StandaloneWindows64");
        Directory.CreateDirectory(Path.Combine(aa, "carch_assets_assets"));
        File.WriteAllBytes(Path.Combine(aa, "carch_assets_assets", "a.bundle"), [0]);

        var sources = AssetSources.Discover(new GameInstall(game.Root, null));

        Assert.Equal(
            ["StandaloneWindows64/carch_assets_assets/a.bundle", "@data/level0", "@data/level10", "@data/resources.assets", "@data/sharedassets0.assets", "@data/sharedassets12.assets"],
            sources.Select(s => s.Key));
        Assert.All(sources, s => Assert.Equal(s.Key.StartsWith("@data/"), s.BuiltIn));
    }

    [Fact]
    public void Built_in_records_say_so_and_name_their_file()
    {
        var record = new AssetRecord("@data/sharedassets0.assets", 7, "Texture2D", "Fence_D", null, null, null);
        var bundle = new AssetRecord("StandaloneWindows64/x.bundle", 7, "Texture2D", "Fence_D", null, null, null);

        Assert.True(record.IsBuiltIn);
        Assert.Equal("sharedassets0.assets", record.DataFile);
        Assert.False(bundle.IsBuiltIn);
        Assert.Null(bundle.DataFile);
    }

    [Fact]
    public void A_built_in_path_cannot_leave_the_data_folder()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));

        Assert.Equal(TyrantErrorCode.AssetNotFound, Assert.Throws<TyrantException>(() => session.DataPath("../../evil.assets")).Code);
        Assert.EndsWith(Path.Combine(GameInstall.DataDirName, "sharedassets0.assets"), session.DataPath("sharedassets0.assets"));
    }

    [Fact]
    public void A_vanished_built_in_file_says_to_index_again()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));

        var ex = Assert.Throws<TyrantException>(() => session.Open(new AssetRecord("@data/sharedassets99.assets", 1, "Texture2D", "x", null, null, null)));

        Assert.Equal(TyrantErrorCode.AssetNotFound, ex.Code);
        Assert.Contains("Index the assets again", ex.Message);
    }
}
