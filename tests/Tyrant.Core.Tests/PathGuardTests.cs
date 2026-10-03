using System.Diagnostics;
using Tyrant.Core.Install;

namespace Tyrant.Core.Tests;

public class PathGuardTests
{
    [Theory]
    [InlineData(@"E:\ws", @"E:\", true)] // a game at a drive root
    [InlineData(@"E:\", @"E:\", true)]
    [InlineData(@"F:\ws", @"E:\", false)]
    [InlineData(@"C:\Game\ws", @"C:\Game", true)]
    [InlineData(@"C:\Game", @"C:\Game\", true)]
    [InlineData(@"C:\Game2\ws", @"C:\Game", false)] // a sibling with the same prefix
    [InlineData(@"\\?\C:\Game\ws", @"C:\Game", true)] // extended-length prefix
    [InlineData(@"c:\game\WS", @"C:\Game", true)]
    public void Paths_inside_the_game_folder_are_found(string path, string gameRoot, bool inside)
    {
        Assert.Equal(inside, PathGuard.IsInside(path, gameRoot));
    }

    [Fact]
    public void A_junction_into_the_game_folder_counts_as_inside()
    {
        var root = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(game);
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{game}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            mklink.WaitForExit();
        Assert.True(Directory.Exists(link), "the junction could not be made");

        Assert.True(PathGuard.IsInside(Path.Combine(link, "ws"), game));
        Assert.True(new GameInstall(game, null).ContainsPath(Path.Combine(link, "ws")));
    }
}
