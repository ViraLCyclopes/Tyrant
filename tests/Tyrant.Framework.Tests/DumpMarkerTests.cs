using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class DumpMarkerTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void A_marker_counts_until_it_expires()
    {
        var dir = TempDir();
        DumpMarker.Write(dir, Now.AddMinutes(5));

        Assert.True(DumpMarker.IsActive(dir, Now));
        Assert.True(DumpMarker.IsActive(dir, Now.AddMinutes(5)));
        Assert.False(DumpMarker.IsActive(dir, Now.AddMinutes(5).AddSeconds(1))); // left by a crash: the next normal game start has its mods
    }

    [Fact]
    public void Without_a_marker_mods_load()
    {
        Assert.False(DumpMarker.IsActive(TempDir(), Now));
        Assert.False(DumpMarker.IsActive(Path.Combine(TempDir(), "missing"), Now));
    }

    [Fact]
    public void An_unreadable_marker_does_not_cost_the_player_their_mods()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, DumpMarker.FileName), "not a date");
        Assert.False(DumpMarker.IsActive(dir, Now));
    }

    [Fact]
    public void Removing_a_marker_ends_it_and_a_missing_one_is_fine()
    {
        var dir = TempDir();
        DumpMarker.Write(dir, Now.AddMinutes(5));
        DumpMarker.Remove(dir);
        DumpMarker.Remove(dir);
        Assert.False(DumpMarker.IsActive(dir, Now));
    }
}
