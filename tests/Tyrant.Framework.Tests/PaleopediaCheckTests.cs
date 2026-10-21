using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PaleopediaCheckTests
{
    [Theory]
    [InlineData("Ultimasaurus", "anax", true)]
    [InlineData("ULTIMASAURUS", "Anax", true)] // the header may be upper-cased
    [InlineData("  Ultimasaurus ", "anax\n", true)]
    [InlineData("<b>Ultimasaurus</b>", "<i>anax</i>", true)] // rich text tags are not part of the name
    [InlineData("Allosaurus", "anax", false)] // the template's page
    [InlineData("Ultimasaurus", "rex", false)]
    [InlineData(null, "anax", false)]
    [InlineData("", "", false)]
    public void The_header_shows_the_species_only_when_genus_and_species_match(string? genus, string? species, bool expected) =>
        Assert.Equal(expected, PaleopediaCheck.ShowsSpecies(genus, species, "Ultimasaurus", "anax"));

    [Fact]
    public void The_shown_name_reads_like_the_header()
    {
        Assert.Equal("Allosaurus anax", PaleopediaCheck.Shown("<b>Allosaurus</b> ", " anax"));
        Assert.Equal("(empty header)", PaleopediaCheck.Shown(null, " "));
    }

    [Fact]
    public void The_call_site_keeps_the_first_game_frames_and_skips_the_recorded_method_and_mod_frames()
    {
        var frames = new (string?, string)[]
        {
            ("Tyrant.Framework.SpeciesPaleopedia", "BeforeSetAnimalScreen"),
            (null, "DMD<PrehistoricKingdom.PaleopediaAnimalScreen::SetAnimalScreen>"),
            ("PrehistoricKingdom.PaleopediaManager", "SetSelectedAnimalScreen"),
            (null, "DMD<PrehistoricKingdom.NurseryMenuV2::OpenPaleopedia>"), // a patched method on the stack
            ("UnityEngine.Events.InvokableCall", "Invoke"),
            ("PrehistoricKingdom.InterfaceManager", "Update"),
            ("PrehistoricKingdom.Game", "Update"),
        };
        Assert.Equal("PaleopediaManager.SetSelectedAnimalScreen < NurseryMenuV2.OpenPaleopedia < InterfaceManager.Update",
            PaleopediaCheck.CallSite(frames, "SetAnimalScreen"));
        Assert.Equal("no game method", PaleopediaCheck.CallSite([("UnityEngine.UI.Button", "Press")], "SetAnimalScreen"));
    }

    [Fact]
    public void Each_case_is_logged_once_per_species()
    {
        var once = new PaleopediaCheck.OncePerCase();
        Assert.True(once.First("mismatch", "tyrant.a.ultimasaurus"));
        Assert.False(once.First("mismatch", "tyrant.a.ultimasaurus")); // the next press says nothing
        Assert.True(once.First("mismatch", "tyrant.b.other")); // another species still logs
        Assert.True(once.First("own page", "tyrant.a.ultimasaurus")); // and so does another case
        Assert.False(once.First("own page", "tyrant.a.ultimasaurus"));
    }
}
