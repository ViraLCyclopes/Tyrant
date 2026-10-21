using Tyrant.Core.Assets;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Core.Tests;

/// <summary>A data dump shaped like the real one (AnimalData whose skins $ref their variation assets) plus matching index records.</summary>
public static class SkinDumps
{
    public const string MaleDiffuse = "11111111111111111111111111111111";
    public const string MaleNormal = "22222222222222222222222222222222";
    public const string FemaleDiffuse = "33333333333333333333333333333333";
    public const string MalePattern = "44444444444444444444444444444444";
    public const string FemaleExtra = "55555555555555555555555555555555";
    public const string PrefabGuid = "09eb8efa8e623a043864dffadc868eb7";

    /// <summary>The species prefab AnimalData.animalRef points at.</summary>
    public static readonly AssetRecord Prefab = new("carch.bundle", 9, "GameObject", "Carcharodontosaurus", "Assets/Carcharodontosaurus.prefab", PrefabGuid, null);

    public static readonly AssetRecord[] Textures =
    [
        new("carch.bundle", 1, "Texture2D", "T_carch_alt1_male_D", "Assets/T_carch_alt1_male_D.png", MaleDiffuse, null),
        new("carch.bundle", 2, "Texture2D", "T_carch_N", "Assets/T_carch_N.png", MaleNormal, null),
        new("carch.bundle", 3, "Texture2D", "T_carch_alt1_female_D", "Assets/T_carch_alt1_female_D.png", FemaleDiffuse, null),
        new("carch.bundle", 4, "Texture2D", "T_carch_pattern", "Assets/T_carch_pattern.png", MalePattern, null),
        new("carch.bundle", 5, "Texture2D", "T_carch_extra", "Assets/T_carch_extra.png", FemaleExtra, null),
    ];

    public static AssetIndex Index() => new() { Assets = [.. Textures] };

    /// <summary>Carch_Alt1_M's albino block, shaped like the game's (a gradient key per colour, ranges as x/y).</summary>
    private const string Albino = """
        "albinoVariationData":{"minMaxHue":{"x":-0.1,"y":0.1},"minMaxVal":{"x":-0.2,"y":0.2},"minMaxSat":{"x":-0.3,"y":0},"iridescenceStrength":0,
         "minMaxOverrideBase":{"x":0.3,"y":0.4},"minMaxPatternFeather":{"x":0,"y":0.4},
         "patternAGradient":{"mode":"Blend","colorKeys":[{"time":0,"r":1,"g":0,"b":0,"a":1},{"time":1,"r":0,"g":0,"b":1,"a":1}],"alphaKeys":[]},
         "patternBGradient":{"mode":"Blend","colorKeys":[{"time":0,"r":0,"g":1,"b":0,"a":1},{"time":1,"r":0,"g":1,"b":0,"a":1}],"alphaKeys":[]},
         "eyeGradient":{"mode":"Blend","colorKeys":[{"time":0,"r":1,"g":0,"b":0,"a":1}],"alphaKeys":[]},
         "secondaryGradient":{"mode":"Blend","colorKeys":[{"time":0,"r":0.5,"g":0.5,"b":0.5,"a":1},{"time":1,"r":0.5,"g":0.5,"b":0.5,"a":1}],"alphaKeys":[]}}
        """;

