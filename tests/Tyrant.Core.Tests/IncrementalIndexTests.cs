using Tyrant.Core.Assets;
using Tyrant.Core.Install;

namespace Tyrant.Core.Tests;

public class IncrementalIndexTests
{
    private static (FakeGame Game, GameInstall Install, string Data) Setup(params string[] files)
    {
        var game = new FakeGame();
        var data = Path.Combine(game.Root, GameInstall.DataDirName);
        Directory.CreateDirectory(data);
        foreach (var name in files) File.WriteAllBytes(Path.Combine(data, name), [1]);
        return (game, new GameInstall(game.Root, null), data);
    }

    private static AssetIndexer Counting(List<string> scanned) =>
        new((_, source, _) =>
        {
            scanned.Add(source.Key);
            return [new AssetRecord(source.Key, 1, "Texture2D", Path.GetFileName(source.FullPath) + "_tex", null, null, null)];
        });

    [Fact]
    public void Unchanged_sources_are_not_scanned_again()
    {
        var (game, install, _) = Setup("sharedassets0.assets", "sharedassets1.assets");
        using var _ = game;
        var scanned = new List<string>();
        var first = Counting(scanned).Build(install, null, CancellationToken.None);
        scanned.Clear();

        var second = Counting(scanned).Build(install, null, CancellationToken.None, first);

        Assert.Empty(scanned);
        Assert.Equal(first.Assets.Select(a => a.Ref), second.Assets.Select(a => a.Ref));
        Assert.Equal(2, second.Sources.Count);
    }

    [Fact]
    public void A_changed_source_is_scanned_again()
    {
        var (game, install, data) = Setup("sharedassets0.assets", "sharedassets1.assets");
        using var _ = game;
        var scanned = new List<string>();
        var first = Counting(scanned).Build(install, null, CancellationToken.None);
        File.WriteAllBytes(Path.Combine(data, "sharedassets1.assets"), [1, 2, 3]);
        scanned.Clear();

        Counting(scanned).Build(install, null, CancellationToken.None, first);

        Assert.Equal(["@data/sharedassets1.assets"], scanned);
    }

    [Fact]
    public void A_source_that_disappeared_loses_its_records()
    {
        var (game, install, data) = Setup("sharedassets0.assets", "sharedassets1.assets");
        using var _ = game;
        var first = Counting([]).Build(install, null, CancellationToken.None);
        File.Delete(Path.Combine(data, "sharedassets1.assets"));

        var second = Counting([]).Build(install, null, CancellationToken.None, first);

        Assert.DoesNotContain(second.Assets, a => a.Bundle == "@data/sharedassets1.assets");
        Assert.DoesNotContain(second.Sources, s => s.Key == "@data/sharedassets1.assets");
    }

    [Fact]
    public void An_index_without_sources_is_rebuilt_in_full()
    {
        var (game, install, _) = Setup("sharedassets0.assets");
        using var _ = game;
        var old = new AssetIndex { SchemaVersion = 2, Assets = [new AssetRecord("StandaloneWindows64/x.bundle", 1, "Texture2D", "old", null, null, null)] };
        var scanned = new List<string>();

        var rebuilt = Counting(scanned).Build(install, null, CancellationToken.None, old);

        Assert.Equal(["@data/sharedassets0.assets"], scanned);
        Assert.Equal(AssetIndex.CurrentSchemaVersion, rebuilt.SchemaVersion);
        Assert.True(old.IsOutdatedFormat);
    }

    [Fact]
    public void A_source_that_fails_is_reported_and_scanned_again_next_time()
    {
        var (game, install, _) = Setup("sharedassets0.assets");
        using var _ = game;
        var failing = new AssetIndexer((_, _, _) => throw new InvalidOperationException("bad file"));
        var first = failing.Build(install, null, CancellationToken.None);
        var scanned = new List<string>();

        Counting(scanned).Build(install, null, CancellationToken.None, first);

        Assert.Single(first.Failures);
        Assert.Equal(["@data/sharedassets0.assets"], scanned); // a failed source is never treated as up to date
    }

    [Fact]
    public void Reused_sources_keep_their_archive_names()
    {
        var (game, install, _) = Setup("sharedassets0.assets");
        using var _ = game;
        var indexer = new AssetIndexer((_, source, archives) =>
        {
            archives.TryAdd("sharedassets0.assets", source.Key);
            return [new AssetRecord(source.Key, 1, "Texture2D", "t", null, null, null)];
        });
        var first = indexer.Build(install, null, CancellationToken.None);

        var second = Counting([]).Build(install, null, CancellationToken.None, first);

        Assert.Equal("@data/sharedassets0.assets", second.Archives["sharedassets0.assets"]);
    }
}
