using Tyrant.Core.Assets;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Core.Tests;

/// <summary>A data dump shaped like the real one (AnimalData whose skins $ref their variation assets) plus matching index records.</summary>
public static class SkinDumps
{
    public const string MaleDiffuse = "11111111111111111111111111111111";
    public const string MaleNormal = "22222222222222222222222222222222";
    public const string FemaleDiffuse = "33333333333333333333333333333333";

    public static readonly AssetRecord[] Textures =
    [
        new("carch.bundle", 1, "Texture2D", "T_carch_alt1_male_D", "Assets/T_carch_alt1_male_D.png", MaleDiffuse, null),
        new("carch.bundle", 2, "Texture2D", "T_carch_N", "Assets/T_carch_N.png", MaleNormal, null),
        new("carch.bundle", 3, "Texture2D", "T_carch_alt1_female_D", "Assets/T_carch_alt1_female_D.png", FemaleDiffuse, null),
    ];

    public static AssetIndex Index() => new() { Assets = [.. Textures] };

    public static void Write(string dataDir)
    {
        var result = new DumpResult();
        void Add(string type, string name, long id, string json) => result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo(type, name, id), json));
        const string Var = "PrehistoricKingdom.AnimalSkinVariationAsset";
        Add("PrehistoricKingdom.AnimalData", "Carcharodontosaurus", 1, $$$$"""
            {"$type":"PrehistoricKingdom.AnimalData","$name":"Carcharodontosaurus","$id":1,"speciesID":"Carcharodontosaurus",
             "skinsData":[
               {"skinName":"Base","maleVariationData":{"$ref":{"type":"{{{{Var}}}}","name":"Carch_Base_M","id":10}},"femaleVariationData":{"$ref":{"type":"{{{{Var}}}}","name":"Carch_Base_F","id":11}}},
               {"skinName":"Alt 1","maleVariationData":{"$ref":{"type":"{{{{Var}}}}","name":"Carch_Alt1_M","id":12}},"femaleVariationData":{"$ref":{"type":"{{{{Var}}}}","name":"Carch_Alt1_F","id":13}}}
             ]}
            """);
        Add(Var, "Carch_Base_M", 10, """{"$type":"x","$name":"Carch_Base_M","$id":10,"adultDiffuseMap":{"m_AssetGUID":"aaaa"}}""");
        Add(Var, "Carch_Base_F", 11, """{"$type":"x","$name":"Carch_Base_F","$id":11,"adultDiffuseMap":{"m_AssetGUID":"bbbb"}}""");
        Add(Var, "Carch_Alt1_M", 12, $$$"""{"$type":"x","$name":"Carch_Alt1_M","$id":12,"adultDiffuseMap":{"m_AssetGUID":"{{{MaleDiffuse}}}"},"adultNormalMap":{"m_AssetGUID":"{{{MaleNormal}}}"},"adultExtraMap":{"m_AssetGUID":""}}""");
        Add(Var, "Carch_Alt1_F", 13, $$$"""{"$type":"x","$name":"Carch_Alt1_F","$id":13,"adultDiffuseMap":{"m_AssetGUID":"{{{FemaleDiffuse}}}"}}""");
        Add("PrehistoricKingdom.VivariumAnimalData", "Frog", 2, """{"$type":"PrehistoricKingdom.VivariumAnimalData","$name":"Frog","$id":2,"speciesID":"Frog","skinsData":[{"skinName":"Green"}]}""");
        DumpWriter.Write(dataDir, result, [], new DumpManifest { RequestId = "r" });
    }
}
