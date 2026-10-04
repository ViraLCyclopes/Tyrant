using Tyrant.Core.Sounds;
using static Tyrant.Core.Tests.SoundDumps;

namespace Tyrant.Core.Tests;

public class SoundCatalogTests
{
    private static string Dump(bool withEventList = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        SoundDumps.Write(dir, withEventList);
        return dir;
    }

    [Fact]
    public void A_species_lists_its_databases_and_event_fields_with_everyone_sharing_each_sound()
    {
        var catalog = SoundCatalog.Read(Dump());

        var carch = catalog.ForSpecies("Carcharodontosaurus");

        Assert.Equal(new[] { Roar, Growl, Breath, Step, Swim, Hit, Nursery }.Order(), carch.Select(s => s.Event).Order());
        Assert.Equal(["Acrocanthosaurus", "Carcharodontosaurus"], carch.Single(s => s.Event == Roar).Species);
        Assert.Equal(["Carcharodontosaurus"], carch.Single(s => s.Event == Hit).Species);
        Assert.DoesNotContain(carch, s => s.Event == AcroCall);
    }

    [Fact]
    public void A_species_list_is_sorted_by_group_then_name()
    {
        var carch = SoundCatalog.Read(Dump()).ForSpecies("carcharodontosaurus");

        var keys = carch.Select(s => (s.Group, s.Name)).ToList();
        Assert.Equal(keys.OrderBy(k => k.Group, StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Name, StringComparer.OrdinalIgnoreCase), keys);
    }

    [Fact]
    public void The_event_list_adds_length_and_one_shot()
    {
        var catalog = SoundCatalog.Read(Dump());

        var roar = catalog.Find(Roar)!;
        Assert.True(catalog.HasEventList);
        Assert.Equal(2400, roar.LengthMs);
        Assert.True(roar.OneShot);
        Assert.False(catalog.Find(Breath)!.OneShot);
        Assert.Null(catalog.Find(Step)!.LengthMs);
    }

    [Theory]
    [InlineData(Roar, "Calls", "Social call")]
    [InlineData(Growl, "Growls", "Growl one shot a")]
    [InlineData(Breath, "Breathing", "Breath sleep in")]
    [InlineData(Step, "Footsteps", "Footstep")]
    [InlineData(Swim, "Body", "Swim biped")]
    [InlineData(Hit, "Combat", "Hit carnivore")]
    [InlineData("event:/AnimalFamily Master/Dinosaurs/X/Foley/Nourishment/Drink/X_DrinkLoop", "Eating & drinking", "Drink loop")]
    [InlineData("event:/AnimalFamily Master/Dinosaurs/X/Foley/Breath/Sniffing/TheroLarge_SniffA", "Breathing", "Sniff a")]
    [InlineData(Click, "Interface", "Click")]
    [InlineData("event:/Music/Menu/MainTheme", "Music", "Main theme")]
    [InlineData(Nursery, "Ambience", "Carch")]
    [InlineData("event:/Misc/Thing", "Other", "Thing")]
    public void Groups_and_names_come_from_the_path(string path, string group, string name)
    {
        Assert.Equal(group, SoundCatalog.GroupOf(path));
        Assert.Equal(name, SoundCatalog.NameOf(path));
    }

    [Fact]
    public void Search_finds_interface_sounds_from_the_event_list()
    {
        var found = SoundCatalog.Read(Dump()).Search("click");

        var click = Assert.Single(found);
        Assert.Equal(Click, click.Event);
        Assert.Empty(click.Species);
    }

    [Fact]
    public void Without_the_event_list_search_still_finds_database_sounds()
    {
        var catalog = SoundCatalog.Read(Dump(withEventList: false));

        Assert.False(catalog.HasEventList);
        Assert.Contains(catalog.Search("social"), s => s.Event == Roar);
        Assert.Empty(catalog.Search("click"));
        Assert.Equal(8, catalog.Search(null).Count);
        Assert.Equal(3, catalog.Search(null, limit: 3).Count);
    }

    [Fact]
    public void A_damaged_event_list_counts_as_missing()
    {
        var dir = Dump();
        File.WriteAllText(Path.Combine(dir, "audio", "events.json"), "{ not json");

        var catalog = SoundCatalog.Read(dir);

        Assert.False(catalog.HasEventList);
        Assert.NotEmpty(catalog.ForSpecies("Carcharodontosaurus"));
    }

    [Fact]
    public void An_unknown_species_has_no_sounds()
    {
        Assert.Empty(SoundCatalog.Read(Dump()).ForSpecies("Unicorn"));
        Assert.Null(SoundCatalog.Read(Dump()).Find("event:/Nope"));
    }
}
