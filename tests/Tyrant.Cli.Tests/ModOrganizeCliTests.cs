using System.Text.Json;
using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class ModOrganizeCliTests
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

    /// <summary>A workspace with mod red-spot holding three ungrouped skins: blue, green, tan.</summary>
    private static string Workspace(FakeGame game)
    {
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", ws, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "red-spot", "--name", "Red spot", "-w", ws).Code);
        var skins = string.Join(",", new[] { "blue", "green", "tan" }.Select(id =>
            $"{{\"id\":\"{id}\",\"species\":\"Carcharodontosaurus\",\"name\":\"{id}\",\"base\":\"0\",\"male\":{{\"diffuse\":\"skins/{id}/male_D.png\"}}}}"));
        File.WriteAllText(Path.Combine(ws, "mods", "red-spot", "mod.json"),
            "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[" + skins + "]}");
        return ws;
    }

    private static (string?, string?)[] Skins(string ws) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(ws, "mods", "red-spot", "mod.json"))).RootElement.GetProperty("skins").EnumerateArray()
            .Select(s => (s.GetProperty("id").GetString(), s.TryGetProperty("group", out var g) ? g.GetString() : null)).ToArray();

    [Fact]
    public void Group_puts_skins_in_a_group_and_none_takes_them_out()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);

        Assert.Equal(ExitCodes.Ok, Run("mod", "group", "red-spot", "blue", "tan", "--group", "Jurassic Park", "-w", ws).Code);
        Assert.Equal(new (string?, string?)[] { ("green", null), ("blue", "Jurassic Park"), ("tan", "Jurassic Park") }, Skins(ws));
        Assert.Contains("group: Jurassic Park", Run("mod", "show", "red-spot", "-w", ws).Out);

        Assert.Equal(ExitCodes.Ok, Run("mod", "group", "red-spot", "tan", "--none", "-w", ws).Code);
        Assert.Equal(new (string?, string?)[] { ("green", null), ("tan", null), ("blue", "Jurassic Park") }, Skins(ws));

        Assert.NotEqual(ExitCodes.Ok, Run("mod", "group", "red-spot", "blue", "-w", ws).Code); // neither --group nor --none
        Assert.NotEqual(ExitCodes.Ok, Run("mod", "group", "red-spot", "nope", "--group", "X", "-w", ws).Code);
        Assert.Equal(new (string?, string?)[] { ("green", null), ("tan", null), ("blue", "Jurassic Park") }, Skins(ws)); // a refused command changes nothing
    }

    [Fact]
    public void Move_skin_moves_within_its_group()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);

        Assert.Equal(ExitCodes.Ok, Run("mod", "move-skin", "red-spot", "tan", "--up", "-w", ws).Code);
        Assert.Equal(new (string?, string?)[] { ("blue", null), ("tan", null), ("green", null) }, Skins(ws));
        Assert.Equal(ExitCodes.Ok, Run("mod", "move-skin", "red-spot", "blue", "--to", "3", "-w", ws).Code);
        Assert.Equal(new (string?, string?)[] { ("tan", null), ("green", null), ("blue", null) }, Skins(ws));
        Assert.NotEqual(ExitCodes.Ok, Run("mod", "move-skin", "red-spot", "blue", "--up", "--down", "-w", ws).Code);
    }

    [Fact]
    public void Delete_without_yes_only_says_what_would_go_and_with_yes_recycles_it()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        var dir = Path.Combine(ws, "mods", "red-spot");

        var (code, output, _) = Run("mod", "delete", "red-spot", "-w", ws);
        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("--yes", output);
        Assert.True(Directory.Exists(dir));

        var deleted = Run("mod", "delete", "red-spot", "--yes", "-w", ws);
        Assert.Equal(ExitCodes.Ok, deleted.Code);
        Assert.False(Directory.Exists(dir));
        Assert.True(TestRecycleBin.Instance.Holds(dir));
        Assert.Contains("clean-skins", deleted.Out);
        Assert.NotEqual(ExitCodes.Ok, Run("mod", "delete", "red-spot", "--yes", "-w", ws).Code); // gone now
    }
}
