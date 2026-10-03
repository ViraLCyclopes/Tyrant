using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class ModCliTests
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

    private static string InitWorkspace(FakeGame game)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", dir, "--game", game.Root).Code);
        return dir;
    }

    [Fact]
    public void New_then_list_shows_the_mod_as_not_installed()
    {
        using var game = new FakeGame();
        var ws = InitWorkspace(game);

        var created = Run("mod", "new", "red-spot", "--name", "Red spot", "-w", ws);
        var (code, output, _) = Run("mod", "list", "-w", ws);

        Assert.Equal(ExitCodes.Ok, created.Code);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("red-spot", output);
        Assert.Contains("not installed", output);
    }

    [Fact]
    public void An_invalid_id_is_an_error_with_the_reason()
    {
        using var game = new FakeGame();
        var ws = InitWorkspace(game);

        var (code, output, error) = Run("mod", "new", "Red Spot", "-w", ws);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("not a valid mod id", output + error);
    }

    [Fact]
    public void Check_reports_a_mod_that_does_nothing_and_succeeds()
    {
        using var game = new FakeGame();
        var ws = InitWorkspace(game);
        Run("mod", "new", "red-spot", "-w", ws);

        var (code, output, _) = Run("mod", "check", "red-spot", "-w", ws);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("does nothing", output);
    }
}
