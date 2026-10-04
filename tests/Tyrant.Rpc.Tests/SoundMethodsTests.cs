using System.Text;
using System.Text.Json;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class SoundMethodsTests
{
    private static async Task<(RpcHarness H, string Ws)> Opened(FakeGame game, bool withDump = true)
    {
        var h = new RpcHarness(TestStudio.Options(dumperDir: TestStudio.FakeGameModsDir()));
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        await h.Call("mods.create", new { id = "carch-voice", name = "Carch voice" });
        if (withDump) SoundDumps.Write(Path.Combine(ws, "data"));
        return (h, ws);
    }

    private static string Wav(string ws, string name)
    {
        var path = Path.Combine(ws, "..", name);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt " + name));
        return path;
    }

    private static string Rev(JsonElement detail) => detail.GetProperty("revision").GetString()!;

    [Fact]
    public async Task Replacing_a_sound_shows_it_in_the_mod_detail()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);

        var detail = await h.Call("mods.replaceSound", new
        {
            id = "carch-voice", @event = SoundDumps.Roar, files = new[] { Wav(ws, "roar.wav") }, species = "Carcharodontosaurus", volume = 0.8,
        });

        var sound = Assert.Single(detail.GetProperty("sounds").EnumerateArray());
        Assert.Equal(SoundDumps.Roar, sound.GetProperty("event").GetString());
        Assert.Equal("Social call", sound.GetProperty("name").GetString());
        Assert.Equal("Calls", sound.GetProperty("group").GetString());
        Assert.Equal("Carcharodontosaurus", sound.GetProperty("species").GetString());
        Assert.Equal(JsonValueKind.Null, sound.GetProperty("skin").ValueKind);
        Assert.StartsWith("sounds/roar-", sound.GetProperty("files")[0].GetString());
        Assert.Equal(0.8, sound.GetProperty("volume").GetDouble());
        Assert.Equal(1.0, sound.GetProperty("agePitch").GetDouble());
    }

    [Fact]
    public async Task Set_and_remove_edit_with_the_revision()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        var added = await h.Call("mods.replaceSound", new { id = "carch-voice", @event = SoundDumps.Roar, files = new[] { Wav(ws, "a.wav") } });

        var set = await h.Call("mods.setSound", new { id = "carch-voice", revision = Rev(added), @event = SoundDumps.Roar, volume = 0.5, newSpecies = "Acrocanthosaurus" });
        var sound = set.GetProperty("sounds")[0];
        Assert.Equal(0.5, sound.GetProperty("volume").GetDouble());
        Assert.Equal("Acrocanthosaurus", sound.GetProperty("species").GetString());

        var stale = await Assert.ThrowsAsync<RpcCallException>(() =>
            h.Call("mods.removeSound", new { id = "carch-voice", revision = Rev(added), @event = SoundDumps.Roar, species = "Acrocanthosaurus" }));
        Assert.Equal("MOD_CHANGED", stale.DataCode);

        var removed = await h.Call("mods.removeSound", new { id = "carch-voice", revision = Rev(set), @event = SoundDumps.Roar, species = "Acrocanthosaurus" });
        Assert.Empty(removed.GetProperty("sounds").EnumerateArray());
    }

    [Fact]
    public async Task A_species_lists_its_sounds_with_who_shares_them()
    {
        using var game = new FakeGame();
        var (h, _) = await Opened(game);

        var sounds = await h.Call("sounds.forSpecies", new { species = "Carcharodontosaurus" });

        var roar = sounds.EnumerateArray().Single(s => s.GetProperty("event").GetString() == SoundDumps.Roar);
        Assert.Equal(["Acrocanthosaurus", "Carcharodontosaurus"], roar.GetProperty("species").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(2400, roar.GetProperty("lengthMs").GetInt32());
        Assert.Equal(7, sounds.GetArrayLength());
    }

    [Fact]
    public async Task Search_finds_every_listed_sound_and_says_whether_the_event_list_exists()
    {
        using var game = new FakeGame();
        var (h, _) = await Opened(game);

        var found = await h.Call("sounds.search", new { text = "click" });

        Assert.True(found.GetProperty("hasEventList").GetBoolean());
        Assert.Equal(SoundDumps.Click, Assert.Single(found.GetProperty("sounds").EnumerateArray()).GetProperty("event").GetString());
    }

    [Fact]
    public async Task Without_a_dump_the_lists_ask_for_one()
    {
        using var game = new FakeGame();
        var (h, _) = await Opened(game, withDump: false);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("sounds.forSpecies", new { species = "Carcharodontosaurus" }));

        Assert.Equal("DATA_MISSING", ex.DataCode);
        Assert.Contains("Run data dump", ex.Message);
    }

    [Fact]
    public async Task Check_warns_about_a_sound_the_game_does_not_have()
    {
        using var game = new FakeGame();
        var (h, ws) = await Opened(game);
        await h.Call("mods.replaceSound", new { id = "carch-voice", @event = "event:/Nope/Not_Real", files = new[] { Wav(ws, "a.wav") } });

        var check = await h.Call("mods.check", new { id = "carch-voice" });

        Assert.Contains(check.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("not one of the game's sounds"));
    }
}
