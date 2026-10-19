using Tyrant.Core.Assets;
using Tyrant.Core.Data;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Dumper.Serialization;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class GamePresetsTests
{
    private const string Var = "PrehistoricKingdom.AnimalSkinVariationAsset";

    /// <summary>A dump with a shimmering species (its second skin's male asset), a furred one and a plain one, shaped like the game's.</summary>
    internal static IReadOnlyList<SpeciesSkins> Dump()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        var result = new DumpResult();
        void Add(string type, string name, long id, string json) => result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo(type, name, id), json));
        string Skin(string name, long male, long female) =>
            "{\"skinName\":\"" + name + "\",\"maleVariationData\":" + Ref(male) + ",\"femaleVariationData\":" + Ref(female) + "}";
        string Ref(long id) => "{\"$ref\":{\"type\":\"" + Var + "\",\"name\":\"v" + id + "\",\"id\":" + id + "}}";
        void Species(string id, long dumpId, string? displayName, params string[] skins) =>
            Add("PrehistoricKingdom.AnimalData", id, dumpId,
                $$"""{"$type":"PrehistoricKingdom.AnimalData","$name":"{{id}}","$id":{{dumpId}},"speciesID":"{{id}}"{{(displayName is null ? "" : $",\"displayName\":\"{displayName}\"")}},"skinsData":[{{string.Join(",", skins)}}]}""");
        Species("Microraptor", 1, "Microraptor", Skin("Base", 10, 11), Skin("Alt", 12, 13));
        Species("Mammoth", 2, "Woolly mammoth", Skin("Base", 20, 21));
        Species("Allosaurus", 3, null, Skin("Base", 30, 31));
        Add(Var, "v10", 10, """{"$id":10,"defaultVariationData":{"iridescence":false,"iridescenceStrength":0.9}}""");
        Add(Var, "v11", 11, """{"$id":11}""");
        Add(Var, "v12", 12, """{"$id":12,"defaultVariationData":{"iridescence":true,"iridescenceStrength":0.25},"albinoVariationData":{"iridescenceStrength":0.1},"melanisticVariationData":{"iridescenceStrength":0.25}}""");
        Add(Var, "v13", 13, """{"$id":13,"defaultVariationData":{"iridescence":true,"iridescenceStrength":0.8}}""");
        Add(Var, "v20", 20, """{"$id":20,"adultFurMap":{"m_AssetGUID":""},"adultFuzzDroopiness":{"x":5,"y":5,"z":5}}""");
        Add(Var, "v21", 21, """{"$id":21,"adultFurMap":{"m_AssetGUID":"abcdefabcdefabcdefabcdefabcdefab"},"adultFuzzDroopiness":{"x":0,"y":-10.3,"z":8.11}}""");
        Add(Var, "v30", 30, """{"$id":30,"defaultVariationData":{"iridescence":false,"iridescenceStrength":0}}""");
        Add(Var, "v31", 31, """{"$id":31}""");
        DumpWriter.Write(ws.DataDir, result, [], new DumpManifest { RequestId = "r" });
        return SpeciesSkinsReader.Read(DataStore.Open(ws));
    }

    private static AnimalMaterial Material(bool ramp, bool mask) =>
        new("Microraptor", new Dictionary<string, float> { ["_IridescenceScale"] = -0.25f, ["_IridescencePower"] = -0.3f, ["_IridescenceBias"] = 1.3f, ["_Length"] = 1.2f },
            new Dictionary<string, float[]>(), ramp ? new AssetRecord("m.bundle", 7, "Texture2D", "Ramp", null, "feed", null) : null,
            mask ? new AssetRecord("m.bundle", 8, "Texture2D", "Mask", null, "beef", null) : null);

    [Fact]
    public void The_dump_lists_shimmering_and_furred_species_by_their_names()
    {
        var species = Dump();

        Assert.Equal([("Microraptor", "Microraptor")], GamePresets.List(species, PresetKind.Iridescence));
        Assert.Equal([("Mammoth", "Woolly mammoth")], GamePresets.List(species, PresetKind.Fur));
        Assert.Equal("Allosaurus", species.Single(s => s.SpeciesId == "Allosaurus").Name); // no displayName: the id, as the game shows it
    }

    [Fact]
    public void A_preset_takes_the_first_shimmering_skin_and_the_species_material()
    {
        var micro = Dump().Single(s => s.SpeciesId == "Microraptor");

        var preset = GamePresets.Of(micro, Material(ramp: true, mask: false));

        Assert.True(preset.Shimmers);
        Assert.Equal(0.25f, preset.Strength!.Normal); // Alt's male asset: the Base skin does not shimmer
        Assert.Equal(0.1f, preset.Strength.Albino); // a mutation's own strength where it differs
        Assert.Null(preset.Strength.Melanistic);
        Assert.True(preset.HasRamp);
        Assert.False(preset.HasMask);
        Assert.Equal(-0.25f, preset.Floats["_IridescenceScale"]);
    }

    [Fact]
    public void A_fur_preset_takes_the_first_furred_skin_s_droop()
    {
        var mammoth = Dump().Single(s => s.SpeciesId == "Mammoth");

        var preset = GamePresets.Of(mammoth, null);

        Assert.True(preset.Furry);
        Assert.Equal([0f, -10.3f, 8.11f], preset.Droop!); // the female asset: the male one names no fur map
        Assert.Empty(preset.Floats); // no material read: no numbers to copy
    }

    [Fact]
    public void A_preset_fragment_is_the_block_as_materials_json_writes_it()
    {
        var micro = Dump().Single(s => s.SpeciesId == "Microraptor");

        var preset = GamePresets.Of(micro, Material(ramp: true, mask: true));
        var json = GamePresets.FragmentJson(preset, PresetKind.Iridescence, "Microraptor");

        Assert.Equal(Json.Write(Json.Parse("""
            {"iridescence":{"strength":{"normal":0.25,"albino":0.1},"ramp":{"species":"Microraptor"},"mask":{"species":"Microraptor"},
             "scale":-0.25,"power":-0.3,"bias":1.3}}
            """)), json);
        // On another species the mask is everywhere: Microraptor's is painted for Microraptor's UV layout.
        var other = (Dictionary<string, object?>)((Dictionary<string, object?>)Json.Parse(GamePresets.FragmentJson(preset, PresetKind.Iridescence, "Titanoboa"))!)["iridescence"]!;
        Assert.Equal("everywhere", other["mask"]);
    }
}
