using Tyrant.Cli;
using Tyrant.Core.Blender;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class BlenderCliTests
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
    public void The_blender_branch_lists_its_commands()
    {
        var (code, output, _) = Run("blender", "--help");
        Assert.Equal(ExitCodes.Ok, code);
        foreach (var c in new[] { "status", "set-path", "install-addon", "open", "send", "destinations" }) Assert.Contains(c, output);
    }

    [Fact]
    public void Open_offers_the_sex_to_show_first()
    {
        var (code, output, _) = Run("blender", "open", "--help");
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("--sex", output);
    }

    [Fact]
    public void Status_without_blender_says_how_to_choose_it()
    {
        using var game = new FakeGame();
        var (code, output, _) = Run("blender", "status", "-w", Workspace(game));
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("not found", output);
        Assert.Contains("tyrant blender set-path", output);
        Assert.Contains("Workspace tab", output);
    }

    [Fact]
    public void Open_without_blender_fails_with_the_way_to_fix_it()
    {
        using var game = new FakeGame();
        var (code, _, err) = Run("blender", "open", "-w", Workspace(game), "--species", "Carcharodontosaurus");
        Assert.Equal(ExitCodes.Error, code);
        Assert.Contains("BLENDER_MISSING", err);
    }

    [Fact]
    public void Send_prints_one_json_line_and_fails_without_a_destination()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        var dir = Path.Combine(ws, "blender", "game", "x");
        Directory.CreateDirectory(dir);
        var project = Path.Combine(dir, BlenderProjectFile.FileName);
        BlenderProjectFile.Write(project, new BlenderProject(1, ws, "t.exe", "b", new BlenderSource("game", "Carcharodontosaurus", null, null),
            null, new Dictionary<string, BlenderMaterial>(), null, [], null, false));

        var (code, output, _) = Run("blender", "send", "-w", ws, project, Path.Combine(dir, "send.glb"));

        Assert.Equal(ExitCodes.Error, code);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var doc = System.Text.Json.JsonDocument.Parse(line);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Destinations_print_the_workspace_mods_as_json()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "my-mod", "-w", ws).Code);
        var dir = Path.Combine(ws, "blender", "game", "x");
        Directory.CreateDirectory(dir);
        var project = Path.Combine(dir, BlenderProjectFile.FileName);
        BlenderProjectFile.Write(project, new BlenderProject(1, ws, "t.exe", "b", new BlenderSource("game", "Carcharodontosaurus", null, null),
            null, new Dictionary<string, BlenderMaterial>(), null, [], null, false));

        var (code, output, _) = Run("blender", "destinations", "-w", ws, project);

        Assert.Equal(ExitCodes.Ok, code);
        using var doc = System.Text.Json.JsonDocument.Parse(output.Trim());
        Assert.Equal("Carcharodontosaurus", doc.RootElement.GetProperty("species").GetString());
        Assert.Equal("my-mod", doc.RootElement.GetProperty("mods")[0].GetProperty("id").GetString());
    }
}
