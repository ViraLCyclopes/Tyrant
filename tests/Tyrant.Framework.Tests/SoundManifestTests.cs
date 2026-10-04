using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SoundManifestTests
{
    private const string Call = "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/_TheroLarge_Comp/Vox/TheroLarge_VoxSocialCall";

    private static string Manifest(string sounds) =>
        "{\"format\":1,\"id\":\"carch-voice\",\"name\":\"Carch voice\",\"version\":\"1.0.0\",\"replace\":[],\"sounds\":" + sounds + "}";

    private static readonly string Two = Manifest(
        "[{\"event\":\"" + Call + "\",\"species\":\"Carcharodontosaurus\",\"files\":[\"sounds/carch-call-1.ogg\",\"sounds/carch-call-2.ogg\"],\"volume\":0.8,\"agePitch\":0.5}," +
        "{\"event\":\"event:/User Interface/Buttons/ClickDefault\",\"files\":[\"sounds/click.wav\"]}]");

    [Fact]
    public void Sounds_are_read_with_their_scope_files_and_settings()
    {
        var m = ModManifest.Parse(Two);

        Assert.Equal(2, m.Sounds.Count);
        var call = m.Sounds[0];
        Assert.Equal((Call, "Carcharodontosaurus", (string?)null), (call.Event, call.Species, call.Skin));
        Assert.Equal(["sounds/carch-call-1.ogg", "sounds/carch-call-2.ogg"], call.Files);
        Assert.Equal((0.8, 0.5), (call.Volume, call.AgePitch));
        Assert.True(call.IsUnique);
        var click = m.Sounds[1];
        Assert.False(click.IsUnique);
        Assert.Equal((1.0, 1.0), (click.Volume, click.AgePitch));
    }

    [Fact]
    public void Sounds_survive_a_round_trip_and_defaults_are_not_written()
    {
        var json = ModManifest.Parse(Two).ToJson();
        var again = ModManifest.Parse(json);

        Assert.Equal(2, again.Sounds.Count);
        Assert.Equal("Carcharodontosaurus", again.Sounds[0].Species);
        Assert.Equal(0.8, again.Sounds[0].Volume);
        Assert.DoesNotContain("\"volume\": 1", json);
        Assert.DoesNotContain("\"agePitch\": 1", json);
    }

    [Fact]
    public void A_mod_without_sounds_writes_none()
    {
        var json = ModManifest.Parse(Manifest("[]")).ToJson();
        Assert.DoesNotContain("\"sounds\"", json);
    }

    [Theory]
    [InlineData("[{\"files\":[\"a.wav\"]}]", "no \"event\"")]
    [InlineData("[{\"event\":\"event:/X\",\"files\":[]}]", "no files")]
    [InlineData("[{\"event\":\"event:/X\"}]", "no files")]
    [InlineData("[{\"event\":\"event:/X\",\"species\":\"A\",\"skin\":\"m/s\",\"files\":[\"a.wav\"]}]", "both")]
    public void A_bad_sound_entry_is_an_error(string sounds, string message)
    {
        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse(Manifest(sounds)));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void In_the_game_a_bad_sound_entry_is_skipped_and_the_rest_kept()
    {
        var skipped = new List<string>();

        var m = ModManifest.Parse(Manifest("[{\"event\":\"event:/X\"},{\"event\":\"event:/Y\",\"files\":[\"y.wav\"]}]"), skipped);

        Assert.Equal("event:/Y", Assert.Single(m.Sounds).Event);
        Assert.Contains(skipped, s => s.Contains("\"sounds\" entry 1"));
    }

    [Fact]
    public void Volume_and_age_pitch_are_kept_in_range()
    {
        var m = ModManifest.Parse(Manifest("[{\"event\":\"event:/X\",\"files\":[\"a.wav\"],\"volume\":5,\"agePitch\":-1}]"));
        Assert.Equal((2.0, 0.0), (m.Sounds[0].Volume, m.Sounds[0].AgePitch));
    }

    [Theory]
    [InlineData("0.3", 0.3)]
    [InlineData("1.7", 1.0)]
    [InlineData("-1", 0.0)]
    public void A_sound_can_set_its_own_chance_kept_in_range(string chance, double expected)
    {
        var m = ModManifest.Parse(Manifest("[{\"event\":\"event:/X\",\"files\":[\"a.wav\"],\"chance\":" + chance + "}]"));

        Assert.Equal(expected, m.Sounds[0].Chance);
    }

    [Fact]
    public void Without_a_chance_the_sound_follows_the_game_and_none_is_written()
    {
        var m = ModManifest.Parse(Two);
        Assert.Null(m.Sounds[0].Chance);
        Assert.DoesNotContain("chance", m.ToJson());

        m.Sounds[0].Chance = 0.25;
        Assert.Equal(0.25, ModManifest.Parse(m.ToJson()).Sounds[0].Chance);
    }
}
