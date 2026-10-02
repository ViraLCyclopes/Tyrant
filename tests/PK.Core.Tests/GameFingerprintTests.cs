using PK.Core.Install;

namespace PK.Core.Tests;

public class GameFingerprintTests
{
    [Fact]
    public void Reads_build_guid_and_hashes_assembly()
    {
        using var game = new FakeGame(buildGuid: "ce7261bf0d004076b451bcb982c67154");
        var fp = GameFingerprint.Compute(new GameInstall(game.Root, null));

        Assert.Equal("ce7261bf0d004076b451bcb982c67154", fp.BuildGuid);
        Assert.Matches("^[0-9a-f]{64}$", fp.AssemblySha256);
    }

    [Fact]
    public void Missing_boot_config_gives_unknown_build_guid()
    {
        using var game = new FakeGame(buildGuid: null);
        Assert.Equal("unknown", GameFingerprint.Compute(new GameInstall(game.Root, null)).BuildGuid);
    }

    [Fact]
    public void Fingerprints_compare_by_value()
    {
        Assert.Equal(new GameFingerprint("a", "b"), new GameFingerprint("a", "b"));
        Assert.NotEqual(new GameFingerprint("a", "b"), new GameFingerprint("a", "c"));
    }
}
