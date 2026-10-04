using System.Text;
using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class SoundCliTests
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

    private static string Setup(FakeGame game)
    {
        var ws = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", ws, "--game", game.Root).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "new", "carch-voice", "--name", "Carch voice", "-w", ws).Code);
        SoundDumps.Write(Path.Combine(ws, "data"));
        return ws;
    }

    [Fact]
    public void Sounds_list_shows_a_species_sounds_by_group_with_who_shares_them()
    {
        using var game = new FakeGame();
        var ws = Setup(game);

        var list = Run("sounds", "list", "--species", "Carcharodontosaurus", "-w", ws);

        Assert.True(list.Code == ExitCodes.Ok, list.Err);
        Assert.Contains("Calls", list.Out);
        Assert.Contains("Social call", list.Out);
        Assert.Contains(SoundDumps.Roar, list.Out);
        Assert.Contains("also Acrocanthosaurus", list.Out);
        Assert.Contains("7 sound(s)", list.Out);
    }

    [Fact]
    public void Sounds_list_searches_every_sound()
    {
        using var game = new FakeGame();
        var ws = Setup(game);

        var list = Run("sounds", "list", "--search", "click", "-w", ws);

        Assert.True(list.Code == ExitCodes.Ok, list.Err);
        Assert.Contains(SoundDumps.Click, list.Out);
        Assert.Contains("1 sound(s)", list.Out);
    }

    [Fact]
    public void Replace_set_and_remove_sound_work_from_the_command_line()
    {
        using var game = new FakeGame();
        var ws = Setup(game);
        var wav = Path.Combine(ws, "..", "roar.wav");
        File.WriteAllBytes(wav, Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt roar"));

        var add = Run("mod", "replace-sound", "carch-voice", SoundDumps.Roar, wav, "--species", "Carcharodontosaurus", "--volume", "0.8", "-w", ws);
        var show = Run("mod", "show", "carch-voice", "-w", ws);
        var set = Run("mod", "set-sound", "carch-voice", SoundDumps.Roar, "--species", "Carcharodontosaurus", "--for-everyone", "--age-pitch", "0", "-w", ws);
        var shown = Run("mod", "show", "carch-voice", "-w", ws);
        var check = Run("mod", "check", "carch-voice", "-w", ws);
        var remove = Run("mod", "remove-sound", "carch-voice", SoundDumps.Roar, "-w", ws);
        var missing = Run("mod", "remove-sound", "carch-voice", SoundDumps.Roar, "-w", ws);

        Assert.True(add.Code == ExitCodes.Ok, add.Err);
        Assert.Contains("for Carcharodontosaurus", add.Out);
        Assert.Contains("Sounds (1)", show.Out);
        Assert.Contains("Social call", show.Out);
        Assert.Contains("volume 0.8", show.Out);
        Assert.True(set.Code == ExitCodes.Ok, set.Err);
        Assert.Contains("for everyone", shown.Out);
        Assert.Contains("age pitch 0", shown.Out);
        Assert.True(check.Code == ExitCodes.Ok, check.Out + check.Err);
        Assert.True(remove.Code == ExitCodes.Ok, remove.Err);
        Assert.NotEqual(ExitCodes.Ok, missing.Code);
    }

    [Fact]
    public void A_sound_gets_its_own_chance_and_back_to_the_games_from_the_command_line()
    {
        using var game = new FakeGame();
        var ws = Setup(game);
        var wav = Path.Combine(ws, "..", "growl.wav");
        File.WriteAllBytes(wav, Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt growl"));

        var add = Run("mod", "replace-sound", "carch-voice", SoundDumps.Roar, wav, "--chance", "0.25", "-w", ws);
        var rare = Run("mod", "show", "carch-voice", "-w", ws);
        var set = Run("mod", "set-sound", "carch-voice", SoundDumps.Roar, "--like-game", "-w", ws);
        var back = Run("mod", "show", "carch-voice", "-w", ws);

        Assert.True(add.Code == ExitCodes.Ok, add.Err);
        Assert.Contains("chance 25%", rare.Out);
        Assert.True(set.Code == ExitCodes.Ok, set.Err);
        Assert.Contains("like the game", back.Out);

        Run("mod", "set-sound", "carch-voice", SoundDumps.Roar, "--chance", "0.4", "-w", ws);
        var again = Run("mod", "replace-sound", "carch-voice", SoundDumps.Roar, wav, "--like-game", "-w", ws);
        Assert.True(again.Code == ExitCodes.Ok, again.Err);
        Assert.Contains("like the game", Run("mod", "show", "carch-voice", "-w", ws).Out);
    }

    [Fact]
    public void A_file_that_is_not_audio_is_refused()
    {
        using var game = new FakeGame();
        var ws = Setup(game);
        var text = Path.Combine(ws, "..", "notes.wav");
        File.WriteAllText(text, "hello");

        var add = Run("mod", "replace-sound", "carch-voice", SoundDumps.Roar, text, "-w", ws);

        Assert.NotEqual(ExitCodes.Ok, add.Code);
        Assert.Contains("not an audio file", add.Out + add.Err);
    }
}
