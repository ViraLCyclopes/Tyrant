using PK.Core.Assets;
using PK.Core.Errors;
using PK.Core.Install;
using PK.Core.Workspaces;

namespace PK.Core.Tests;

public class AssetIndexerTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"));

    private static string AaDir(FakeGame game) => AssetSession.AaDirOf(new GameInstall(game.Root, null));

    [Fact]
    public void Install_without_addressables_gives_empty_index()
    {
        using var game = new FakeGame();
        var index = new AssetIndexer().Build(new GameInstall(game.Root, null), null, CancellationToken.None);

        Assert.Empty(index.Assets);
        Assert.Empty(index.Failures);
        Assert.Empty(index.MissingBundles);
        Assert.NotNull(index.Fingerprint);
    }

    [Fact]
    public void Unreadable_bundle_is_recorded_and_missing_catalog_bundles_are_listed()
    {
        using var game = new FakeGame();
        var platform = Path.Combine(AaDir(game), "StandaloneWindows64");
        Directory.CreateDirectory(platform);
        File.WriteAllBytes(Path.Combine(platform, "broken.bundle"), [1, 2, 3, 4, 5]);
        File.WriteAllText(Path.Combine(AaDir(game), "catalog.json"), CatalogFixture.Build(
            CatalogFixture.Bundle("StandaloneWindows64/broken.bundle"),
            CatalogFixture.Bundle("StandaloneWindows64/DLC/Deluxe/giraffa.bundle")));

        var index = new AssetIndexer().Build(new GameInstall(game.Root, null), null, CancellationToken.None);

        var failure = Assert.Single(index.Failures);
        Assert.Equal("StandaloneWindows64/broken.bundle", failure.Bundle);
        Assert.False(string.IsNullOrWhiteSpace(failure.Error));
        Assert.Equal(new[] { "StandaloneWindows64/DLC/Deluxe/giraffa.bundle" }, index.MissingBundles);
    }

    [Fact]
    public void BuildAndSave_writes_index_and_stamps_output()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(TempDir(), install);

        new AssetIndexer().BuildAndSave(install, ws, null, CancellationToken.None);

        Assert.True(File.Exists(AssetIndex.PathIn(ws)));
        Assert.Contains(AssetIndex.OutputName, Workspace.Open(ws.Dir).Data.Outputs.Keys);
    }

    [Fact]
    public void Session_refuses_bundle_paths_outside_addressables_folder()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));

        var ex = Assert.Throws<PkException>(() => session.BundlePath("../../../Prehistoric Kingdom.exe"));
        Assert.Equal(PkErrorCode.AssetNotFound, ex.Code);
    }

    [Fact]
    public void Session_reports_bundle_deleted_since_indexing()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));
        var record = new AssetRecord("StandaloneWindows64/gone.bundle", 1, "Texture2D", "T", null, null, null);

        var ex = Assert.Throws<PkException>(() => session.Open(record));
        Assert.Equal(PkErrorCode.AssetNotFound, ex.Code);
        Assert.Contains("pk assets index", ex.Message);
    }

    [Fact]
    public void Cancelled_token_stops_indexing()
    {
        using var game = new FakeGame();
        var platform = Path.Combine(AaDir(game), "StandaloneWindows64");
        Directory.CreateDirectory(platform);
        File.WriteAllBytes(Path.Combine(platform, "a.bundle"), [1]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new AssetIndexer().Build(new GameInstall(game.Root, null), null, cts.Token));
    }
}