    /// <summary>scaleGrowthCurve: the Carcharodontosaurus' scale curve keys as JSON ([{"time":0,"value":0.5},…]); null leaves it out.</summary>
    public static void Write(string dataDir, string? scaleGrowthCurve = null)
    {
        var result = new DumpResult();
        void Add(string type, string name, long id, string json) => result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo(type, name, id), json));
        const string Var = "PrehistoricKingdom.AnimalSkinVariationAsset";
        Add("PrehistoricKingdom.AnimalData", "Carcharodontosaurus", 1, $$$$"""
            {"$type":"PrehistoricKingdom.AnimalData","$name":"Carcharodontosaurus","$id":1,"speciesID":"Carcharodontosaurus","animalRef":{"m_AssetGUID":"{{{{PrefabGuid}}}}"},
             "blendGrowthCurve":{"keys":[{"time":0,"value":0,"inTangent":1,"outTangent":1},{"time":1,"value":1,"inTangent":1,"outTangent":1}]},
             "skinGrowthCurve":{"keys":[{"time":0,"value":0,"inTangent":1,"outTangent":1},{"time":1,"value":1,"inTangent":1,"outTangent":1}]},
             "blendShapesRelative":true,{{{{(scaleGrowthCurve is null ? "" : "\"scaleGrowthCurve\":{\"keys\":" + scaleGrowthCurve + "},")}}}}
             "GrowthData":{"bones":[
               {"transformName":"Arm.L","mode":"Scale",
                "localBabyTransformation":{"position":{"x":0.1,"y":0.3,"z":0},"scale":{"x":1.14,"y":1.14,"z":1.14}},
                "localAdolescentTransformation":{"position":{"x":0.1,"y":0.3,"z":0},"scale":{"x":1.07,"y":1.07,"z":1.07}},
                "localAdultTransformation":{"position":{"x":0.1,"y":0.3,"z":0},"scale":{"x":1.09,"y":1.09,"z":1.09}}},
               {"transformName":"Hip","mode":"All",
                "localBabyTransformation":{"position":{"x":0,"y":0.8,"z":0},"scale":{"x":1,"y":1,"z":1}},
                "localAdolescentTransformation":{"position":{"x":0,"y":0.9,"z":0},"scale":{"x":1,"y":1,"z":1}},
                "localAdultTransformation":{"position":{"x":0,"y":1,"z":0},"scale":{"x":1,"y":1,"z":1}}}]},
             "skinsData":[
               {"skinName":"Base","maleGrowthClamp":1,"femaleGrowthClamp":0.8,"maleSizeMultiplier":1.3,"femaleSizeMultiplier":1.35,"maleVariationData":{"$ref":{"type":"{{{{Var}}}}","name":"Carch_Base_M","id":10}},"femaleVariationData":{"$ref":{"type":"{{{{Var}}}}","name":"Carch_Base_F","id":11}}},
               {"skinName":"Alt 1","portraits":{"adultMale":{"m_AssetGUID":"abc"},"infant":{"m_AssetGUID":""}},"maleGrowthClamp":1,"femaleGrowthClamp":0.7,"maleSizeMultiplier":1.2,"femaleSizeMultiplier":1.25,"maleVariationData":{"$ref":{"type":"{{{{Var}}}}","name":"Carch_Alt1_M","id":12}},"femaleVariationData":{"$ref":{"type":"{{{{Var}}}}","name":"Carch_Alt1_F","id":13}}}
             ]}
            """);
        Add(Var, "Carch_Base_M", 10, """{"$type":"x","$name":"Carch_Base_M","$id":10,"adultDiffuseMap":{"m_AssetGUID":"aaaa"}}""");
        Add(Var, "Carch_Base_F", 11, """{"$type":"x","$name":"Carch_Base_F","$id":11,"adultDiffuseMap":{"m_AssetGUID":"bbbb"}}""");
        Add(Var, "Carch_Alt1_M", 12, $$$"""{"$type":"x","$name":"Carch_Alt1_M","$id":12,"adultDiffuseMap":{"m_AssetGUID":"{{{MaleDiffuse}}}"},"adultNormalMap":{"m_AssetGUID":"{{{MaleNormal}}}"},"adultPatternMap":{"m_AssetGUID":"{{{MalePattern}}}"},"adultExtraMap":{"m_AssetGUID":""},"defaultVariationData":{"iridescence":true,"iridescenceStrength":0.25},{{{Albino}}}}""");
        Add(Var, "Carch_Alt1_F", 13, $$$$"""{"$type":"x","$name":"Carch_Alt1_F","$id":13,"adultDiffuseMap":{"m_AssetGUID":"{{{{FemaleDiffuse}}}}"},"adultExtraMap":{"m_AssetGUID":"{{{{FemaleExtra}}}}"},"defaultVariationData":{"iridescence":false,"iridescenceStrength":1},"leucisticVariationData":{"patternAGradient":{"mode":"Blend","colorKeys":[{"time":0,"r":1,"g":1,"b":1,"a":1}]}}}""");
        Add("PrehistoricKingdom.VivariumAnimalData", "Frog", 2, """{"$type":"PrehistoricKingdom.VivariumAnimalData","$name":"Frog","$id":2,"speciesID":"Frog","skinsData":[{"skinName":"Green"}]}""");
        DumpWriter.Write(dataDir, result, [], new DumpManifest { RequestId = "r" });
    }
}
