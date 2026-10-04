using Tyrant.Dumper.Serialization;

namespace Tyrant.Core.Tests;

/// <summary>A data dump with species audio databases (the real shapes) and, optionally, the FMOD event list.</summary>
public static class SoundDumps
{
    public const string Theropod = "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/_TheroLarge_Comp";
    public const string Roar = Theropod + "/Vox/TheroLarge_VoxSocialCall";
    public const string Growl = Theropod + "/Growls/Growls_Vox/TheroLarge_Growl_OneShot_A";
    public const string Breath = "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/__TheropodLarge_Core (-28 LUFS)/TheroLarge_CoreV2/TheroLarge_Breath_Sleep_In";
    public const string Step = "event:/AnimalShared Master/_Creatures/Footsteps/Main/TheroLarge_Footstep";
    public const string Swim = "event:/AnimalShared Master/_Creatures/Body (-28 LUFS)/Swim/C_Swim_Biped";
    public const string Hit = "event:/AnimalShared Master/_Creatures/Combat/Hits/C_Hit_Carnivore";
    public const string Nursery = "event:/Ambience Nursery Animals/Dinosaurs/Nursery_Carch";
    public const string AcroCall = "event:/AnimalFamily Master/Dinosaurs/Acrocanthosaurus/Vox/Acro_VoxBroadcast";
    public const string Click = "event:/User Interface/Buttons/UI_Click";

    /// <summary>Carch and Acro share the large theropod database; Acro also has its own; Carch has a hit and a nursery event.</summary>
    public static void Write(string dir, bool withEventList = true)
    {
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
    }

}
