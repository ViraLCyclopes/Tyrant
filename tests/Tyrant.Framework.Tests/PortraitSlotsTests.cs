using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PortraitSlotsTests
{
    [Theory]
    [InlineData(2, true, 0, "adultMale")]
    [InlineData(1, true, 1, "adultMaleAlbino")]      // adolescents use the adult pictures, as the game does
    [InlineData(2, false, 2, "adultFemaleMelanistic")]
    [InlineData(2, false, 3, "adultFemaleLeucistic")]
    [InlineData(0, true, 0, "infant")]
    [InlineData(0, false, 3, "infantLeucistic")]       // infants have one picture per morph, either sex
    [InlineData(3, true, 0, null)]                     // a stage the game does not have
    [InlineData(2, true, 4, null)]                     // a morph the game does not have
    public void The_slot_is_the_one_the_game_reads(int stage, bool male, int morph, string? slot)
    {
        Assert.Equal(slot, PortraitSlots.For(stage, male, morph));
    }

    [Fact]
    public void There_are_twelve_slots_named_like_the_games_fields()
    {
        Assert.Equal(12, PortraitSlots.All.Length);
        Assert.True(PortraitSlots.IsSlot("infantAlbino"));
        Assert.False(PortraitSlots.IsSlot("AdultMale"));
        Assert.Equal("adultMale.render.png", PortraitSlots.FileName("adultMale", rendered: true));
        Assert.True(PortraitSlots.IsRendered("skins/a/portraits/adultMale.render.png"));
        Assert.False(PortraitSlots.IsRendered("skins/a/portraits/adultMale.png"));
    }

    [Theory]
    [InlineData(128, 64, true)]
    [InlineData(256, 128, true)]
    [InlineData(130, 64, true)]   // within 2%
    [InlineData(128, 128, false)]
    [InlineData(0, 0, false)]
    public void Two_to_one_allows_two_percent(int w, int h, bool expected)
    {
        Assert.Equal(expected, PortraitSlots.IsTwoToOne(w, h));
    }

    [Theory]
    [InlineData(128, 16f)]
    [InlineData(256, 32f)]
    [InlineData(64, 8f)]
    public void Pixels_per_unit_keep_the_games_world_size(int width, float ppu)
    {
        Assert.Equal(ppu, PortraitSlots.PixelsPerUnit(width));
    }

    [Fact]
    public void A_skins_portraits_round_trip_in_slot_order()
    {
        var m = ModManifest.Parse("""
            { "format": 1, "id": "red-spot-carcharo", "skins": [ { "id": "red-spot", "species": "Carcharodontosaurus",
              "male": { "diffuse": "skins/red-spot/male_D.png" },
              "portraits": { "infant": "skins/red-spot/portraits/infant.png", "adultMale": "skins/red-spot/portraits/adultMale.render.png" } } ] }
            """);

        Assert.Equal("skins/red-spot/portraits/infant.png", m.Skins[0].Portraits!["infant"]);
        var json = m.ToJson();
        Assert.True(json.IndexOf("\"adultMale\"", StringComparison.Ordinal) < json.IndexOf("\"infant\"", StringComparison.Ordinal));
        Assert.Equal(m.Skins[0].Portraits, ModManifest.Parse(json).Skins[0].Portraits);
    }

    [Fact]
    public void An_unknown_portrait_slot_is_an_error_naming_the_slots()
    {
        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse("""
            { "format": 1, "id": "red-spot-carcharo", "skins": [ { "id": "red-spot", "species": "Carcharodontosaurus",
              "male": { "diffuse": "a.png" }, "portraits": { "adult": "b.png" } } ] }
            """));
        Assert.Contains("adultMale", ex.Message);
    }
}
