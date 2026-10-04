using System.Text;
using System.Text.Json;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Dumper.Tests;

public class AudioEventListTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory("tyrant-audio-").FullName;

    [Fact]
    public void The_events_are_written_sorted_by_path_with_their_details()
    {
        var dir = TempDir();
        AudioEventList.Write(dir, [
            new AudioEvent("event:/User Interface/Click", "{b}", 120, true, "bank:/UI"),
            new AudioEvent("event:/Creatures/Sh_TheropodLargeAudio/Vox/Roar", "{a}", 1300, true, "bank:/Shared_Creatures"),
        ]);

        var file = Path.Combine(dir, "audio", "events.json");
        var bytes = File.ReadAllBytes(file);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]); // UTF-8 without a BOM
        var root = JsonDocument.Parse(Encoding.UTF8.GetString(bytes)).RootElement;
        Assert.Equal(AudioEventList.Format, root.GetProperty("format").GetInt32());
        var events = root.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(["event:/Creatures/Sh_TheropodLargeAudio/Vox/Roar", "event:/User Interface/Click"],
            events.Select(e => e.GetProperty("path").GetString()));
        var roar = events[0];
        Assert.Equal("{a}", roar.GetProperty("guid").GetString());
        Assert.Equal(1300, roar.GetProperty("lengthMs").GetInt32());
        Assert.True(roar.GetProperty("oneShot").GetBoolean());
        Assert.Equal("bank:/Shared_Creatures", roar.GetProperty("bank").GetString());
    }

    [Fact]
    public void An_event_in_two_banks_is_listed_once()
    {
        var dir = TempDir();
        AudioEventList.Write(dir, [
            new AudioEvent("event:/X", "{a}", 10, false, "bank:/One"),
            new AudioEvent("event:/X", "{a}", 10, false, "bank:/Two"),
        ]);

        var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "audio", "events.json"))).RootElement;
        Assert.Single(root.GetProperty("events").EnumerateArray());
    }

    [Fact]
    public void Quotes_and_unicode_in_paths_stay_valid_json()
    {
        var dir = TempDir();
        AudioEventList.Write(dir, [new AudioEvent("event:/Ambience/\"Wind\" é", "{c}", 0, false, "bank:/Amb")]);

        var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "audio", "events.json"))).RootElement;
        Assert.Equal("event:/Ambience/\"Wind\" é", root.GetProperty("events")[0].GetProperty("path").GetString());
    }
}
