namespace Tyrant.Core.Tests;

public class TestTempTests
{
    [Fact]
    public void Folders_older_than_a_day_are_removed_and_recent_ones_kept()
    {
        var root = Path.Combine(Path.GetTempPath(), "tyrant-tests-cleanup", Guid.NewGuid().ToString("N"));
        var old = Directory.CreateDirectory(Path.Combine(root, "old")).FullName;
        File.WriteAllText(Path.Combine(old, "x.txt"), "x");
        var recent = Directory.CreateDirectory(Path.Combine(root, "recent")).FullName;
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));

        var removed = TestTemp.CleanOld(root, DateTime.UtcNow);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
        Directory.Delete(root, recursive: true);
    }
}
