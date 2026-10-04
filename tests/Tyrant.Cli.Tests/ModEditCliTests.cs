using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class ModEditCliTests
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

    private static string WorkspaceWithSkin(FakeGame game)
    {
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", ws, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "red-spot", "--name", "Red spot", "-w", ws).Code);
        var dir = Path.Combine(ws, "mods", "red-spot");
        Png(Path.Combine(dir, "skins", "blue", "male_D.png"));
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[{\"texture\":\"T_A_D\",\"file\":\"textures/a.png\"}],\"skins\":[{\"id\":\"blue\",\"species\":\"Carcharodontosaurus\",\"name\":\"Blue\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/blue/male_D.png\"}}]}");
        return ws;
    }

    private static string Png(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }

    private static string ModJson(string ws) => File.ReadAllText(Path.Combine(ws, "mods", "red-spot", "mod.json"));

    private static string Field(string ws, string name) =>
        System.Text.Json.JsonDocument.Parse(ModJson(ws)).RootElement.TryGetProperty(name, out var v) ? v.ToString() : "";

    [Fact]
    public void Show_lists_the_details_replacements_and_skins()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);

        var (code, output, _) = Run("mod", "show", "red-spot", "-w", ws);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Red spot", output);
        Assert.Contains("T_A_D", output);
        Assert.Contains("blue", output);
        Assert.Contains("Carcharodontosaurus", output);
    }

    [Fact]
    public void Set_changes_only_the_given_details()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);

        Assert.Equal(ExitCodes.Ok, Run("mod", "set", "red-spot", "--version", "1.2.0", "--author", "Shadow", "-w", ws).Code);

        Assert.Equal(("1.2.0", "Shadow", "Red spot"), (Field(ws, "version"), Field(ws, "author"), Field(ws, "name")));
    }

    [Fact]
    public void Rename_and_remove_a_skin()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);

        Assert.Equal(ExitCodes.Ok, Run("mod", "rename-skin", "red-spot", "blue", "Ocean blue", "-w", ws).Code);
        Assert.Contains("Ocean blue", ModJson(ws));
        Assert.Equal(ExitCodes.Ok, Run("mod", "remove-skin", "red-spot", "blue", "--delete-files", "-w", ws).Code);
        Assert.DoesNotContain("Ocean blue", ModJson(ws));
        Assert.False(File.Exists(Path.Combine(ws, "mods", "red-spot", "skins", "blue", "male_D.png")));
    }

    [Fact]
    public void Colors_show_set_and_clear()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        var file = Path.Combine(ws, "colors.json");
        File.WriteAllText(file, "{\"pattern\":{\"a\":\"#3060ff\"}}");

        Assert.Equal(ExitCodes.Ok, Run("mod", "colors", "red-spot", "blue", "--set", file, "-w", ws).Code);
        Assert.Contains("#3060ff", Run("mod", "colors", "red-spot", "blue", "--show", "-w", ws).Out);
        Assert.Equal(ExitCodes.Ok, Run("mod", "colors", "red-spot", "blue", "--clear", "-w", ws).Code);
        Assert.DoesNotContain("colors", ModJson(ws));
        Assert.Equal(ExitCodes.Error, Run("mod", "colors", "red-spot", "blue", "-w", ws).Code); // none of --show/--set/--clear
    }

    [Fact]
    public void Skin_file_thumbnail_and_unreplace()
    {
        using var game = new FakeGame();
        var ws = WorkspaceWithSkin(game);
        var art = Png(Path.Combine(ws, "art", "extra.png"));

        Assert.Equal(ExitCodes.Ok, Run("mod", "skin-file", "red-spot", "blue", "male", "extra", art, "-w", ws).Code);
        Assert.Contains("skins/blue/male_extra.png", ModJson(ws));
        Assert.Equal(ExitCodes.Ok, Run("mod", "skin-file", "red-spot", "blue", "male", "extra", "--base", "-w", ws).Code);
        Assert.DoesNotContain("male_extra", ModJson(ws));
        Assert.Equal(ExitCodes.Error, Run("mod", "skin-file", "red-spot", "blue", "male", "diffuse", "--base", "-w", ws).Code); // last file
        Assert.Equal(ExitCodes.Ok, Run("mod", "thumbnail", "red-spot", "blue", art, "-w", ws).Code);
        Assert.Contains("thumbnail.png", ModJson(ws));
        Assert.Equal(ExitCodes.Ok, Run("mod", "thumbnail", "red-spot", "blue", "--auto", "-w", ws).Code);
        Assert.DoesNotContain("thumbnail", ModJson(ws));
        Assert.Equal(ExitCodes.Ok, Run("mod", "unreplace", "red-spot", "T_A_D", "-w", ws).Code);
        Assert.DoesNotContain("T_A_D", ModJson(ws));
    }
}
