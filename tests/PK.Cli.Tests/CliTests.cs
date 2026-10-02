using PK.Cli;
using PK.Core.Tests;

[assembly: CollectionBehavior(DisableTestParallelization = true)] // tests redirect Console

namespace PK.Cli.Tests;

public class CliTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"), "ws");

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    [Fact]
    public void Install_detect_with_game_path_prints_fingerprint()
    {
        using var game = new FakeGame(buildGuid: "guid-cli");
        var (code, output, _) = Run("install", "detect", "--game", game.Root);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("guid-cli", output);
        Assert.Contains(game.Root, output);
    }

    [Fact]
    public void Install_detect_with_bad_path_returns_pk_error()
    {
        var (code, _, err) = Run("install", "detect", "--game", Path.GetTempPath());
        Assert.Equal(ExitCodes.PkError, code);
        Assert.Contains("GAME_NOT_FOUND", err);
    }

    [Fact]
    public void Workspace_init_then_status()
    {
        using var game = new FakeGame();
        var dir = TempDir();

        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", dir, "--game", game.Root).Code);
        Assert.True(File.Exists(Path.Combine(dir, "pkws.json")));

        var (code, output, _) = Run("workspace", "status", "-w", dir);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains(game.Root, output);
    }

    [Fact]
    public void Status_on_corrupt_workspace_reports_workspace_invalid()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pkws.json"), "garbage");

        var (code, _, err) = Run("workspace", "status", "-w", dir);

        Assert.Equal(ExitCodes.PkError, code);
        Assert.Contains("WORKSPACE_INVALID", err);
        Assert.DoesNotContain("   at ", err); // no stack trace
    }

    [Fact]
    public void Decompile_selected_assembly_succeeds()
    {
        using var game = new FakeGame();
        var dir = TempDir();
        Run("workspace", "init", dir, "--game", game.Root);

        var (code, output, _) = Run("decompile", "-w", dir, "--assemblies", "Assembly-CSharp");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("ok", output);
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(dir, "source", "Assembly-CSharp"), "*.csproj"));
    }

    [Fact]
    public void Decompile_default_set_on_fake_game_is_partial()
    {
        using var game = new FakeGame(); // only Assembly-CSharp exists
        var dir = TempDir();
        Run("workspace", "init", dir, "--game", game.Root);

        var (code, output, _) = Run("decompile", "-w", dir);

        Assert.Equal(ExitCodes.Partial, code);
        Assert.Contains("FAIL  PKEnums", output);
    }

    [Fact]
    public void Unknown_command_is_usage_error()
    {
        Assert.Equal(ExitCodes.Usage, Run("frobnicate").Code);
    }

    [Fact]
    public void Workspace_init_on_a_file_path_is_a_pk_error_not_a_crash()
    {
        using var game = new FakeGame();
        var file = TempDir();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");

        var (code, _, err) = Run("workspace", "init", file, "--game", game.Root);

        Assert.Equal(ExitCodes.PkError, code);
        Assert.Contains("WORKSPACE_INVALID", err);
        Assert.DoesNotContain("   at ", err);
    }

    [Fact]
    public void Status_warning_clears_after_refresh()
    {
        using var game = new FakeGame(buildGuid: "build-1");
        var dir = TempDir();
        Run("workspace", "init", dir, "--game", game.Root);
        Run("decompile", "-w", dir, "--assemblies", "Assembly-CSharp");
        File.WriteAllText(Path.Combine(game.Root, "Prehistoric Kingdom_Data", "boot.config"), "build-guid=build-2\n");
        Assert.Contains("WARNING", Run("workspace", "status", "-w", dir).Out);

        Run("decompile", "-w", dir, "--assemblies", "Assembly-CSharp");
        var (_, output, _) = Run("workspace", "status", "-w", dir);

        Assert.DoesNotContain("WARNING", output);
        Assert.DoesNotContain("(stale)", output);
    }

    [Fact]
    public void Workspace_commands_accept_game_override_for_moved_install()
    {
        using var oldGame = new FakeGame();
        using var newGame = new FakeGame();
        var dir = TempDir();
        Run("workspace", "init", dir, "--game", oldGame.Root);

        var (code, output, _) = Run("workspace", "status", "-w", dir, "--game", newGame.Root);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains(newGame.Root, output);
        Assert.Contains(newGame.Root.Replace(@"\", @"\\"), File.ReadAllText(Path.Combine(dir, "pkws.json")));
    }
}
