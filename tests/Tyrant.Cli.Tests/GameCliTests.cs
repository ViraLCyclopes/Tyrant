using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class GameCliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    private static string Workspace(FakeGame game)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", dir, "--game", game.Root).Code);
        return dir;
    }

    [Fact]
    public void Game_status_names_both_framework_versions()
    {
        using var game = new FakeGame();

        var (code, output, _) = Run("game", "status", "-w", Workspace(game));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("not installed", output);
        Assert.Contains(Tyrant.Framework.Core.FrameworkInfo.Version, output);
    }

    [Fact]
    public void The_game_branch_offers_install_update_and_uninstall()
    {
        var (code, output, _) = Run("game", "--help");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("install", output);
        Assert.Contains("update", output);
        Assert.Contains("uninstall", output);
    }
}
