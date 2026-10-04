using Tyrant.Core.Sounds;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Core.Tests;

public class SoundCatalogTests
{
    private const string Theropod = "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/_TheroLarge_Comp";
    private const string Roar = Theropod + "/Vox/TheroLarge_VoxSocialCall";
    private const string Growl = Theropod + "/Growls/Growls_Vox/TheroLarge_Growl_OneShot_A";
    private const string Breath = "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/__TheropodLarge_Core (-28 LUFS)/TheroLarge_CoreV2/TheroLarge_Breath_Sleep_In";
    private const string Step = "event:/AnimalShared Master/_Creatures/Footsteps/Main/TheroLarge_Footstep";
    private const string Swim = "event:/AnimalShared Master/_Creatures/Body (-28 LUFS)/Swim/C_Swim_Biped";
    private const string Hit = "event:/AnimalShared Master/_Creatures/Combat/Hits/C_Hit_Carnivore";
    private const string Nursery = "event:/Ambience Nursery Animals/Dinosaurs/Nursery_Carch";
    private const string AcroCall = "event:/AnimalFamily Master/Dinosaurs/Acrocanthosaurus/Vox/Acro_VoxBroadcast";
    private const string Click = "event:/User Interface/Buttons/UI_Click";

    /// <summary>Carch and Acro share the large theropod database; Acro also has its own; Carch has a hit and a nursery event.</summary>
    private static string Dump(bool withEventList = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "data");
        var result = new DumpResult();
        void Add(string type, string name, long id, string json) => result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo(type, name, id), json));
        const string Db = "PrehistoricKingdom.AnimalAudioDatabase";
        static string Container(string name, params string[] events) =>
            $$"""{"AudioEventContainerName":"{{name}}","AudioEvents":[{{string.Join(",", events.Select(e => $$"""{"EventName":"x","AudioEvent":"{{e}}"}"""))}}]}""";
        Add(Db, "Sh_TheropodLargeAudio", 100,
            $$"""{"$type":"{{Db}}","$name":"Sh_TheropodLargeAudio","$id":100,"AudioEventContainers":[{{Container("Vox Main", Roar, Growl)}},{{Container("Breathing", Breath, "")}},{{Container("Steps", Step)}}]}""");
        Add(Db, "AnimalSwimmingAudio_Biped", 101, $$"""{"$type":"{{Db}}","$name":"AnimalSwimmingAudio_Biped","$id":101,"AudioEventContainers":[{{Container("Swim", Swim)}}]}""");
        Add(Db, "AcrocanthosaurusAudio", 102, $$"""{"$type":"{{Db}}","$name":"AcrocanthosaurusAudio","$id":102,"AudioEventContainers":[{{Container("Vox", AcroCall)}}]}""");
        static string Ref(string name, long id) => $$"""{"$ref":{"type":"PrehistoricKingdom.AnimalAudioDatabase","name":"{{name}}","id":{{id}} } }""";
        Add("PrehistoricKingdom.AnimalData", "Carcharodontosaurus", 1,
            $$"""{"$type":"PrehistoricKingdom.AnimalData","$name":"Carcharodontosaurus","$id":1,"speciesID":"Carcharodontosaurus","animalHitEventAudio":"{{Hit}}","NurseryShot":"{{Nursery}}","NurseryLoop":"","AudioDatabases":[{{Ref("Sh_TheropodLargeAudio", 100)}},{{Ref("AnimalSwimmingAudio_Biped", 101)}}]}""");
        Add("PrehistoricKingdom.AnimalData", "Acrocanthosaurus", 2,
            $$"""{"$type":"PrehistoricKingdom.AnimalData","$name":"Acrocanthosaurus","$id":2,"speciesID":"Acrocanthosaurus","AudioDatabases":[{{Ref("Sh_TheropodLargeAudio", 100)}},{{Ref("AcrocanthosaurusAudio", 102)}},null]}""");
        DumpWriter.Write(dir, result, [], new DumpManifest { RequestId = "r" });
        if (withEventList)
            AudioEventList.Write(dir, [
                new AudioEvent(Roar, "{1}", 2400, true, "bank:/Shared"),
                new AudioEvent(Click, "{2}", 90, true, "bank:/UI"),
                new AudioEvent(Breath, "{3}", 0, false, "bank:/Shared"),
            ]);
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
