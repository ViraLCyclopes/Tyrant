using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Tests;

public class EnvironmentTexturesTests
{
    [Fact]
    public void Presets_have_unique_ids_and_cover_ground_and_sky()
    {
        Assert.Equal(EnvironmentPresets.All.Count, EnvironmentPresets.All.Select(p => p.Id).Distinct().Count());
        Assert.Contains(EnvironmentPresets.All, p => p is { Id: "lush-grass", Kind: EnvironmentKind.Ground });
        Assert.Contains(EnvironmentPresets.All, p => p is { Id: "noon", Kind: EnvironmentKind.Sky });
    }

    [Fact]
    public void An_unknown_preset_is_refused_with_the_known_ones()
    {
        var ex = Assert.Throws<ArgumentException>(() => EnvironmentPresets.Find("moon"));

        Assert.Contains("lush-grass", ex.Message);
    }

    [Fact]
    public void A_missing_game_file_is_reported_by_name()
    {
        using var game = new FakeGame(); // has no sharedassets files
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));

        var ex = Assert.Throws<TyrantException>(() =>
            new EnvironmentTextureWriter().Write(new GameInstall(game.Root, null), new AssetIndex(), EnvironmentPresets.Find("lush-grass"), dir));

        Assert.Equal(TyrantErrorCode.AssetNotFound, ex.Code);
        Assert.Contains("sharedassets3.assets", ex.Message);
    }
}
